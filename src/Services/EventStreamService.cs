using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Publishes resource-change events to SSE subscribers (/api/stream)
/// and persists each one so a reconnecting client can catch up with
/// ?since=&lt;id&gt; instead of a full resync. Built for the Bazarr
/// integration; the web frontend can adopt it to replace polling.
/// </summary>
public class EventStreamService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventStreamService> _logger;
    private readonly ConcurrentDictionary<Guid, Channel<StreamEvent>> _subscribers = new();

    public EventStreamService(IServiceScopeFactory scopeFactory, ILogger<EventStreamService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Persist one event and fan it out to live subscribers. Never
    /// throws: the stream is an observer, a failure here must not
    /// break the operation being observed.
    /// </summary>
    public async Task PublishAsync(string resourceType, string action, int? eventId = null,
        string? externalId = null, int? leagueId = null, string? path = null)
    {
        try
        {
            var evt = new StreamEvent
            {
                Timestamp = DateTime.UtcNow,
                ResourceType = resourceType,
                Action = action,
                EventId = eventId,
                ExternalId = externalId,
                LeagueId = leagueId,
                Path = path,
            };

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                db.StreamEvents.Add(evt);
                await db.SaveChangesAsync();
            }

            foreach (var channel in _subscribers.Values)
            {
                // Bounded channels drop-oldest; a slow consumer falls back
                // to cursor catch-up on its next reconnect.
                channel.Writer.TryWrite(evt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Stream] Failed to publish {Type}.{Action}", resourceType, action);
        }
    }

    /// <summary>
    /// Persist a batch in one transaction, then fan out. Used by sync,
    /// where one season can add hundreds of events.
    /// </summary>
    public async Task PublishBatchAsync(IReadOnlyList<StreamEvent> events)
    {
        if (events.Count == 0) return;
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                db.StreamEvents.AddRange(events);
                await db.SaveChangesAsync();
            }

            foreach (var channel in _subscribers.Values)
            {
                foreach (var evt in events)
                {
                    channel.Writer.TryWrite(evt);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Stream] Failed to publish batch of {Count}", events.Count);
        }
    }

    /// <summary>
    /// Register a live subscriber. Dispose the returned subscription to
    /// detach. Missed rows are read from the DB by the endpoint before
    /// it starts draining this channel.
    /// </summary>
    public Subscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<StreamEvent>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
        _subscribers[id] = channel;
        return new Subscription(this, id, channel.Reader);
    }

    private void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public async Task<List<StreamEvent>> ReplaySinceAsync(int sinceId, int limit, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        return await db.StreamEvents.AsNoTracking()
            .Where(e => e.Id > sinceId)
            .OrderBy(e => e.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public sealed class Subscription : IDisposable
    {
        private readonly EventStreamService _service;
        private readonly Guid _id;

        public ChannelReader<StreamEvent> Reader { get; }

        internal Subscription(EventStreamService service, Guid id, ChannelReader<StreamEvent> reader)
        {
            _service = service;
            _id = id;
            Reader = reader;
        }

        public void Dispose() => _service.Unsubscribe(_id);
    }
}
