using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class StreamEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    /// <summary>How many rows one replay query fetches.</summary>
    private const int ReplayPageSize = 1000;

    /// <summary>
    /// How many replay pages a reconnecting consumer may be handed before it
    /// is told to resync instead. Without a ceiling a very stale cursor would
    /// make one connection walk the whole history.
    /// </summary>
    private const int MaxReplayPages = 20;

    public static IEndpointRouteBuilder MapStreamEndpoints(this IEndpointRouteBuilder app)
    {
        // SSE feed of resource changes. EventSource cannot set headers,
        // so consumers authenticate with the existing ?apikey= query
        // support. Reconnect with ?since=<last id> to replay missed
        // rows before going live; the Last-Event-ID header works too.
        app.MapGet("/api/stream", async (HttpContext ctx, EventStreamService stream,
            ILogger<EventStreamService> logger) =>
        {
            var since = 0;
            if (int.TryParse(ctx.Request.Query["since"], out var q))
            {
                since = q;
            }
            else if (int.TryParse(ctx.Request.Headers["Last-Event-ID"], out var h))
            {
                since = h;
            }

            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";
            // Nginx and similar proxies buffer by default; this header
            // asks them not to, or events arrive in bursts.
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            var ct = ctx.RequestAborted;
            using var subscription = stream.Subscribe();

            var lastSent = since;
            if (since > 0)
            {
                // Replay in pages until caught up. A single capped query left
                // a consumer more than one page behind permanently short of
                // everything past the cap, and it went live from there
                // believing it was current, which is the opposite of what the
                // documented replay promises.
                var pages = 0;
                var caughtUp = false;
                while (pages < MaxReplayPages)
                {
                    var missed = await stream.ReplaySinceAsync(lastSent, ReplayPageSize, ct);
                    if (missed.Count == 0)
                    {
                        caughtUp = true;
                        break;
                    }

                    foreach (var evt in missed)
                    {
                        await WriteEventAsync(ctx, evt);
                        lastSent = evt.Id;
                    }

                    await ctx.Response.Body.FlushAsync(ct);
                    pages++;

                    if (missed.Count < ReplayPageSize)
                    {
                        caughtUp = true;
                        break;
                    }
                }

                if (!caughtUp)
                {
                    // Too far behind to replay sensibly. Say so rather than
                    // letting the consumer carry on with a hole in its state.
                    logger.LogInformation(
                        "[Stream] Consumer resumed from {Since} and is further behind than {Max} pages. Asking it to resync.",
                        since, MaxReplayPages);
                    await ctx.Response.WriteAsync(
                        $"event: stream.resync\ndata: {{\"reason\":\"too far behind\",\"lastId\":{lastSent}}}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }

            // One outstanding read, reused across heartbeats. Starting a fresh
            // wait on every heartbeat left the previous one registered on the
            // channel forever, so an idle connection piled up a pending wait
            // every fifteen seconds for as long as it stayed open.
            Task<bool>? readTask = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    readTask ??= subscription.Reader.WaitToReadAsync(ct).AsTask();

                    using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var delayTask = Task.Delay(Heartbeat, heartbeatCts.Token);
                    var completed = await Task.WhenAny(readTask, delayTask);

                    if (completed != readTask)
                    {
                        // SSE comment line keeps proxies and clients from
                        // timing out an idle stream. The read stays pending
                        // for the next pass.
                        await ctx.Response.WriteAsync(": keepalive\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                        continue;
                    }

                    // Stop the timer rather than leaving it to fire later.
                    heartbeatCts.Cancel();

                    var hasData = await readTask;
                    readTask = null;
                    if (!hasData) break;

                    // A consumer that reads too slowly has its oldest queued
                    // events discarded. It used to be told nothing, so a
                    // connection that stayed open simply never learned about
                    // those changes. Catch it up from the cursor instead.
                    if (subscription.MissedEvents())
                    {
                        var pages = 0;
                        var caughtUp = false;
                        while (pages < MaxReplayPages)
                        {
                            var missed = await stream.ReplaySinceAsync(lastSent, ReplayPageSize, ct);
                            if (missed.Count == 0)
                            {
                                caughtUp = true;
                                break;
                            }

                            foreach (var evt in missed)
                            {
                                if (evt.Id <= lastSent) continue;
                                await WriteEventAsync(ctx, evt);
                                lastSent = evt.Id;
                            }

                            pages++;
                            if (missed.Count < ReplayPageSize)
                            {
                                caughtUp = true;
                                break;
                            }
                        }

                        if (!caughtUp)
                        {
                            // Still behind after the last page. The drain below
                            // carries the newest events and would push the
                            // cursor over everything not replayed, losing it for
                            // good. Ask for a resync instead, the same answer
                            // the initial replay gives.
                            logger.LogInformation(
                                "[Stream] Consumer fell further behind than {Max} pages while connected. Asking it to resync.",
                                MaxReplayPages);
                            await ctx.Response.WriteAsync(
                                $"event: stream.resync\ndata: {{\"reason\":\"too far behind\",\"lastId\":{lastSent}}}\n\n", ct);
                        }

                        await ctx.Response.Body.FlushAsync(ct);
                    }

                    while (subscription.Reader.TryRead(out var evt))
                    {
                        // The live channel can race the replay query;
                        // the cursor keeps delivery exactly-once.
                        if (evt.Id <= lastSent) continue;
                        await WriteEventAsync(ctx, evt);
                        lastSent = evt.Id;
                    }
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client went away; normal SSE lifecycle.
            }
        });

        return app;
    }

    private static async Task WriteEventAsync(HttpContext ctx, StreamEvent evt)
    {
        var data = JsonSerializer.Serialize(new
        {
            id = evt.Id,
            timestamp = evt.Timestamp,
            resourceType = evt.ResourceType,
            action = evt.Action,
            eventId = evt.EventId,
            externalId = evt.ExternalId,
            leagueId = evt.LeagueId,
            path = evt.Path,
        }, JsonOptions);

        await ctx.Response.WriteAsync(
            $"id: {evt.Id}\nevent: {evt.ResourceType}.{evt.Action}\ndata: {data}\n\n",
            ctx.RequestAborted);
    }
}
