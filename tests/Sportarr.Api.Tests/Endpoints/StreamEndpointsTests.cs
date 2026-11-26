using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// A consumer that resumed with a cursor past the newest id, for example after
/// a database restore, heard nothing. The live loop skipped every id at or
/// below that cursor. The stream now asks it to resync from the newest id.
/// </summary>
public class StreamEndpointsTests : IAsyncLifetime
{
    // Shorter than the 15 second keepalive, so a wait for it fails the test.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    // A file database, so the endpoint and the test each open their own
    // connection. One shared in-memory connection is not safe across threads.
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"sportarr-stream-{Guid.NewGuid():N}.db");
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private EventStreamService _stream = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<SportarrDbContext>(o => o.UseSqlite($"DataSource={_databasePath};Pooling=False"));
        builder.Services.AddSingleton<EventStreamService>();
        _app = builder.Build();
        _app.MapStreamEndpoints();
        await _app.StartAsync();
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SportarrDbContext>().Database.EnsureCreatedAsync();
        }
        _client = _app.GetTestClient();
        _stream = _app.Services.GetRequiredService<EventStreamService>();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        File.Delete(_databasePath);
    }

    [Fact]
    public async Task Headers_arrive_before_the_first_keepalive()
    {
        // A cancelled request also completes the response, so the wait is timed
        // rather than cut off.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _client.GetAsync("/api/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        watch.Stop();

        watch.Elapsed.Should().BeLessThan(ReadTimeout);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task Cursor_past_the_newest_id_gets_a_resync_and_then_live_events()
    {
        await PublishAsync(3);
        using var cts = new CancellationTokenSource(ReadTimeout);
        using var response = await _client.GetAsync("/api/stream?since=50", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        var resync = await ReadFrameAsync(reader, cts.Token);
        resync.Name.Should().Be("stream.resync");
        resync.Id.Should().BeNull();
        resync.Data.Should().Be("{\"reason\":\"cursor ahead\",\"lastId\":3}");

        await PublishAsync(1);
        var live = await ReadFrameAsync(reader, cts.Token);
        live.Id.Should().Be("4");
        live.Name.Should().Be("event.updated");
    }

    [Fact]
    public async Task Cursor_with_no_stored_events_gets_a_resync_to_zero()
    {
        using var cts = new CancellationTokenSource(ReadTimeout);
        using var response = await _client.GetAsync("/api/stream?since=7", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        var resync = await ReadFrameAsync(reader, cts.Token);
        resync.Name.Should().Be("stream.resync");
        resync.Data.Should().Be("{\"reason\":\"cursor ahead\",\"lastId\":0}");
    }

    [Fact]
    public async Task Cursor_at_the_newest_id_goes_live_with_no_resync()
    {
        await PublishAsync(3);
        using var cts = new CancellationTokenSource(ReadTimeout);
        using var response = await _client.GetAsync("/api/stream?since=3", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        await PublishAsync(1);
        var live = await ReadFrameAsync(reader, cts.Token);
        live.Id.Should().Be("4");
        live.Name.Should().Be("event.updated");
    }

    [Fact]
    public async Task Cursor_behind_the_newest_id_replays_what_it_missed()
    {
        await PublishAsync(3);
        using var cts = new CancellationTokenSource(ReadTimeout);
        using var response = await _client.GetAsync("/api/stream?since=1", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        (await ReadFrameAsync(reader, cts.Token)).Id.Should().Be("2");
        (await ReadFrameAsync(reader, cts.Token)).Id.Should().Be("3");
    }

    [Fact]
    public async Task Prune_keeps_the_newest_row_when_every_row_is_old()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var old = DateTime.UtcNow.AddDays(-10);
        db.StreamEvents.AddRange(Enumerable.Range(1, 3).Select(_ => new StreamEvent
        {
            Timestamp = old, ResourceType = "event", Action = "updated", EventId = 1,
        }));
        await db.SaveChangesAsync();
        var housekeeping = new HousekeepingService(_app.Services, NullLogger<HousekeepingService>.Instance);

        await housekeeping.PruneStreamEventsAsync(db, CancellationToken.None);
        (await db.StreamEvents.AsNoTracking().Select(e => e.Id).ToListAsync()).Should().Equal(3);

        db.StreamEvents.Add(new StreamEvent
        {
            Timestamp = DateTime.UtcNow, ResourceType = "event", Action = "updated", EventId = 1,
        });
        await db.SaveChangesAsync();
        await housekeeping.PruneStreamEventsAsync(db, CancellationToken.None);
        (await db.StreamEvents.AsNoTracking().Select(e => e.Id).ToListAsync()).Should().Equal(4);
    }

    private async Task PublishAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _stream.PublishAsync("event", "updated", eventId: 1, leagueId: 1);
        }
    }

    private static async Task<(string? Id, string? Name, string Data)> ReadFrameAsync(StreamReader reader, CancellationToken ct)
    {
        string? id = null;
        string? name = null;
        var data = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(ct) ?? throw new EndOfStreamException();
            if (line.StartsWith(':'))
            {
                continue;
            }
            if (line.Length == 0)
            {
                if (data.Count > 0)
                {
                    return (id, name, string.Join("\n", data));
                }
                continue;
            }
            var colon = line.IndexOf(':');
            var field = line[..colon];
            var value = line[(colon + 1)..].TrimStart(' ');
            switch (field)
            {
                case "id": id = value; break;
                case "event": name = value; break;
                case "data": data.Add(value); break;
            }
        }
    }
}
