using FluentAssertions;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Endpoints;

public class LeagueEventsPaginationTests
{
    // Tied dates expose a missing Id tiebreaker.
    private const int EventsPerDate = 5;

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Harness(SqliteConnection connection, SportarrDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public SportarrDbContext Db { get; }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();
            return new Harness(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class PausePageQueryInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> _pageRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releasePage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pause;

        public Task PageRequested => _pageRequested.Task;

        public void PauseNextPage() => Interlocked.Exchange(ref _pause, 1);

        public void ReleasePage() => _releasePage.TrySetResult(true);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _pause, 0) == 1)
            {
                _pageRequested.TrySetResult(true);
                await _releasePage.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private static async Task<int> SeedKeepAllLeagueAsync(
        SportarrDbContext db, int eventCount, Func<int, string?>? seasonFor = null)
    {
        seasonFor ??= _ => "2025-26";

        db.Leagues.Add(new League
        {
            Id = 1,
            Name = "NBA",
            Sport = "Basketball",
            KeepAllEvents = true,
        });
        db.Teams.Add(new Team { Id = 1, ExternalId = "tm-home", Name = "Home Side", LeagueId = 1, Sport = "Basketball" });
        db.Teams.Add(new Team { Id = 2, ExternalId = "tm-away", Name = "Away Side", LeagueId = 1, Sport = "Basketball" });

        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= eventCount; i++)
        {
            db.Events.Add(new Event
            {
                Id = i,
                Title = $"Game {i}",
                Sport = "Basketball",
                LeagueId = 1,
                Season = seasonFor(i),
                Round = "1",
                HomeTeamId = 1,
                AwayTeamId = 2,
                HomeTeamExternalId = "tm-home",
                AwayTeamExternalId = "tm-away",
                HomeTeamName = "Home Side",
                AwayTeamName = "Away Side",
                EventDate = baseDate.AddDays(i / EventsPerDate),
                HasFile = i % 10 == 0,
            });

            if (i % 10 == 0)
            {
                db.EventFiles.Add(new EventFile
                {
                    EventId = i,
                    FilePath = $"/media/game-{i}.mkv",
                    Size = 1000,
                    Exists = true,
                });
            }
        }

        await db.SaveChangesAsync();
        return 1;
    }

    [Fact]
    public async Task UnfilteredPageQuery_CutsItsWindowInSqlWithTheIdTiebreaker()
    {
        await using var harness = await Harness.CreateAsync();

        var (_, pageQuery) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(harness.Db, 1, null, currentPage: 3, size: 100);
        var sql = pageQuery.ToQueryString();

        sql.Should().Contain("LIMIT",
            "the page window must be cut in the database instead of loading the whole league per page");
        sql.Should().Contain("OFFSET",
            "page 3 must skip the first two pages server-side");

        // EF adds an outer ORDER BY for the joins. Check the window's order.
        var limitIndex = sql.IndexOf("LIMIT", StringComparison.OrdinalIgnoreCase);
        var windowOrderIndex = sql.LastIndexOf("ORDER BY", limitIndex, StringComparison.OrdinalIgnoreCase);
        windowOrderIndex.Should().BeGreaterThan(-1,
            "the page window's own ORDER BY precedes its LIMIT");
        // Strip identifier quotes before matching column names.
        var windowOrderBy = sql[windowOrderIndex..limitIndex].Replace("\"", "");

        var dateIndex = windowOrderBy.IndexOf("EventDate", StringComparison.OrdinalIgnoreCase);
        var idIndex = windowOrderBy.IndexOf("Id", StringComparison.OrdinalIgnoreCase);
        dateIndex.Should().BeGreaterThan(-1, "the page window is ordered by event date");
        idIndex.Should().BeGreaterThan(-1, "the Id tiebreaker must be part of the page window's ordering");
        dateIndex.Should().BeLessThan(idIndex,
            "the Id tiebreaker must follow EventDate so page boundaries stay deterministic when dates tie");
        windowOrderBy[dateIndex..idIndex].Should().Contain("DESC", "newest events come first");
        windowOrderBy[(idIndex + "Id".Length)..].Trim().Should().Be("DESC",
            "the page window's ordering must end with the Id DESC tiebreaker");
    }

