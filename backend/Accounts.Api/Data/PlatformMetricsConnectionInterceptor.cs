using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Accounts.Api.Data;

public sealed class PlatformMetricsConnectionInterceptor(PlatformMetrics metrics) : DbConnectionInterceptor
{
    private readonly ConcurrentDictionary<DbConnection, long> opening = new();

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        opening[connection] = Stopwatch.GetTimestamp();
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        opening[connection] = Stopwatch.GetTimestamp();
        return ValueTask.FromResult(result);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        var elapsed = opening.TryRemove(connection, out var started)
            ? Stopwatch.GetElapsedTime(started)
            : TimeSpan.Zero;
        metrics.DatabaseConnectionOpened(elapsed);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ConnectionOpened(connection, eventData);
        return Task.CompletedTask;
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        opening.TryRemove(connection, out _);
        metrics.DatabaseConnectionClosed();
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        opening.TryRemove(connection, out _);
        metrics.DatabaseConnectionClosed();
        return ValueTask.FromResult(result);
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
        opening.TryRemove(connection, out _);

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        opening.TryRemove(connection, out _);
        return Task.CompletedTask;
    }
}
