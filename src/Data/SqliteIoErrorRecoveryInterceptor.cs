using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sportarr.Api.Data;

/// <summary>
/// Discards pooled SQLite connections after an I/O error surfaced through EF
/// command, connection, or transaction interception so later work can open a
/// fresh native handle. The failed operation is never retried here: for writes,
/// SQLite may already have committed part or all of the operation.
/// </summary>
public sealed class SqliteIoErrorRecoveryInterceptor :
    DbCommandInterceptor,
    IDbConnectionInterceptor,
    IDbTransactionInterceptor
{
    private static readonly TimeSpan PoolClearCooldown = TimeSpan.FromSeconds(30);

    private readonly ILogger<SqliteIoErrorRecoveryInterceptor> _logger;
    private readonly Action _clearPools;
    private readonly TimeProvider _timeProvider;
    private long _lastPoolClearTimestamp = long.MinValue;

    public SqliteIoErrorRecoveryInterceptor(ILogger<SqliteIoErrorRecoveryInterceptor> logger)
        : this(logger, SqliteConnection.ClearAllPools, TimeProvider.System)
    {
    }

    internal SqliteIoErrorRecoveryInterceptor(
        ILogger<SqliteIoErrorRecoveryInterceptor> logger,
        Action clearPools,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _clearPools = clearPools;
        _timeProvider = timeProvider;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        TryRecover(eventData.Exception);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TryRecover(eventData.Exception);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    public void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
        TryRecover(eventData.Exception);

    public Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TryRecover(eventData.Exception);
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        TryRecover(eventData.Exception);

    public Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TryRecover(eventData.Exception);
        return Task.CompletedTask;
    }

    internal bool TryRecover(Exception exception)
    {
        if (!TryFindIoError(exception, out var sqliteException))
            return false;

        var nowTimestamp = _timeProvider.GetTimestamp();
        while (true)
        {
            var previousTimestamp = Volatile.Read(ref _lastPoolClearTimestamp);
            if (previousTimestamp != long.MinValue &&
                _timeProvider.GetElapsedTime(previousTimestamp, nowTimestamp) < PoolClearCooldown)
                return false;

            if (Interlocked.CompareExchange(
                    ref _lastPoolClearTimestamp,
                    nowTimestamp,
                    previousTimestamp) == previousTimestamp)
                break;
        }

        try
        {
            _clearPools();
        }
        catch (Exception clearException)
        {
            // Pool cleanup is best-effort and must never replace the original
            // database exception that EF Core is already propagating.
            Interlocked.CompareExchange(ref _lastPoolClearTimestamp, long.MinValue, nowTimestamp);
            TryLog(() => _logger.LogError(
                    clearException,
                    "[Database] Failed to clear pooled SQLite connections after I/O error {SqliteErrorCode} (extended {SqliteExtendedErrorCode})",
                    sqliteException.SqliteErrorCode,
                    sqliteException.SqliteExtendedErrorCode));
            return true;
        }

        TryLog(() => _logger.LogWarning(
                sqliteException,
                "[Database] SQLite I/O error {SqliteErrorCode} (extended {SqliteExtendedErrorCode}); cleared pooled connections so newly opened connections use fresh handles",
                sqliteException.SqliteErrorCode,
                sqliteException.SqliteExtendedErrorCode));
        return true;
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Recovery is invoked while EF Core is already propagating the
            // command failure. A logging provider must not replace it.
        }
    }

    internal static bool TryFindIoError(Exception exception, out SqliteException sqliteException)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqliteException candidate && candidate.SqliteErrorCode == 10)
            {
                sqliteException = candidate;
                return true;
            }
        }

        sqliteException = null!;
        return false;
    }
}
