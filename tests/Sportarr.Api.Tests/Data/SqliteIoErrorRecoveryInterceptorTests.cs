using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Data;

public class SqliteIoErrorRecoveryInterceptorTests
{
    private static SqliteIoErrorRecoveryInterceptor CreateInterceptor(
        Action clearPools,
        ILogger<SqliteIoErrorRecoveryInterceptor>? logger = null) =>
        new(
            logger ?? NullLogger<SqliteIoErrorRecoveryInterceptor>.Instance,
            clearPools,
            TimeProvider.System);

    private sealed class ThrowingLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("logger failed");
    }

    private sealed class ThrowOnceCommandInterceptor(SqliteException exception) : DbCommandInterceptor
    {
        private int _remaining = 1;

        public override InterceptionResult<int> NonQueryExecuting(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            if (Interlocked.Exchange(ref _remaining, 0) == 1)
                throw exception;

            return result;
        }
    }

    private sealed class ThrowOnceConnectionInterceptor(SqliteException exception) : DbConnectionInterceptor
    {
        private int _remaining = 1;

        public override InterceptionResult ConnectionOpening(
            System.Data.Common.DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result)
        {
            if (Interlocked.Exchange(ref _remaining, 0) == 1)
                throw exception;

            return result;
        }
    }

    private sealed class ThrowOnceTransactionInterceptor(SqliteException exception) : DbTransactionInterceptor
    {
        private int _remaining = 1;

        public override InterceptionResult TransactionCommitting(
            System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result)
        {
            if (Interlocked.Exchange(ref _remaining, 0) == 1)
                throw exception;

            return result;
        }
    }

    [Fact]
    public void TryFindIoError_FindsNestedSqliteIoErrorAndPreservesExtendedCode()
    {
        var sqliteException = new SqliteException("short read", 10, 522);
        var exception = new DbUpdateException("save failed", sqliteException);

        var found = SqliteIoErrorRecoveryInterceptor.TryFindIoError(exception, out var result);

        found.Should().BeTrue();
        result.Should().BeSameAs(sqliteException);
        result.SqliteExtendedErrorCode.Should().Be(522);
    }

    [Fact]
    public void TryRecover_NonIoErrorDoesNotClearPools()
    {
        var clearCount = 0;
        var interceptor = CreateInterceptor(() => Interlocked.Increment(ref clearCount));

        var recovered = interceptor.TryRecover(new SqliteException("busy", 5, 5));

        recovered.Should().BeFalse();
        clearCount.Should().Be(0);
    }

    [Fact]
    public void TryRecover_ConcurrentIoErrorsClearPoolsOnceWithinCooldown()
    {
        var clearCount = 0;
        var interceptor = CreateInterceptor(() => Interlocked.Increment(ref clearCount));
        var exception = new SqliteException("I/O error", 10, 4618);

        Parallel.For(0, 64, _ => interceptor.TryRecover(exception));

        clearCount.Should().Be(1);
    }

    [Fact]
    public void TryRecover_ClearAndLoggingFailuresDoNotEscape()
    {
        var interceptor = CreateInterceptor(
            () => throw new InvalidOperationException("clear failed"),
            new ThrowingLogger<SqliteIoErrorRecoveryInterceptor>());

        var act = () => interceptor.TryRecover(new SqliteException("I/O error", 10, 10));

        act.Should().NotThrow().Which.Should().BeTrue();
    }

    [Fact]
    public void TryRecover_ClearFailureDoesNotConsumeCooldown()
    {
        var clearAttempts = 0;
        var interceptor = CreateInterceptor(() =>
        {
            if (Interlocked.Increment(ref clearAttempts) == 1)
                throw new InvalidOperationException("clear failed");
        });
        var exception = new SqliteException("I/O error", 10, 10);

        interceptor.TryRecover(exception).Should().BeTrue();
        interceptor.TryRecover(exception).Should().BeTrue();

        clearAttempts.Should().Be(2);
    }

    [Fact]
    public void TryRecover_SuccessfulClearWithLoggingFailureDoesNotEscape()
    {
        var interceptor = CreateInterceptor(
            () => { },
            new ThrowingLogger<SqliteIoErrorRecoveryInterceptor>());

        var act = () => interceptor.TryRecover(new SqliteException("I/O error", 10, 10));

        act.Should().NotThrow().Which.Should().BeTrue();
    }

    [Fact]
    public void AddSportarrDatabase_AttachesOneSharedRecoveryInterceptorToBothContextPaths()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSportarrDatabase(
            new ConfigurationBuilder().Build(),
            Path.Combine(Path.GetTempPath(), $"sportarr-recovery-{Guid.NewGuid():N}.db"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var scopedContext = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        using var factoryContext = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<SportarrDbContext>>()
            .CreateDbContext();

        var registered = scope.ServiceProvider.GetRequiredService<SqliteIoErrorRecoveryInterceptor>();
        GetRecoveryInterceptors(scopedContext).Should().ContainSingle().Which.Should().BeSameAs(registered);
        GetRecoveryInterceptors(factoryContext).Should().ContainSingle().Which.Should().BeSameAs(registered);
    }

    [Fact]
    public void EfCommandFailureCallback_InvokesRecoveryAndPreservesOriginalException()
    {
        var exception = new SqliteException("I/O error", 10, 522);
        var clearCount = 0;
        var recovery = CreateInterceptor(() => Interlocked.Increment(ref clearCount));
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(new ThrowOnceCommandInterceptor(exception), recovery)
            .Options;

        using var context = new SportarrDbContext(options);
        var act = () => context.Database.ExecuteSqlRaw("SELECT 1");

        act.Should().Throw<SqliteException>().Which.Should().BeSameAs(exception);
        clearCount.Should().Be(1);
    }

    [Fact]
    public void EfConnectionFailureCallback_InvokesRecoveryAndPreservesOriginalException()
    {
        var exception = new SqliteException("I/O error", 10, 4618);
        var clearCount = 0;
        var recovery = CreateInterceptor(() => Interlocked.Increment(ref clearCount));
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(new ThrowOnceConnectionInterceptor(exception), recovery)
            .Options;

        using var context = new SportarrDbContext(options);
        var act = () => context.Database.OpenConnection();

        act.Should().Throw<SqliteException>().Which.Should().BeSameAs(exception);
        clearCount.Should().Be(1);
        context.Database.OpenConnection();
    }

    [Fact]
    public void EfTransactionFailureCallback_InvokesRecoveryAndPreservesOriginalException()
    {
        var exception = new SqliteException("I/O error", 10, 778);
        var clearCount = 0;
        var recovery = CreateInterceptor(() => Interlocked.Increment(ref clearCount));
        var options = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(new ThrowOnceTransactionInterceptor(exception), recovery)
            .Options;

        using var context = new SportarrDbContext(options);
        context.Database.OpenConnection();
        using var transaction = context.Database.BeginTransaction();
        var act = () => transaction.Commit();

        act.Should().Throw<SqliteException>().Which.Should().BeSameAs(exception);
        clearCount.Should().Be(1);
    }

    [Fact]
    public void ClearAllPools_RetiresActiveHandleAfterItCloses()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sportarr-pool-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Pooling=True";

        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            var originalHandle = connection.Handle;

            SqliteConnection.ClearAllPools();

            connection.Handle.Should().BeSameAs(originalHandle);
            connection.Close();
            connection.Open();
            connection.Handle.Should().NotBeSameAs(originalHandle);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }

    private static List<SqliteIoErrorRecoveryInterceptor> GetRecoveryInterceptors(SportarrDbContext context) =>
        context.GetService<IDbContextOptions>()
            .Extensions
            .OfType<CoreOptionsExtension>()
            .SelectMany(extension => extension.Interceptors ?? Array.Empty<IInterceptor>())
            .OfType<SqliteIoErrorRecoveryInterceptor>()
            .ToList();
}
