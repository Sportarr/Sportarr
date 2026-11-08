using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// The loop verdict behind the DVR watchdog's looping-stream check. A provider
/// that loses a channel can keep it up with a loop of its last seconds. The
/// file keeps growing, so only the pictures show the loop. The verdict must
/// catch that. It must never fail a live recording over a replay, a studio
/// or a still picture.
/// </summary>
public class FrameRecurrenceTests
{
    // Keyframes of a 13-second clip at one keyframe every ~2.5 seconds.
    private const int ClipKeyframes = 5;

    [Fact]
    public void A_short_clip_played_over_and_over_is_a_loop()
    {
        var random = new Random(1);
        var clip = Pictures(random, ClipKeyframes);
        var keyframes = Enumerable.Range(0, 72).Select(i => Reencoded(clip[i % ClipKeyframes], random)).ToList();

        FrameRecurrence.LooksLooped(keyframes).Should().BeTrue();
    }

    [Fact]
    public void A_clip_that_does_not_fill_whole_keyframe_intervals_is_still_a_loop()
    {
        // This is a 13-second clip with a keyframe every 2 seconds. The passes
        // alternate between six and seven keyframes, so the period changes
        // by one.
        var random = new Random(6);
        var clip = Pictures(random, 7);
        var keyframes = new List<ulong>();
        for (var pass = 0; keyframes.Count < 72; pass++)
            keyframes.AddRange(clip.Take(pass % 2 == 0 ? 6 : 7).Select(p => Reencoded(p, random)));

        FrameRecurrence.LooksLooped(keyframes).Should().BeTrue();
    }

    [Fact]
    public void Two_cameras_taking_turns_in_a_studio_is_not_a_loop()
    {
        // Every cut goes back to a picture seen before, but after however long
        // the other presenter talked, not after a fixed clip length. Two of
        // these interviews scored 0.78 and 0.62 before the period was required.
        var random = new Random(7);
        var cameras = Pictures(random, 2);
        var keyframes = new List<ulong>();
        for (var shot = 0; keyframes.Count < 72; shot++)
            keyframes.AddRange(Enumerable.Range(0, 1 + random.Next(6)).Select(_ => Reencoded(cameras[shot % 2], random)));

        FrameRecurrence.LooksLooped(keyframes).Should().BeFalse();
    }

