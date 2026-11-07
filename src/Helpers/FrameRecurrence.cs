namespace Sportarr.Api.Helpers;

/// <summary>
/// Tells a stream that replays the same few seconds over and over apart from
/// live footage, from one tiny fingerprint per keyframe.
///
/// Some IPTV providers keep a channel up when its source drops by looping the
/// last seconds they received. The output file keeps growing at the normal
/// bitrate, so a growth check never notices, and the recording fills with the
/// same clip until the window closes.
///
/// The fingerprint is a 64-bit difference hash of a 9x8 grayscale thumbnail.
/// It survives re-encoding and scaling and costs next to nothing to compare.
///
/// Only keyframes whose picture moved since the keyframe before count. A still
/// picture (a holding slide, a frozen frame) is no evidence either way. A
/// moving keyframe is evidence of a loop when it nearly matches an earlier one
/// at the loop's period: about the same number of keyframes back as most other
/// repeats in the window. The period is what tells a loop from a studio. Two
/// presenters on two cameras repeat the same pictures all the time too, but
/// a cut back to a camera comes after however long the other one talked, not
/// after a fixed clip length.
/// </summary>
public static class FrameRecurrence
{
    public const int ThumbnailWidth = 9;
    public const int ThumbnailHeight = 8;
    public const int ThumbnailBytes = ThumbnailWidth * ThumbnailHeight;

    // Hamming distances out of 64 bits. Re-encoded copies of one picture land
    // within a few bits of each other; two keyframes of moving footage a few
    // seconds apart differ in well over a dozen.
    private const int SamePictureMaxDistance = 6;
    private const int MovedPictureMinDistance = 10;

    // Below this many moving keyframes there is too little footage to judge.
    // A provider that sends a keyframe every ten seconds yields eighteen in a
    // three-minute window; one that sends one every second yields 180.
    private const int MinMovingKeyframes = 10;

    // A keyframe interval that does not divide the clip length makes the
    // period wobble by one keyframe between passes.
    private const int PeriodTolerance = 1;

    // Share of moving keyframes that must repeat at the period. Synthetic loops
    // of a 13-second race clip scored 0.93 and 0.95. Across 40 recordings of
    // races, fight nights and studio talk shows, no three-minute window scored
    // above 0.53, which was two cameras taking turns on a studio interview.
    private const double LoopedShare = 0.8;

    /// <summary>
    /// The difference hash of one 9x8 grayscale thumbnail, one byte per pixel,
    /// row by row: bit set where a pixel is brighter than its right neighbour.
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
    /// writes them. A trailing partial thumbnail is ignored.
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
    /// True when the keyframes, oldest first, look like a short clip played
    /// over and over rather than live footage.
    /// </summary>
    public static bool LooksLooped(IReadOnlyList<ulong> keyframes)
    {
        var moving = 0;
        var repeatsByLag = new Dictionary<int, int>();
        for (var i = 1; i < keyframes.Count; i++)
        {
            if (Distance(keyframes[i], keyframes[i - 1]) < MovedPictureMinDistance)
                continue;

            moving++;
            if (LagToPreviousMatch(keyframes, i) is int lag)
                repeatsByLag[lag] = repeatsByLag.GetValueOrDefault(lag) + 1;
        }

        return moving >= MinMovingKeyframes && RepeatsAtPeriod(repeatsByLag) >= moving * LoopedShare;
    }

    /// <summary>How many keyframes back the nearest near-match of this one is.</summary>
    private static int? LagToPreviousMatch(IReadOnlyList<ulong> keyframes, int index)
    {
        // The keyframe right before is never a match: the picture moved away
        // from it, which is what made this one count.
        for (var j = index - 2; j >= 0; j--)
        {
            if (Distance(keyframes[index], keyframes[j]) <= SamePictureMaxDistance)
                return index - j;
        }
        return null;
    }

    /// <summary>
    /// Repeats at the best-supported period: the lag whose band, give or take
    /// the tolerance, holds the most repeats.
    /// </summary>
    private static int RepeatsAtPeriod(Dictionary<int, int> repeatsByLag) =>
        repeatsByLag.Keys
            .Select(period => Enumerable.Range(period - PeriodTolerance, 2 * PeriodTolerance + 1)
                .Sum(lag => repeatsByLag.GetValueOrDefault(lag)))
            .DefaultIfEmpty(0)
            .Max();

    private static int Distance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);
}
