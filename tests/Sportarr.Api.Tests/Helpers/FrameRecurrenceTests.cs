using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sportarr.Api.Helpers;
using Xunit;

namespace Sportarr.Api.Tests.Helpers;

/// <summary>
/// The loop verdict behind the DVR watchdog's looping-stream check. A provider
/// that loses a channel can keep it up by replaying its last few seconds; the
/// file keeps growing, so only the pictures give it away. The verdict must
/// catch that and must never fail a live recording over a replay or a still
/// picture.
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

        FrameRecurrence.LooksLooped([], keyframes).Should().BeTrue();
    }

    [Fact]
    public void A_clip_that_does_not_fill_whole_keyframe_intervals_is_still_a_loop()
    {
        // A 13-second clip with a keyframe every 2 seconds: passes alternate
        // between six and seven keyframes, so the period wobbles by one.
        var random = new Random(6);
        var clip = Pictures(random, 7);
        var keyframes = new List<ulong>();
        for (var pass = 0; keyframes.Count < 72; pass++)
            keyframes.AddRange(clip.Take(pass % 2 == 0 ? 6 : 7).Select(p => Reencoded(p, random)));

        FrameRecurrence.LooksLooped([], keyframes).Should().BeTrue();
    }

    [Fact]
    public void A_clip_longer_than_the_recent_keyframes_is_a_loop_when_its_last_pass_is_earlier()
    {
        // A two-minute clip at a keyframe every second outlasts a judged
        // stretch of 72 keyframes: none of them repeats another of them.
        var random = new Random(8);
        var clip = Pictures(random, 120);
        var earlier = clip.Select(p => Reencoded(p, random)).ToList();
        var recent = clip.Take(72).Select(p => Reencoded(p, random)).ToList();

        FrameRecurrence.LooksLooped(earlier, recent).Should().BeTrue();
        FrameRecurrence.LooksLooped([], recent).Should().BeFalse();
    }

    [Fact]
    public void Only_the_recent_keyframes_are_judged()
    {
        // Live footage back after a long loop: the loop still fills the
        // earlier keyframes, but no recent one repeats it.
        var random = new Random(9);
        var clip = Pictures(random, ClipKeyframes);
        var earlier = Enumerable.Range(0, 400).Select(i => Reencoded(clip[i % ClipKeyframes], random)).ToList();
        var recent = Pictures(random, 72);

        FrameRecurrence.LooksLooped(earlier, recent).Should().BeFalse();
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

        FrameRecurrence.LooksLooped([], keyframes).Should().BeFalse();
    }

    [Fact]
    public void Live_footage_is_not_a_loop()
    {
        var keyframes = Pictures(new Random(2), 72);

        FrameRecurrence.LooksLooped([], keyframes).Should().BeFalse();
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

        FrameRecurrence.LooksLooped([], keyframes).Should().BeFalse();
    }

    [Fact]
    public void A_still_picture_is_not_a_loop()
    {
        // A holding slide or a frozen frame repeats one picture with nothing
        // moving. That is the growth check's business, not this one's.
        var random = new Random(4);
        var slide = Pictures(random, 1)[0];
        var keyframes = Enumerable.Range(0, 72).Select(_ => Reencoded(slide, random)).ToList();

        FrameRecurrence.LooksLooped([], keyframes).Should().BeFalse();
    }

    [Fact]
    public void Too_little_footage_is_no_verdict()
    {
        // Nine keyframes of a three-keyframe clip: nearly all of them recur,
        // but nine keyframes is too little to fail a recording over.
        var random = new Random(5);
        var clip = Pictures(random, 3);
        var keyframes = Enumerable.Range(0, 9).Select(i => Reencoded(clip[i % 3], random)).ToList();

        FrameRecurrence.LooksLooped([], keyframes).Should().BeFalse();
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
