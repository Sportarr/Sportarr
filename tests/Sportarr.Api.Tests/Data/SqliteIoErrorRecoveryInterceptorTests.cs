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

    private static List<SqliteIoErrorRecoveryInterceptor> GetRecoveryInterceptors(SportarrDbContext context) =>
        context.GetService<IDbContextOptions>()
            .Extensions
            .OfType<CoreOptionsExtension>()
            .SelectMany(extension => extension.Interceptors ?? Array.Empty<IInterceptor>())
            .OfType<SqliteIoErrorRecoveryInterceptor>()
            .ToList();
}
