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
                var missed = await stream.ReplaySinceAsync(since, 1000, ct);
                foreach (var evt in missed)
                {
                    await WriteEventAsync(ctx, evt);
                    lastSent = evt.Id;
                }
                await ctx.Response.Body.FlushAsync(ct);
            }

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var readTask = subscription.Reader.WaitToReadAsync(ct).AsTask();
                    var completed = await Task.WhenAny(readTask, Task.Delay(Heartbeat, ct));

                    if (completed != readTask)
                    {
                        // SSE comment line keeps proxies and clients from
                        // timing out an idle stream.
                        await ctx.Response.WriteAsync(": keepalive\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                        continue;
                    }

                    if (!await readTask) break;

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