    [Fact]
    public async Task UnfilteredPageQuery_SeeksItsWindowThroughTheLeagueEventsIndex()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Db.Database.GetMigrations().Should().Contain("20261009222658_AddLeagueEventsCompositeIndex");
        await harness.Db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Events_LeagueId_EventDate_Id");
        var migration = new Sportarr.Api.Migrations.AddLeagueEventsCompositeIndex();
        var commands = harness.Db.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.UpOperations, harness.Db.Model);
        foreach (var command in commands)
            await harness.Db.Database.ExecuteSqlRawAsync(command.CommandText);
        const int total = 2500;
        var leagueId = await SeedKeepAllLeagueAsync(harness.Db, total);

        // Check the inner window. EF may sort the returned page outside it.
        var (_, pageQuery) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(harness.Db, leagueId, null, currentPage: 3, size: 100);
        var sql = pageQuery.ToQueryString();

        var fromIndex = sql.IndexOf("FROM (", StringComparison.OrdinalIgnoreCase);
        fromIndex.Should().BeGreaterThan(-1, "EF pushes the page window into a subquery");
        var innerStart = fromIndex + "FROM (".Length;
        var depth = 1;
        var scan = innerStart;
        while (scan < sql.Length && depth > 0)
        {
            if (sql[scan] == '(')
            {
                depth++;
            }
            else if (sql[scan] == ')')
            {
                depth--;
            }
            scan++;
        }
        depth.Should().Be(0, "the window subquery's parentheses must close inside the captured SQL");
        var innerEnd = scan - 1;
        innerEnd.Should().BeGreaterThan(innerStart, "the window subquery closes before the joins begin");
        var window = sql[innerStart..innerEnd];

        // The inner window determines the page's index use.
        window.Should().Contain("LeagueId", "the page window filters by league");
        window.Should().Contain("EventDate", "the page window orders by event date");
        window.Should().Contain("LIMIT", "the page window cuts its page in SQL");

        var connection = (SqliteConnection)harness.Db.Database.GetDbConnection();
        var plan = await ExplainQueryPlanAsync(connection, window);
        var planText = string.Join("\n", plan);

        plan.Should().Contain(line =>
                line.Contains("SEARCH", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("IX_Events_LeagueId_EventDate_Id", StringComparison.Ordinal) &&
                line.Contains("LeagueId=", StringComparison.Ordinal),
            "the page window must seek the composite league events index by league, not scan it. Plan:\n" + planText);
        plan.Should().NotContain(line => line.Contains("TEMP B-TREE", StringComparison.OrdinalIgnoreCase),
            "the composite index must serve the window's ordering instead of re-sorting the league per page. Plan:\n" + planText);
    }

    [Fact]
    public async Task HugePage_ClampsItsOffsetInsteadOfOverflowing()
    {
        await using var harness = await Harness.CreateAsync();
        const int total = 2500;
        var leagueId = await SeedKeepAllLeagueAsync(harness.Db, total);

        // The largest page can overflow an int offset before it reaches SQL.
        var (currentPage, size) = LeagueEndpoints.NormalizeLeagueEventsPaging(int.MaxValue, 1000);
        var (_, pageQuery) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(harness.Db, leagueId, null, currentPage, size);

        var sql = pageQuery.ToQueryString();
        var offsetIndex = sql.IndexOf("OFFSET", StringComparison.OrdinalIgnoreCase);
        offsetIndex.Should().BeGreaterThan(-1, "the composed page query always carries an OFFSET");
        // EF declares the offset value in a ".param set" line.
        var offsetParameter = sql[(offsetIndex + "OFFSET".Length)..].TrimStart()
            .Split(' ', ',', ')', '\r', '\n', ';')[0];
        var declarationPrefix = $".param set {offsetParameter} ";
        var declarationIndex = sql.IndexOf(declarationPrefix, StringComparison.Ordinal);
        declarationIndex.Should().BeGreaterThan(-1,
            "ToQueryString declares the offset parameter's value in a .param set line");
        var offset = int.Parse(sql[(declarationIndex + declarationPrefix.Length)..].Split('\r', '\n')[0]);
        offset.Should().BeGreaterOrEqualTo(0,
            "the page offset must never overflow int into a negative SQL OFFSET");

        // The clamped offset must return an empty page.
        var records = await pageQuery.ToListAsync();
        records.Should().BeEmpty("a page beyond the last row is empty, not a 500");
    }

    [Fact]
    public async Task UnfilteredCountQuery_CountsRowsWithoutTheRelationshipGraph()
    {
        await using var harness = await Harness.CreateAsync();

        var (countQuery, _) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(harness.Db, 1, "2025", currentPage: 1, size: 100);
        var sql = countQuery.ToQueryString();

        sql.Should().NotContain("LIMIT", "the count is one number, not a page of rows");
        sql.Should().NotContain("OFFSET");
        sql.Should().NotContain("LEFT JOIN",
            "counting a league's events must not drag teams and files into the query");
        sql.Should().NotContain("INNER JOIN");
    }

    [Fact]
    public async Task UnfilteredWalk_ReturnsEveryEventOnce_InDateThenIdOrder()
    {
        await using var harness = await Harness.CreateAsync();
        const int total = 2500;
        const int size = 1000;
        var leagueId = await SeedKeepAllLeagueAsync(harness.Db, total);

        var (countQuery, _) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(harness.Db, leagueId, null, 1, size);
        var totalRecords = await countQuery.CountAsync();
        totalRecords.Should().Be(total);

        var seen = new List<int>();
        var totalPages = LeagueEndpoints.LeagueEventsTotalPages(totalRecords, size);
        for (var page = 1; page <= totalPages; page++)
        {
            var (_, pageQuery) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(harness.Db, leagueId, null, page, size);
            var records = await pageQuery.ToListAsync();

            records.Should().HaveCount(Math.Min(size, totalRecords - seen.Count),
                $"page {page} must return exactly its window out of {totalRecords}");

            // The response mapper needs teams and files for each page row.
            records.Should().OnlyContain(e => e.HomeTeam != null && e.AwayTeam != null);
            records.Where(e => e.HasFile).Should().OnlyContain(e => e.Files.Count == 1);

            seen.AddRange(records.Select(e => e.Id));
        }

        seen.Should().OnlyHaveUniqueItems("a page boundary must never duplicate a row");
        seen.Should().HaveCount(total, "the walk must cover the whole league with no gaps");

        var expected = await harness.Db.Events
            .AsNoTracking()
            .OrderByDescending(e => e.EventDate)
            .ThenByDescending(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync();
        seen.Should().Equal(expected,
            "the walk must follow EventDate desc then Id desc, matching the unpaged membership");
    }

    [Fact]
    public async Task UnfilteredPage_CountAndRowsUseTheSameSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-league-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var pause = new PausePageQueryInterceptor();
        try
        {
            var connectionString = $"Data Source={Path.Combine(directory, "events.db")};Pooling=False";
            var readOptions = new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(pause)
                .Options;
            await using var readDb = new SportarrDbContext(readOptions);
            await readDb.Database.EnsureCreatedAsync();
            await readDb.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
            var leagueId = await SeedKeepAllLeagueAsync(readDb, 1000);

            pause.PauseNextPage();
            var pageTask = LeagueEndpoints.GetUnfilteredEventPageAsync(readDb, leagueId, null, 1, 1000);
            await pause.PageRequested.WaitAsync(TimeSpan.FromSeconds(10));

            await using (var writer = new SportarrDbContext(new DbContextOptionsBuilder<SportarrDbContext>()
                .UseSqlite(connectionString)
                .Options))
            {
                writer.Events.Add(NewerEvent(leagueId));
                await writer.SaveChangesAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }

            pause.ReleasePage();
            var page = await pageTask;
            page.TotalRecords.Should().Be(1000);
            page.Records.Should().HaveCount(1000);
            page.Records.Should().Contain(e => e.Id == 1,
                "a concurrent insert must not displace an existing event from the only page");
            page.Records.Should().NotContain(e => e.Id == 1001);

        }
        finally
        {
            pause.ReleasePage();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Event NewerEvent(int leagueId) => new()
    {
        Id = 1001,
        Title = "New game",
        Sport = "Basketball",
        LeagueId = leagueId,
        Season = "2025-26",
        EventDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task EnvelopeMath_CountsAndClampsStayExact()
    {
        await using var harness = await Harness.CreateAsync();
        const int total = 37;
        var leagueId = await SeedKeepAllLeagueAsync(harness.Db, total);

        var (countQuery, _) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(harness.Db, leagueId, null, 1, 1);
        (await countQuery.CountAsync()).Should().Be(total);

        LeagueEndpoints.LeagueEventsTotalPages(total, 1).Should().Be(37);
        LeagueEndpoints.LeagueEventsTotalPages(total, 7).Should().Be(6);
        LeagueEndpoints.LeagueEventsTotalPages(total, 36).Should().Be(2);
        LeagueEndpoints.LeagueEventsTotalPages(total, 37).Should().Be(1);
        LeagueEndpoints.LeagueEventsTotalPages(total, 100).Should().Be(1);
        LeagueEndpoints.LeagueEventsTotalPages(0, 100).Should().Be(0);

        LeagueEndpoints.NormalizeLeagueEventsPaging(null, null).Should().Be((1, 100));
        LeagueEndpoints.NormalizeLeagueEventsPaging(0, 0).Should().Be((1, 1));
        LeagueEndpoints.NormalizeLeagueEventsPaging(-5, 9999).Should().Be((1, 1000));
        LeagueEndpoints.NormalizeLeagueEventsPaging(4, 250).Should().Be((4, 250));

        var (_, pageQuery) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(harness.Db, leagueId, null, 1, 500);
        (await pageQuery.ToListAsync()).Should().HaveCount(total,
            "a page larger than the league returns everything once");
    }

    [Fact]
    public async Task FilteredLeague_StillPagesTheInMemoryVisibleList()
    {
        await using var harness = await Harness.CreateAsync();
        var db = harness.Db;

        db.Leagues.Add(new League { Id = 1, Name = "NFL", Sport = "American Football" });
        db.Teams.Add(new Team { Id = 1, ExternalId = "tm-a", Name = "Team A", LeagueId = 1, Sport = "American Football" });
        db.Teams.Add(new Team { Id = 2, ExternalId = "tm-b", Name = "Team B", LeagueId = 1, Sport = "American Football" });
        db.Teams.Add(new Team { Id = 3, ExternalId = "tm-c", Name = "Team C", LeagueId = 1, Sport = "American Football" });
        db.Teams.Add(new Team { Id = 4, ExternalId = "tm-d", Name = "Team D", LeagueId = 1, Sport = "American Football" });
        db.LeagueTeams.Add(new LeagueTeam { LeagueId = 1, TeamId = 1, Monitored = true });
        db.LeagueTeams.Add(new LeagueTeam { LeagueId = 1, TeamId = 2, Monitored = true });
        db.LeagueTeams.Add(new LeagueTeam { LeagueId = 1, TeamId = 3, Monitored = false });

        var baseDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 20; i++)
        {
            var (home, away, hasFile) = i switch
            {
                <= 8 => ("tm-a", "tm-c", false),
                <= 12 => ("tm-c", "tm-b", false),
                <= 16 => ("tm-c", "tm-d", true),
                _ => ("tm-c", "tm-d", false),
            };
            db.Events.Add(new Event
            {
                Id = i,
                Title = $"Game {i}",
                Sport = "American Football",
                LeagueId = 1,
                Season = "2026",
                Round = "1",
                HomeTeamExternalId = home,
                AwayTeamExternalId = away,
                HomeTeamName = home,
                AwayTeamName = away,
                EventDate = baseDate.AddDays(i),
                HasFile = hasFile,
            });
        }
        await db.SaveChangesAsync();

        var league = await db.Leagues
            .AsNoTracking()
            .Include(l => l.MonitoredTeams)
            .ThenInclude(lt => lt.Team)
            .FirstAsync(l => l.Id == 1);

        var all = await LeagueEndpoints.LoadLeagueEventsForVisibilityAsync(db, 1, null);
        var visible = LeagueEndpoints.SelectVisibleEvents(all, league, showAll: false);
        visible.Should().HaveCount(16,
            "monitored-team games and file-bearing events are the visible set");

        const int size = 3;
        var collected = new List<int>();
        for (var page = 1; page <= LeagueEndpoints.LeagueEventsTotalPages(visible.Count, size); page++)
        {
            var slice = await LeagueEndpoints.GetFilteredEventPageAsync(db, league, null, showAll: false, page, size);
            slice.TotalRecords.Should().Be(visible.Count,
                "the envelope count is the visible total, not the league total");
            collected.AddRange(slice.Records.Select(e => e.Id));
        }

        collected.Should().Equal(visible.Select(e => e.Id).ToList(),
            "the filtered path must keep slicing the same visible list in the same order");
    }

    [Fact]
    public async Task FilteredHugePage_ReturnsNoRows()
    {
        await using var harness = await Harness.CreateAsync();
        var leagueId = await SeedKeepAllLeagueAsync(harness.Db, 20);
        var league = await harness.Db.Leagues.FirstAsync(l => l.Id == leagueId);
        league.KeepAllEvents = false;
        await harness.Db.SaveChangesAsync();

        var slice = await LeagueEndpoints.GetFilteredEventPageAsync(
            harness.Db, league, null, showAll: false, int.MaxValue, 1000);

        slice.TotalRecords.Should().Be(20);
        slice.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UnfilteredPage_MatchesTheVisibleListOnTiedDates(bool keepAll, bool showAll)
    {
        await using var harness = await Harness.CreateAsync();
        var leagueId = await SeedKeepAllLeagueAsync(harness.Db, 20);
        var league = await harness.Db.Leagues.FirstAsync(l => l.Id == leagueId);
        league.KeepAllEvents = keepAll;
        await harness.Db.SaveChangesAsync();

        var all = await LeagueEndpoints.LoadLeagueEventsForVisibilityAsync(harness.Db, leagueId, null);
        var visible = LeagueEndpoints.SelectVisibleEvents(all, league, showAll);
        var (countQuery, pageQuery) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(
            harness.Db, leagueId, null, currentPage: 1, size: 7);
        var page = await pageQuery.ToListAsync();

        (await countQuery.CountAsync()).Should().Be(visible.Count);
        page.Select(e => e.Id).Should().Equal(visible.Take(7).Select(e => e.Id));
    }

    [Fact]
    public async Task SeasonNarrowing_StillAppliesToTheFastPath()
    {
        await using var harness = await Harness.CreateAsync();
        const int total = 80;
        string? SeasonFor(int i) => (i % 4) switch
        {
            0 => "2024",
            1 => "2025",
            2 => null,
            _ => "",
        };

        var leagueId = await SeedKeepAllLeagueAsync(harness.Db, total, SeasonFor);

        // A named season includes only its own rows.
        await AssertSeasonMembershipAsync(harness.Db, leagueId, "2024", i => SeasonFor(i) == "2024", total);
        await AssertSeasonMembershipAsync(harness.Db, leagueId, "2025", i => SeasonFor(i) == "2025", total);
        // "Unknown" includes null and empty seasons.
        await AssertSeasonMembershipAsync(harness.Db, leagueId, "Unknown", i => SeasonFor(i) is null or "", total);
    }

    private static async Task AssertSeasonMembershipAsync(
        SportarrDbContext db, int leagueId, string season, Func<int, bool> expected, int total)
    {
        var expectedIds = Enumerable.Range(1, total).Where(expected).ToList();

        var (countQuery, _) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(db, leagueId, season, 1, 1000);
        (await countQuery.CountAsync()).Should().Be(expectedIds.Count,
            $"season {season} must narrow the count");

        var (_, pageQuery) = LeagueEndpoints.ComposeUnfilteredEventPageQueries(db, leagueId, season, 1, 1000);
        var records = await pageQuery.ToListAsync();
        records.Should().HaveCount(expectedIds.Count,
            $"season {season} must narrow the page");
        records.Select(e => e.Id).Should().BeEquivalentTo(expectedIds,
            $"season {season} must return exactly that season's rows");
    }

    private static async Task<List<string>> ExplainQueryPlanAsync(SqliteConnection connection, string sqlText)
    {
        // SQLite's plan only needs the statement before its parameter lines.
        var executable = sqlText.Split(".param set")[0].TrimEnd(';', ' ', '\r', '\n');
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + executable;
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(3));
        }
        return lines;
    }
}
