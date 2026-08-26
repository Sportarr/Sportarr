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
    private const int SubscriberQueueCapacity = 256;

    /// <summary>
    /// One live subscriber's queue, and whether anything has been dropped from
    /// it.
    ///
    /// The queue drops its oldest entry when it fills, and the comment used to
    /// say a slow consumer would catch up from the cursor "on its next
    /// reconnect". A consumer that stays connected never reconnects, so it
    /// simply never learned about those changes and went on showing stale
    /// state indefinitely. The flag lets the endpoint notice and replay.
    /// </summary>
    private sealed class SubscriberQueue
    {
        public required Channel<StreamEvent> Channel { get; init; }
        private int _dropped;

        public void NoteDropped() => Interlocked.Exchange(ref _dropped, 1);

        /// <summary>True once, then clears, so the caller replays exactly once.</summary>
        public bool ConsumeDropped() => Interlocked.Exchange(ref _dropped, 0) == 1;

        private readonly object _writeGate = new();

        public void Write(StreamEvent evt)
        {
            // Reader.Count is the depth right now. At capacity the next write
            // discards the oldest entry, and that entry is a change some
            // consumer has not seen.
            //
            // Reading the depth and writing have to happen together. Two
            // publishers arriving at once both saw room, both wrote, and the
            // second one pushed an entry out without either noticing, so the
            // consumer was never told to replay and lost that change for good.
            lock (_writeGate)
            {
                if (Channel.Reader.Count >= SubscriberQueueCapacity)
                {
                    NoteDropped();
                }
                Channel.Writer.TryWrite(evt);
            }
        }
    }

    private readonly ConcurrentDictionary<Guid, SubscriberQueue> _subscribers = new();

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

            foreach (var subscriber in _subscribers.Values)
            {
                // Bounded queue, oldest dropped when full. A drop is recorded
                // so the consumer can be caught up from the cursor without
                // having to reconnect first.
                subscriber.Write(evt);
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

            foreach (var subscriber in _subscribers.Values)
            {
                foreach (var evt in events)
                {
                    subscriber.Write(evt);
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
            new BoundedChannelOptions(SubscriberQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
        var queue = new SubscriberQueue { Channel = channel };
        _subscribers[id] = queue;
        return new Subscription(this, id, channel.Reader, queue.ConsumeDropped);
    }

    private void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var subscriber))
        {
            subscriber.Channel.Writer.TryComplete();
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

        private readonly Func<bool> _consumeDropped;

        public ChannelReader<StreamEvent> Reader { get; }

        /// <summary>
        /// True once when this subscriber's queue overflowed and events were
        /// discarded, so the caller knows to catch up from the cursor rather
        /// than carry on with a gap it cannot see.
        /// </summary>
        public bool MissedEvents() => _consumeDropped();

        internal Subscription(EventStreamService service, Guid id, ChannelReader<StreamEvent> reader, Func<bool> consumeDropped)
        {
            _service = service;
            _id = id;
            Reader = reader;
            _consumeDropped = consumeDropped;
        }

        public void Dispose() => _service.Unsubscribe(_id);
    }
}
