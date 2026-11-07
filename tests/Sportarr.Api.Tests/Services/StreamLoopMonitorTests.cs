using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The bookkeeping around the loop verdict: which captures are watched, what
/// part of the file each look reads, and how long the evidence is kept. The
/// verdict itself is covered by FrameRecurrenceTests; here the keyframes come
/// from a fake sampler instead of FFmpeg.
/// </summary>
public class StreamLoopMonitorTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 7, 15, 55, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private readonly string _dir = Directory.CreateTempSubdirectory("loop-monitor-").FullName;
    private readonly FakeSampler _sampler = new();
    private readonly StreamLoopMonitor _monitor;

    public StreamLoopMonitorTests()
    {
        _monitor = new StreamLoopMonitor(_sampler, NullLogger<StreamLoopMonitor>.Instance);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task A_loop_is_reported_once_a_whole_window_has_been_watched()
    {
        var capture = Capture("event.ts");

        var verdicts = await WatchLoopAsync(capture, ticks: 7);

        verdicts.Should().Equal(false, false, false, false, false, false, true);
    }

    [Fact]
    public async Task Live_footage_is_never_reported()
    {
        var capture = Capture("event.ts");
        var random = new Random(1);

        for (var tick = 0; tick < 12; tick++)
        {
            Grow(capture);
            _sampler.Next = Enumerable.Range(0, 12).Select(_ => (ulong)random.NextInt64()).ToList();
            (await _monitor.IsLoopingAsync(1, capture, Start + tick * Tick, default)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Only_transport_stream_captures_are_watched()
    {
        var capture = Capture("event.mkv");

        var verdicts = await WatchLoopAsync(capture, ticks: 8);

        verdicts.Should().OnlyContain(v => !v);
        _sampler.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task Each_look_reads_only_what_was_appended_since_the_last()
    {
        var capture = Capture("event.ts");

        Grow(capture, 1000);
        await _monitor.IsLoopingAsync(1, capture, Start, default);
        await _monitor.IsLoopingAsync(1, capture, Start + Tick, default);
        Grow(capture, 500);
        await _monitor.IsLoopingAsync(1, capture, Start + 2 * Tick, default);

        _sampler.Reads.Select(r => (r.From, r.Length)).Should().Equal((0L, 1000L), (1000L, 500L));
    }

    [Fact]
    public async Task The_first_look_at_a_long_capture_reads_only_its_tail()
    {
        var capture = Capture("event.ts");
        using (var file = File.OpenWrite(capture))
            file.SetLength(StreamLoopMonitor.MaxBytesPerLook + 4096);

        await _monitor.IsLoopingAsync(1, capture, Start, default);

        _sampler.Reads.Select(r => (r.From, r.Length)).Should().Equal((4096L, StreamLoopMonitor.MaxBytesPerLook));
    }

    [Fact]
    public async Task A_new_capture_file_starts_the_watch_over()
    {
        // A fallback channel records to a new file; the loop on the old one is
        // no evidence against it.
        await WatchLoopAsync(Capture("first.ts"), ticks: 6);
        var second = Capture("second.ts");

        var verdicts = await WatchLoopAsync(second, ticks: 7, startTick: 6);

        verdicts.Take(6).Should().OnlyContain(v => !v);
        verdicts.Last().Should().BeTrue();
        _sampler.Reads.Where(r => r.Path == second).First().From.Should().Be(0);
    }

    [Fact]
    public async Task Keyframes_older_than_the_window_no_longer_count()
    {
        // Ten minutes of live footage, then the source drops and the provider
        // starts looping: the loop is caught within three minutes, not diluted
        // by the live footage before it.
        var capture = Capture("event.ts");
        var random = new Random(2);
        for (var tick = 0; tick < 20; tick++)
        {
            Grow(capture);
            _sampler.Next = Enumerable.Range(0, 12).Select(_ => (ulong)random.NextInt64()).ToList();
            await _monitor.IsLoopingAsync(1, capture, Start + tick * Tick, default);
        }

        var verdicts = await WatchLoopAsync(capture, ticks: 7, startTick: 20);

        verdicts.Last().Should().BeTrue();
    }

    [Fact]
    public async Task A_forgotten_recording_is_watched_from_scratch()
    {
        var kept = Capture("kept.ts");
        var dropped = Capture("dropped.ts");
        for (var tick = 0; tick < 6; tick++)
        {
            await LookAtLoopAsync(1, kept, tick);
            await LookAtLoopAsync(2, dropped, tick);
        }

        _monitor.ForgetAllExcept(new HashSet<int> { 1 });

        (await LookAtLoopAsync(1, kept, 6)).Should().BeTrue();
        (await LookAtLoopAsync(2, dropped, 6)).Should().BeFalse();
    }

    [Fact]
    public async Task A_sampler_failure_is_no_verdict()
    {
        var capture = Capture("event.ts");
        _sampler.Fails = true;

        var verdicts = await WatchLoopAsync(capture, ticks: 8);

        verdicts.Should().OnlyContain(v => !v);
    }

    // Twelve keyframes per tick of a five-keyframe clip.
    private async Task<List<bool>> WatchLoopAsync(string capture, int ticks, int startTick = 0)
    {
        var verdicts = new List<bool>();
        for (var tick = startTick; tick < startTick + ticks; tick++)
            verdicts.Add(await LookAtLoopAsync(1, capture, tick));
        return verdicts;
    }

    private static readonly ulong[] Clip = Enumerable.Range(0, 5).Select(i => (ulong)new Random(100 + i).NextInt64()).ToArray();

    private Task<bool> LookAtLoopAsync(int recordingId, string capture, int tick)
    {
        Grow(capture);
        _sampler.Next = Enumerable.Range(tick * 12, 12).Select(i => Clip[i % Clip.Length]).ToList();
        return _monitor.IsLoopingAsync(recordingId, capture, Start + tick * Tick, default);
    }

    private string Capture(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    private static void Grow(string capture, int bytes = 188)
    {
        using var file = new FileStream(capture, FileMode.Append);
        file.Write(new byte[bytes]);
    }

    private sealed class FakeSampler : IKeyframeSampler
    {
        public IReadOnlyList<ulong> Next { get; set; } = Array.Empty<ulong>();
        public bool Fails { get; set; }
        public List<(string Path, long From, long Length)> Reads { get; } = new();

        public Task<IReadOnlyList<ulong>> SampleAsync(string path, long from, long length, CancellationToken ct)
        {
            Reads.Add((path, from, length));
            if (Fails)
                throw new InvalidOperationException("FFmpeg was not found");
            var keyframes = Next;
            Next = Array.Empty<ulong>();
            return Task.FromResult(keyframes);
        }
    }
}
