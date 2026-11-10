using FluentAssertions;
using Microsoft.Data.Sqlite;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Issue #288. An instance ran for 15 days on a damaged database. Two of its
/// 52 tables had an unreadable root page, the other 47 answered normally, so
/// the UI rendered and the probe endpoint reported healthy while recordings
/// completed on disk and never reached the library.
///
/// The tracker is what tells the health surfaces the difference between a
/// database that is damaged and a statement that was simply wrong. Only the
/// first kind may mark an instance unhealthy.
/// </summary>
public class DatabaseHealthTrackerTests
{
    // SQLITE_CORRUPT. The "database disk image is malformed" error the
    // reporter saw 61,143 times in seven days.
    private const int SqliteCorrupt = 11;

    // SQLITE_NOTADB. What a truncated or overwritten file reads as.
    private const int SqliteNotADb = 26;

    private static SqliteException Sqlite(int errorCode, string message = "database disk image is malformed")
        => new(message, errorCode);

    [Fact]
    public void AFreshTrackerReportsNothing()
    {
        var tracker = new DatabaseHealthTracker();

        tracker.IsDamaged.Should().BeFalse();
        tracker.Snapshot().Count.Should().Be(0);
    }

    [Fact]
    public void AMalformedImageMarksTheDatabaseDamaged()
    {
        var tracker = new DatabaseHealthTracker();

        tracker.RecordFailure(Sqlite(SqliteCorrupt));

        tracker.IsDamaged.Should().BeTrue("a damaged file never repairs itself, so one is enough to act on");
    }

    [Fact]
    public void AFileThatIsNotADatabaseCountsTheSameWay()
    {
        var tracker = new DatabaseHealthTracker();

        tracker.RecordFailure(Sqlite(SqliteNotADb, "file is not a database"));

        tracker.IsDamaged.Should().BeTrue();
    }

    [Fact]
    public void AnOrdinaryFailureNeverMarksTheDatabaseDamaged()
    {
        var tracker = new DatabaseHealthTracker();

        // SQLITE_CONSTRAINT. A duplicate key is a bug in a query, not a
        // damaged file, and must never take the instance unhealthy.
        tracker.RecordFailure(Sqlite(19, "UNIQUE constraint failed"));
        // SQLITE_BUSY. Contention, and it clears on its own.
        tracker.RecordFailure(Sqlite(5, "database is locked"));
        // SQLITE_IOERR. Serious, but a full disk or a lost mount is not the
        // same as a damaged file and it can come back.
        tracker.RecordFailure(Sqlite(10, "disk I/O error"));
        tracker.RecordFailure(new InvalidOperationException("no rows"));
        tracker.RecordFailure(null);

        tracker.IsDamaged.Should().BeFalse();
        tracker.Snapshot().Count.Should().Be(0);
    }

    [Fact]
    public void TheSignalIsFoundThroughTheInnerExceptions()
    {
        // EF wraps provider exceptions, so the code is rarely on the outside.
        var wrapped = new InvalidOperationException(
            "An exception occurred while reading",
            new AggregateException(Sqlite(SqliteCorrupt)));

        DatabaseHealthTracker.IsDamageSignal(wrapped).Should().BeTrue();
    }

    [Fact]
    public void ADeeplyWrappedSignalIsStillFound()
    {
        var deep = new InvalidOperationException("outer",
            new InvalidOperationException("middle",
                new InvalidOperationException("inner", Sqlite(SqliteNotADb))));

        DatabaseHealthTracker.IsDamageSignal(deep).Should().BeTrue();
    }

    [Fact]
    public void APostgresCorruptionCodeCountsToo()
    {
        // XX001 data_corrupted, XX002 index_corrupted.
        foreach (var state in new[] { "XX001", "XX002" })
        {
            var tracker = new DatabaseHealthTracker();
            tracker.RecordFailure(new Npgsql.PostgresException("corrupt", "ERROR", "ERROR", state));
            tracker.IsDamaged.Should().BeTrue(state);
        }
    }

    [Fact]
    public void AnOrdinaryPostgresErrorDoesNot()
    {
        var tracker = new DatabaseHealthTracker();

        // 23505 unique_violation.
        tracker.RecordFailure(new Npgsql.PostgresException("duplicate key", "ERROR", "ERROR", "23505"));

        tracker.IsDamaged.Should().BeFalse();
    }

    [Fact]
    public void TheInterceptorReportsAFailedCommand()
    {
        // The wiring matters as much as the classification. Every query in
        // the app reaches the tracker through this one callback.
        var tracker = new DatabaseHealthTracker();
        var interceptor = new Sportarr.Api.Data.CommandCountingInterceptor(tracker);

        interceptor.CommandFailed(
            new Microsoft.Data.Sqlite.SqliteCommand(),
            new Microsoft.EntityFrameworkCore.Diagnostics.CommandErrorEventData(
                eventDefinition: null!,
                messageGenerator: (_, __) => "",
                connection: null!,
                command: new Microsoft.Data.Sqlite.SqliteCommand(),
                logCommandText: "",
                context: null,
                executeMethod: Microsoft.EntityFrameworkCore.Diagnostics.DbCommandMethod.ExecuteReader,
                commandId: Guid.NewGuid(),
                connectionId: Guid.NewGuid(),
                exception: Sqlite(SqliteCorrupt),
                async: false,
                logParameterValues: false,
                startTime: DateTimeOffset.UtcNow,
                duration: TimeSpan.Zero,
                commandSource: Microsoft.EntityFrameworkCore.Diagnostics.CommandSource.LinqQuery));

        tracker.IsDamaged.Should().BeTrue();
    }

    [Fact]
    public void RepeatedFailuresAreCountedAndTheFirstIsRemembered()
    {
        var tracker = new DatabaseHealthTracker();

        tracker.RecordFailure(Sqlite(SqliteCorrupt, "first"));
        tracker.RecordFailure(Sqlite(SqliteCorrupt, "second"));
        tracker.RecordFailure(Sqlite(SqliteCorrupt, "third"));

        var (count, lastError, firstSeen, lastSeen) = tracker.Snapshot();
        count.Should().Be(3);
        lastError.Should().Contain("third");
        firstSeen.Should().NotBeNull();
        lastSeen.Should().NotBeNull();
        lastSeen!.Value.Should().BeOnOrAfter(firstSeen!.Value, "the first sighting is what dates the damage");
    }

    [Fact]
    public void ResetClearsIt()
    {
        var tracker = new DatabaseHealthTracker();
        tracker.RecordFailure(Sqlite(SqliteCorrupt));

        tracker.Reset();

        tracker.IsDamaged.Should().BeFalse("a restore is the only thing that repairs the file");
        tracker.Snapshot().Count.Should().Be(0);
    }

    [Fact]
    public void ItIsSafeToRecordFromManyThreadsAtOnce()
    {
        // Every query in the app reports through one instance of this.
        var tracker = new DatabaseHealthTracker();

        Parallel.For(0, 500, _ => tracker.RecordFailure(Sqlite(SqliteCorrupt)));

        tracker.Snapshot().Count.Should().Be(500);
    }
}
