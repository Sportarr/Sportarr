using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Data;

/// <summary>
/// Counts executed DB commands into the ambient <see cref="SyncMetrics"/>
/// counter so a measured league sync can report objective DB round-trip
/// totals. Registered on the DbContext options for both the scoped context
/// and the context factory.
///
/// Every override is a no-op outside a <see cref="SyncMetrics"/> measured
/// block (a single AsyncLocal read), so this imposes no meaningful overhead
/// on normal request-path queries.
/// </summary>
public sealed class CommandCountingInterceptor : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        SyncMetrics.IncrementDbCommands(command.CommandText);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        SyncMetrics.IncrementDbCommands(command.CommandText);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        SyncMetrics.IncrementDbCommands(command.CommandText);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        SyncMetrics.IncrementDbCommands(command.CommandText);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        SyncMetrics.IncrementDbCommands(command.CommandText);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        SyncMetrics.IncrementDbCommands(command.CommandText);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }
}
