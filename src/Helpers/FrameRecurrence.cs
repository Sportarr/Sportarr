namespace Sportarr.Api.Helpers;

/// <summary>
/// Tells a stream that plays the same short clip again and again apart from
/// live footage. It uses one small fingerprint per keyframe.
///
/// Some IPTV providers keep a channel up after its source drops. They loop
/// the last seconds they received. The output file keeps its normal growth,
/// so the growth check does not see a problem. The recording then holds the
/// same clip until the window closes.
///
/// The fingerprint is a 64-bit difference hash of a 9x8 grayscale thumbnail.
/// It survives re-encoding and scaling, and it is cheap to compare.
///
/// Only a keyframe whose picture moved since the previous keyframe counts.
/// A still picture, such as a holding slide or a frozen frame, is no
/// evidence. A moving keyframe is evidence of a loop when it nearly matches
/// an earlier keyframe at the loop period.
///
/// The period separates a loop from a studio. Two cameras that take turns
/// also show the same pictures again and again. But a cut back to one camera
/// comes after the other presenter stops talking, not after a fixed clip
/// length. A loop also shows at least three different pictures in each pass.
/// Two cameras or two slides that take turns show only two.
/// </summary>
public static class FrameRecurrence
{
    public const int ThumbnailWidth = 9;
    public const int ThumbnailHeight = 8;
    public const int ThumbnailBytes = ThumbnailWidth * ThumbnailHeight;

    // These are Hamming distances out of 64 bits. Re-encoded copies of one
    // picture differ by a few bits. Two keyframes of moving footage a few
    // seconds apart differ by more than a dozen bits.
    private const int SamePictureMaxDistance = 6;
    private const int MovedPictureMinDistance = 10;

    // Fewer moving keyframes than this are too little footage for a verdict.
    // A keyframe every ten seconds gives eighteen in three minutes. A keyframe
    // every second gives 180.
    private const int MinMovingKeyframes = 10;

    // A loop must show this many clearly different pictures in its repeats.
    // Two cameras or two slides that take turns show only two.
    private const int MinDistinctPictures = 3;

    // A period below three keyframes cannot hold three different pictures.
    private const int MinPeriod = 3;

    // Below this share of frozen keyframes the window can be a loop. A
    // frozen keyframe shows the same picture as the keyframe before it. A
    // slide carousel that holds each slide for two keyframes or more is
    // frozen at half of its keyframes or more. A loop of footage is rarely
    // frozen. A keyframe that drifts a little but does not move counts
    // neither way.
    //
    // This rule does not cover a carousel that changes the slide at every
    // keyframe. Any content that repeats on a fixed cycle counts as a loop.
    // Examples are an animated carousel and cameras that cut in a fixed
    // order. The watchdog still needs a usable fallback before it acts.
    //
    // Gentle drift can hide a loop. On a continuous 2-second keyframe grid,
    // a keyframe can nearly match a keyframe a few positions back. The
    // nearest match then gives a short lag, and the loop is missed.
    private const double MaxFrozenShare = 0.5;

    // When the keyframe interval does not divide the clip length, the period
    // changes by one keyframe from pass to pass. The band allows for that.
    // The band grows with the period, so that it stays near a tenth of it.
    // Periods of three and four keyframes get no band. A band of one keyframe
    // there accepts lags that differ by a third, and a fast studio edit fills
    // that band by chance.
    private const int PeriodsPerToleranceStep = 10;

    // This is the share of moving keyframes that must repeat at the period.
    // Synthetic loops of a 13-second race clip scored 0.93 and 0.95. Across
    // 40 recordings of races, fight nights and studio talk shows, no
    // three-minute window scored above 0.53.
    private const double LoopedShare = 0.8;

    /// <summary>
    /// The difference hash of one 9x8 grayscale thumbnail. The thumbnail has
    /// one byte per pixel, row by row. A bit is set where a pixel is brighter
    /// than its right neighbour.
    /// </summary>
    public static ulong Fingerprint(ReadOnlySpan<byte> thumbnail)
    {
        if (thumbnail.Length != ThumbnailBytes)
            throw new ArgumentException($"A thumbnail is {ThumbnailBytes} bytes, got {thumbnail.Length}", nameof(thumbnail));

        ulong bits = 0;
        for (var y = 0; y < ThumbnailHeight; y++)
        {
            for (var x = 0; x < ThumbnailWidth - 1; x++)
            {
                var pixel = y * ThumbnailWidth + x;
                bits = (bits << 1) | (thumbnail[pixel] > thumbnail[pixel + 1] ? 1UL : 0UL);
            }
        }
        return bits;
    }