    // Two cameras in a studio, with shots of a random number of keyframes in
    // each range. Every cut goes back to a picture seen before.
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(1, 3)]
    [InlineData(1, 6)]
    public void Two_cameras_with_short_shots_are_never_a_loop(int minShot, int maxShot)
    {
        foreach (var length in new[] { 36, 90, 180 })
        {
            for (var seed = 0; seed < 200; seed++)
            {
                var keyframes = Studio(new Random(seed), cameras: 2, minShot, maxShot, length);

                FrameRecurrence.LooksLooped(keyframes).Should().BeFalse($"seed {seed}, {length} keyframes");
            }
        }
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    public void Three_cameras_with_short_shots_are_not_a_loop(int minShot, int maxShot)
    {
        // A third camera passes the distinct-pictures rule. The irregular
        // cuts must then fail the period rule.
        foreach (var length in new[] { 36, 90, 180 })
        {
            for (var seed = 0; seed < 200; seed++)
            {
                var keyframes = Studio(new Random(seed), cameras: 3, minShot, maxShot, length);

                FrameRecurrence.LooksLooped(keyframes).Should().BeFalse($"seed {seed}, {length} keyframes");
            }
        }
    }

    [Fact]
    public void A_carousel_of_two_slides_is_never_a_loop()
    {
        // The slides alternate at a perfect period, but a loop must show at
        // least three different pictures.
        for (var hold = 1; hold <= 10; hold++)
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var random = new Random(seed);
                var slides = Pictures(random, 2);
                var keyframes = Enumerable.Range(0, 180).Select(i => Reencoded(slides[i / hold % 2], random)).ToList();

                FrameRecurrence.LooksLooped(keyframes).Should().BeFalse($"hold {hold}, seed {seed}");
            }
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void A_carousel_that_holds_each_slide_for_two_or_more_keyframes_is_never_a_loop(int slideCount)
    {
        // Each slide stays for two to four keyframes. The slides come back at
        // a steady period, and there are three or more of them. But half of
        // the keyframes or more are frozen, which a loop of footage never
        // shows.
        foreach (var fixedHold in new[] { 2, 3, 4, 0 })
        {
            for (var seed = 0; seed < 200; seed++)
            {
                var random = new Random(seed);
                var slides = Pictures(random, slideCount);
                var keyframes = new List<ulong>();
                for (var slide = 0; keyframes.Count < 181; slide++)
                {
                    // A hold of 0 here means a random hold for each slide.
                    var hold = fixedHold > 0 ? fixedHold : random.Next(2, 5);
                    for (var i = 0; i < hold && keyframes.Count < 181; i++)
                        keyframes.Add(Reencoded(slides[slide % slideCount], random));
                }

                // The window can start on the first keyframe of a slide or in
                // the middle of one.
                FrameRecurrence.LooksLooped(keyframes.Take(180).ToList()).Should().BeFalse($"hold {fixedHold}, seed {seed}");
                FrameRecurrence.LooksLooped(keyframes.Skip(1).ToList()).Should().BeFalse($"hold {fixedHold}, seed {seed}, offset");
            }
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void A_carousel_that_changes_the_slide_at_every_keyframe_counts_as_a_loop(int slideCount)
    {
        // This is accepted behavior. Content that repeats on a fixed cycle
        // looks the same as a looped clip. After the scheduled start, such a
        // channel shows no event, and the watchdog still needs a usable
        // fallback before it acts.
        var random = new Random(9);
        var slides = Pictures(random, slideCount);
        var keyframes = Enumerable.Range(0, 180).Select(i => Reencoded(slides[i % slideCount], random)).ToList();

        FrameRecurrence.LooksLooped(keyframes).Should().BeTrue();
    }

    // Twenty passes of a 10-keyframe loop. Four keyframes of each pass are
    // frozen copies of the keyframe before. A frozen tail follows. The window
    // has 199 + tail comparisons and 80 + tail frozen keyframes. The rule
    // rejects the window when frozen + 1 >= comparisons / 2. That is true
    // from a tail of 37 keyframes.
    [Theory]
    [InlineData(36, true)]
    [InlineData(37, false)]
    public void The_frozen_rule_rejects_a_window_at_exactly_half(int tail, bool looped)
    {
        FrameRecurrence.LooksLooped(FrozenBoundaryWindow(tail)).Should().Be(looped);
    }

    [Fact]
    public void The_frozen_rule_does_not_count_the_comparison_across_a_gap()
    {
        // A gap before a frozen tail keyframe removes one frozen comparison.
        // The window at exactly half then falls below it.
        var atHalf = FrozenBoundaryWindow(37);
        FrameRecurrence.LooksLooped(atHalf, new HashSet<int> { 210 }).Should().BeTrue();

        // A gap before a moving keyframe removes one comparison that is not
        // frozen. The window just below half then reaches it.
        var belowHalf = FrozenBoundaryWindow(36);
        FrameRecurrence.LooksLooped(belowHalf, new HashSet<int> { 190 }).Should().BeFalse();
    }

    // A 13-second loop as a provider sends it. With a continuous keyframe
    // grid, the keyframe at clip second s shows second s of the clip, so the
    // period is 13 keyframes at any whole-second interval. When the encoder
    // restarts the grid at each pass, a pass holds ceil(13 / interval)
    // keyframes.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void A_13_second_loop_is_a_loop(int keyframeSeconds, bool gridRestartsEachPass)
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var keyframes = Loop13(new Random(seed), keyframeSeconds, gridRestartsEachPass);

            FrameRecurrence.LooksLooped(keyframes).Should().BeTrue($"seed {seed}");
        }
    }

    // A clip where many keyframes drift a little but do not move, such as a
    // fight seen from one camera. Only the cuts move. The drift is no
    // evidence either way, so the clip must still count as a loop.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_13_second_loop_with_few_moving_keyframes_is_a_loop(int keyframeSeconds)
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var random = new Random(seed);
            var clip = DriftingClip(random, seconds: 13, cuts: random.Next(5, 8));
            var keyframes = new List<ulong>();
            for (var second = 0; second < 180; second += keyframeSeconds)
                keyframes.Add(Reencoded(clip[second % clip.Count], random));

            FrameRecurrence.LooksLooped(keyframes).Should().BeTrue($"seed {seed}");
        }
    }

    [Fact]
    public void A_13_second_loop_on_a_continuous_5_second_grid_is_not_caught()
    {
        // This is a known limit. The period is 13 keyframes, 65 seconds. The
        // three-minute window holds only 36 keyframes, so less than 80% of
        // them can repeat. A minimum period of four keyframes would also lose
        // the 5-second case with a restarting grid (period 3), so the verdict
        // uses the distinct-pictures rule and no fixed floor above 3.
        var keyframes = Loop13(new Random(1), keyframeSeconds: 5, gridRestartsEachPass: false);

        FrameRecurrence.LooksLooped(keyframes).Should().BeFalse();
    }

    [Fact]
    public void A_frozen_picture_is_never_a_loop()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var frame = Pictures(random, 1)[0];
            var keyframes = Enumerable.Range(0, 180).Select(_ => Reencoded(frame, random)).ToList();

            FrameRecurrence.LooksLooped(keyframes).Should().BeFalse($"seed {seed}");
        }
    }

    [Fact]
    public void Keyframes_are_never_compared_across_a_gap()
    {
        // Each stretch between gaps holds exactly one pass of the clip. Inside
        // a stretch nothing repeats, so the verdict has no evidence.
        var random = new Random(8);
        var clip = Pictures(random, ClipKeyframes);
        var keyframes = Enumerable.Range(0, 72).Select(i => Reencoded(clip[i % ClipKeyframes], random)).ToList();
        var gaps = Enumerable.Range(1, 71).Where(i => i % ClipKeyframes == 0).ToHashSet();

        FrameRecurrence.LooksLooped(keyframes).Should().BeTrue();
        FrameRecurrence.LooksLooped(keyframes, gaps).Should().BeFalse();
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(13, 1)]
    [InlineData(15, 2)]
    [InlineData(60, 6)]
    public void The_period_band_grows_with_the_period(int period, int tolerance)
    {
        FrameRecurrence.Tolerance(period).Should().Be(tolerance);
    }

    [Fact]
    public void Live_footage_is_not_a_loop()
    {
        var keyframes = Pictures(new Random(2), 72);

        FrameRecurrence.LooksLooped(keyframes).Should().BeFalse();
    }

    [Fact]
    public void Live_footage_with_replays_is_not_a_loop()
    {
        // Every fifth keyframe shows a picture from earlier in the window, as
        // a replay of an overtake or a camera angle that keeps coming back.
        var random = new Random(3);
        var keyframes = Pictures(random, 72);
        for (var i = 10; i < keyframes.Count; i += 5)
            keyframes[i] = Reencoded(keyframes[i - 8], random);

        FrameRecurrence.LooksLooped(keyframes).Should().BeFalse();
    }

    [Fact]
    public void A_still_picture_is_not_a_loop()
    {
        // A holding slide or a frozen frame repeats one picture with nothing
        // moving. That is the growth check's business, not this one's.
        var random = new Random(4);
        var slide = Pictures(random, 1)[0];
        var keyframes = Enumerable.Range(0, 72).Select(_ => Reencoded(slide, random)).ToList();

        FrameRecurrence.LooksLooped(keyframes).Should().BeFalse();
    }

    [Fact]
    public void Too_little_footage_is_no_verdict()
    {
        // These are nine keyframes of a three-keyframe clip. Nearly all of
        // them repeat. But nine keyframes are too few to fail a recording.
        var random = new Random(5);
        var clip = Pictures(random, 3);
        var keyframes = Enumerable.Range(0, 9).Select(i => Reencoded(clip[i % 3], random)).ToList();

        FrameRecurrence.LooksLooped(keyframes).Should().BeFalse();
    }

    [Fact]
    public void A_picture_brighter_to_the_left_everywhere_sets_every_bit()
    {
        var thumbnail = Thumbnail((x, _) => (byte)(200 - x * 10));

        FrameRecurrence.Fingerprint(thumbnail).Should().Be(ulong.MaxValue);
    }

    [Fact]
    public void A_picture_brighter_to_the_right_everywhere_sets_no_bit()
    {
        var thumbnail = Thumbnail((x, _) => (byte)(x * 10));

        FrameRecurrence.Fingerprint(thumbnail).Should().Be(0UL);
    }

    [Fact]
    public void Back_to_back_thumbnails_are_split_and_a_partial_one_is_ignored()
    {
        var bright = Thumbnail((x, _) => (byte)(200 - x * 10));
        var dark = Thumbnail((x, _) => (byte)(x * 10));
        var raw = bright.Concat(dark).Concat(bright.Take(10)).ToArray();

        FrameRecurrence.Fingerprints(raw).Should().Equal(ulong.MaxValue, 0UL);
    }

    [Fact]
    public void A_thumbnail_of_the_wrong_size_is_refused()
    {
        var act = () => FrameRecurrence.Fingerprint(new byte[FrameRecurrence.ThumbnailBytes - 1]);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Keyframes of a studio with cameras that take turns. Each camera shows
    /// one scene. Its shots start a few bits away from that scene, and each
    /// keyframe in a shot drifts a few bits from the one before, as a
    /// presenter moves.
    /// </summary>
    private static List<ulong> Studio(Random random, int cameras, int minShot, int maxShot, int length)
    {
        var scenes = Pictures(random, cameras);
        var keyframes = new List<ulong>(length);
        var camera = 0;
        while (keyframes.Count < length)
        {
            var picture = Flip(scenes[camera], random, random.Next(3));
            var shot = random.Next(minShot, maxShot + 1);
            for (var i = 0; i < shot && keyframes.Count < length; i++)
            {
                keyframes.Add(picture);
                picture = Flip(picture, random, 1 + random.Next(3));
            }
            camera = cameras == 2 ? 1 - camera : (camera + 1 + random.Next(cameras - 1)) % cameras;
        }
        return keyframes;
    }

    /// <summary>
    /// Three minutes of keyframes of a 13-second clip in a loop. The clip
    /// has one picture per second of footage.
    /// </summary>
    private static List<ulong> Loop13(Random random, int keyframeSeconds, bool gridRestartsEachPass)
    {
        const int ClipSeconds = 13;
        var clip = Pictures(random, ClipSeconds);
        var keyframes = new List<ulong>();
        if (gridRestartsEachPass)
        {
            for (var second = 0; second < 180; second += ClipSeconds)
                for (var at = 0; at < ClipSeconds && second + at < 180; at += keyframeSeconds)
                    keyframes.Add(Reencoded(clip[at], random));
        }
        else
        {
            for (var second = 0; second < 180; second += keyframeSeconds)
                keyframes.Add(Reencoded(clip[second % ClipSeconds], random));
        }
        return keyframes;
    }

    private static List<ulong> FrozenBoundaryWindow(int tail)
    {
        var random = new Random(10);
        var pictures = Pictures(random, 6);
        // Positions 1, 3, 5 and 7 repeat the picture before them.
        var clip = new[] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 5 };
        var keyframes = new List<ulong>();
        for (var pass = 0; pass < 20; pass++)
            keyframes.AddRange(clip.Select(p => Reencoded(pictures[p], random)));
        for (var i = 0; i < tail; i++)
            keyframes.Add(Reencoded(pictures[5], random));
        return keyframes;
    }

    /// <summary>
    /// One picture per second of a clip. A cut gives a new picture. Between
    /// cuts each second drifts seven to nine bits from the one before. The
    /// first second is always a cut, so the clip also moves where it starts
    /// again.
    /// </summary>
    private static List<ulong> DriftingClip(Random random, int seconds, int cuts)
    {
        var cutAt = new HashSet<int> { 0 };
        while (cutAt.Count < cuts)
            cutAt.Add(random.Next(1, seconds));

        var clip = new List<ulong>(seconds);
        for (var second = 0; second < seconds; second++)
            clip.Add(cutAt.Contains(second) ? Pictures(random, 1)[0] : FlipDistinct(clip[^1], random, random.Next(7, 10)));
        return clip;
    }

    private static ulong FlipDistinct(ulong picture, Random random, int bits)
    {
        foreach (var bit in Enumerable.Range(0, 64).OrderBy(_ => random.Next()).Take(bits))
            picture ^= 1UL << bit;
        return picture;
    }

    private static ulong Flip(ulong picture, Random random, int bits)
    {
        for (var i = 0; i < bits; i++)
            picture ^= 1UL << random.Next(64);
        return picture;
    }

    private static List<ulong> Pictures(Random random, int count) =>
        Enumerable.Range(0, count).Select(_ => (ulong)random.NextInt64() ^ ((ulong)random.Next(2) << 63)).ToList();

    // A re-encoded copy of a picture differs in a bit or two.
    private static ulong Reencoded(ulong picture, Random random) =>
        picture ^ (1UL << random.Next(64)) ^ (random.Next(2) == 0 ? 0UL : 1UL << random.Next(64));

    private static byte[] Thumbnail(Func<int, int, byte> brightness)
    {
        var thumbnail = new byte[FrameRecurrence.ThumbnailBytes];
        for (var y = 0; y < FrameRecurrence.ThumbnailHeight; y++)
            for (var x = 0; x < FrameRecurrence.ThumbnailWidth; x++)
                thumbnail[y * FrameRecurrence.ThumbnailWidth + x] = brightness(x, y);
        return thumbnail;
    }
}
