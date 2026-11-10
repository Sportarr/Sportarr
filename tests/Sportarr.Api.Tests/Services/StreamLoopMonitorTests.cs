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
/// The bookkeeping around the loop verdict. These tests cover which captures
/// are watched, which part of the file each look reads, and how long the
/// evidence stays. FrameRecurrenceTests covers the verdict itself. Here a
/// fake sampler gives the keyframes instead of FFmpeg.
/// </summary>
public class StreamLoopMonitorTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 7, 15, 55, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    // One tick of growth. It is larger than the tail margin, so each look
    // has something to read.
    private const long TickBytes = 1024 * 1024;

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
    public async Task The_first_look_reads_nothing_and_later_looks_read_what_was_appended()
    {
        // The first look only notes the position. Footage from before the
        // watch, such as a pre-event promo, is never judged.
        var capture = Capture("event.ts");
        Grow(capture, 2 * TickBytes);
        await _monitor.IsLoopingAsync(1, capture, Start, default);
        await _monitor.IsLoopingAsync(1, capture, Start + Tick, default);
        Grow(capture, TickBytes);
        await _monitor.IsLoopingAsync(1, capture, Start + 2 * Tick, default);
        Grow(capture, TickBytes);
        await _monitor.IsLoopingAsync(1, capture, Start + 3 * Tick, default);

        var first = EndOfRead(2 * TickBytes);
        var second = EndOfRead(3 * TickBytes);
        var third = EndOfRead(4 * TickBytes);
        _sampler.Reads.Select(r => (r.From, r.Length)).Should().Equal((first, second - first), (second, third - second));
    }

    [Fact]
    public async Task Each_read_stops_short_of_the_end_on_a_packet_boundary()
    {
        // The recorder can still be writing the last keyframe. This look
        // leaves it out. The next look starts at the same position.
        var capture = Capture("event.ts");
        await _monitor.IsLoopingAsync(1, capture, Start, default);
        Grow(capture, TickBytes + 1000);
        await _monitor.IsLoopingAsync(1, capture, Start + Tick, default);

        var read = _sampler.Reads.Single();
        read.From.Should().Be(0);
        (read.From + read.Length).Should().BeLessOrEqualTo(TickBytes + 1000 - StreamLoopMonitor.TailMargin);
        ((read.From + read.Length) % 188).Should().Be(0);
    }

    [Fact]
    public async Task A_capped_look_never_compares_keyframes_across_the_gap()
    {
        // Every append is above the cap, so each look loses its start. One
        // look of twelve keyframes holds only a little over two passes of the
        // clip. That is too little evidence on its own, so no verdict comes.
        var capture = Capture("event.ts");
        var verdicts = new List<bool>();
        for (var tick = 0; tick < 10; tick++)
            verdicts.Add(await LookAtLoopAsync(1, capture, tick, StreamLoopMonitor.MaxBytesPerLook + TickBytes));

        verdicts.Should().OnlyContain(v => !v);
        _sampler.Reads.Should().OnlyContain(r => r.Length <= StreamLoopMonitor.MaxBytesPerLook);
    }

    [Fact]
    public async Task A_capture_that_gets_shorter_is_watched_from_scratch()
    {
        var capture = Capture("event.ts");
        await WatchLoopAsync(capture, ticks: 7);
        using (var file = File.OpenWrite(capture))
            file.SetLength(TickBytes);

        var verdicts = await WatchLoopAsync(capture, ticks: 7, startTick: 7);

        verdicts.Take(6).Should().OnlyContain(v => !v);
        verdicts.Last().Should().BeTrue();
        _sampler.Reads.Skip(6).First().From.Should().Be(EndOfRead(2 * TickBytes));
    }

    [Fact]
    public async Task A_new_capture_file_starts_the_watch_over()
    {
        // A fallback channel records to a new file. The loop on the old file
        // is no evidence against it.
        await WatchLoopAsync(Capture("first.ts"), ticks: 6);
        var second = Capture("second.ts");

        var verdicts = await WatchLoopAsync(second, ticks: 7, startTick: 6);

        verdicts.Take(6).Should().OnlyContain(v => !v);
        verdicts.Last().Should().BeTrue();
        _sampler.Reads.Where(r => r.Path == second).First().From.Should().Be(EndOfRead(TickBytes));
    }

    [Fact]
    public async Task Keyframes_older_than_the_window_no_longer_count()
    {
        // Ten minutes of live footage come first. Then the source drops and
        // the provider starts a loop. The loop is caught within three
        // minutes, and the live footage before it does not dilute it.
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

    [Fact]
    public async Task A_capture_without_video_is_no_longer_sampled()
    {
        var capture = Capture("radio.ts");
        _sampler.NoVideo = true;

        var verdicts = await WatchLoopAsync(capture, ticks: 8);

        verdicts.Should().OnlyContain(v => !v);
        _sampler.Reads.Should().HaveCount(2);
    }

    // Twelve keyframes for each tick of a five-keyframe clip.
    private async Task<List<bool>> WatchLoopAsync(string capture, int ticks, int startTick = 0)
    {
        var verdicts = new List<bool>();
        for (var tick = startTick; tick < startTick + ticks; tick++)
            verdicts.Add(await LookAtLoopAsync(1, capture, tick));
        return verdicts;
    }

    private static readonly ulong[] Clip = Enumerable.Range(0, 5).Select(i => (ulong)new Random(100 + i).NextInt64()).ToArray();

    private Task<bool> LookAtLoopAsync(int recordingId, string capture, int tick, long bytes = TickBytes)
    {
        Grow(capture, bytes);
        _sampler.Next = Enumerable.Range(tick * 12, 12).Select(i => Clip[i % Clip.Length]).ToList();
        return _monitor.IsLoopingAsync(recordingId, capture, Start + tick * Tick, default);
    }

    private string Capture(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    // Sparse growth keeps the test files cheap.
    private static void Grow(string capture, long bytes = TickBytes)
    {
        using var file = new FileStream(capture, FileMode.Open);
        file.SetLength(file.Length + bytes);
    }

    private static long EndOfRead(long size)
    {
        var end = Math.Max(0, size - StreamLoopMonitor.TailMargin);
        return end - end % 188;
    }

    private sealed class FakeSampler : IKeyframeSampler
    {
        public IReadOnlyList<ulong> Next { get; set; } = Array.Empty<ulong>();
        public bool Fails { get; set; }
        public bool NoVideo { get; set; }
        public List<(string Path, long From, long Length)> Reads { get; } = new();

        public Task<IReadOnlyList<ulong>> SampleAsync(string path, long from, long length, CancellationToken ct)
        {
            Reads.Add((path, from, length));
            if (Fails)
                throw new InvalidOperationException("FFmpeg was not found");
            if (NoVideo)
                throw new NoVideoStreamException("The capture holds no video stream");
            var keyframes = Next;
            Next = Array.Empty<ulong>();
            return Task.FromResult(keyframes);
        }
    }
}
