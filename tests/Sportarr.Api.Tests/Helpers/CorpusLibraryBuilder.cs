using System.Globalization;
using System.Text.Json;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// One real file name from the library import corpus and the event it
/// belongs to. A null expected event means no event in the corpus seasons
/// is that file, so any confident match is wrong.
/// </summary>
public record CorpusCase(
    string RelativePath,
    string LeagueExternalId,
    string Season,
    string? ExpectedEventExternalId,
    string? Source = null);

/// <summary>
/// Loads the library import corpus fixtures. The season snapshots become
/// the leagues and events of a library, so every other event of a season
/// is a decoy for each corpus file.
/// </summary>
public static class CorpusLibraryBuilder
{
    public static IReadOnlyList<CorpusCase> ReadCases(string fixturesDir)
    {
        var cases = new List<CorpusCase>();
        foreach (var line in File.ReadLines(Path.Combine(fixturesDir, "corpus.jsonl")))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var row = JsonDocument.Parse(line).RootElement;
            cases.Add(new CorpusCase(
                row.GetProperty("relativePath").GetString()!,
                row.GetProperty("leagueExternalId").GetString()!,
                row.GetProperty("season").GetString()!,
                row.GetProperty("expected").GetString(),
                row.TryGetProperty("source", out var source) ? source.GetString() : null));
        }
        return cases;
    }

    public static void Seed(SportarrDbContext db, string fixturesDir)
    {
        var leagues = new Dictionary<string, League>();
        foreach (var path in Directory.GetFiles(Path.Combine(fixturesDir, "seasons"), "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var snapshot = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            var leagueRow = snapshot.GetProperty("league");
            var leagueId = leagueRow.GetProperty("idLeague").GetString()!;
            if (!leagues.TryGetValue(leagueId, out var league))
            {
                league = new League
                {
                    Name = leagueRow.GetProperty("strLeague").GetString()!,
                    Sport = leagueRow.GetProperty("strSport").GetString()!,
                    ExternalId = leagueId,
                    AlternateName = Text(leagueRow, "strLeagueAlternate"),
                };
                db.Leagues.Add(league);
                db.SaveChanges();
                leagues[leagueId] = league;
            }

            var events = snapshot.GetProperty("events").EnumerateArray()
                .Select(row => new
                {
                    Id = row.GetProperty("idEvent").GetString()!,
                    Title = row.GetProperty("strEvent").GetString()!,
                    Season = row.GetProperty("strSeason").GetString()!,
                    Round = Text(row, "intRound"),
                    Date = EventDate(row),
                })
                .OrderBy(e => e.Date)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .ToList();

            var episode = 0;
            foreach (var e in events)
            {
                db.Events.Add(new Event
                {
                    Title = e.Title,
                    Season = e.Season,
                    SeasonNumber = int.Parse(e.Season[..4], CultureInfo.InvariantCulture),
                    EpisodeNumber = ++episode,
                    Round = e.Round,
                    EventDate = e.Date,
                    ExternalId = e.Id,
                    Sport = league.Sport,
                    LeagueId = league.Id,
                    HasFile = false,
                    Monitored = true,
                });
            }
            db.SaveChanges();
        }
    }

    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTime EventDate(JsonElement row)
    {
        var date = DateTime.ParseExact(row.GetProperty("dateEvent").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = Text(row, "strTime");
        if (time is { Length: >= 8 } && TimeSpan.TryParse(time[..8], CultureInfo.InvariantCulture, out var clock))
            date = date.Add(clock);
        return DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }
}