    /// <summary>
    /// Fingerprints of back-to-back thumbnails, as FFmpeg's rawvideo output
    /// writes them. The method ignores a partial thumbnail at the end.
    /// </summary>
    public static List<ulong> Fingerprints(ReadOnlySpan<byte> thumbnails)
    {
        var result = new List<ulong>(thumbnails.Length / ThumbnailBytes);
        for (var offset = 0; offset + ThumbnailBytes <= thumbnails.Length; offset += ThumbnailBytes)
        {
            result.Add(Fingerprint(thumbnails.Slice(offset, ThumbnailBytes)));
        }
        return result;
    }

    /// <summary>
    /// True when the keyframes, oldest first, look like a short clip that
    /// plays again and again.
    /// </summary>
    public static bool LooksLooped(IReadOnlyList<ulong> keyframes) => LooksLooped(keyframes, null);

    /// <summary>
    /// True when the keyframes, oldest first, look like a short clip that
    /// plays again and again. <paramref name="gapsBefore"/> holds the indexes
    /// of keyframes that follow footage that was not sampled. The verdict
    /// does not compare keyframes across a gap.
    /// </summary>
    public static bool LooksLooped(IReadOnlyList<ulong> keyframes, IReadOnlySet<int>? gapsBefore)
    {
        var moving = 0;
        var frozen = 0;
        var compared = 0;
        var repeats = new List<(int Index, int Lag)>();
        var segmentStart = 0;
        for (var i = 1; i < keyframes.Count; i++)
        {
            if (gapsBefore != null && gapsBefore.Contains(i))
            {
                // Count this keyframe as moving with no repeat. That can only
                // lower the score, so a gap never helps a false verdict.
                segmentStart = i;
                moving++;
                continue;
            }

            compared++;
            var step = Distance(keyframes[i], keyframes[i - 1]);
            if (step <= SamePictureMaxDistance)
                frozen++;
            if (step < MovedPictureMinDistance)
                continue;

            moving++;
            if (LagToPreviousMatch(keyframes, i, segmentStart) is int lag)
                repeats.Add((i, lag));
        }

        // The comparison across a gap does not count here. The one extra
        // frozen keyframe covers a window that starts or ends in the middle
        // of a slide.
        if (moving < MinMovingKeyframes || frozen + 1 >= compared * MaxFrozenShare)
            return false;

        foreach (var period in repeats.Select(r => r.Lag).Distinct())
        {
            if (period < MinPeriod)
                continue;

            var tolerance = Tolerance(period);
            var atPeriod = repeats.Where(r => Math.Abs(r.Lag - period) <= tolerance).ToList();
            if (atPeriod.Count >= moving * LoopedShare && HasDistinctPictures(keyframes, atPeriod))
                return true;
        }
        return false;
    }

    /// <summary>How many keyframes back the nearest near-match of this one is.</summary>
    private static int? LagToPreviousMatch(IReadOnlyList<ulong> keyframes, int index, int segmentStart)
    {
        // The keyframe right before is never a match. The picture moved away
        // from it, so this keyframe counts at all.
        for (var j = index - 2; j >= segmentStart; j--)
        {
            if (Distance(keyframes[index], keyframes[j]) <= SamePictureMaxDistance)
                return index - j;
        }
        return null;
    }

    internal static int Tolerance(int period) =>
        (period + PeriodsPerToleranceStep / 2) / PeriodsPerToleranceStep;

    /// <summary>
    /// True when the repeats show enough clearly different pictures. Each
    /// picture that is far from all pictures kept so far is kept. The kept
    /// pictures are then far from each other in pairs.
    /// </summary>
    private static bool HasDistinctPictures(IReadOnlyList<ulong> keyframes, List<(int Index, int Lag)> repeats)
    {
        var kept = new List<ulong>(MinDistinctPictures);
        foreach (var (index, _) in repeats)
        {
            var picture = keyframes[index];
            if (kept.All(k => Distance(k, picture) >= MovedPictureMinDistance))
            {
                kept.Add(picture);
                if (kept.Count >= MinDistinctPictures)
                    return true;
            }
        }
        return false;
    }

    private static int Distance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);
}
