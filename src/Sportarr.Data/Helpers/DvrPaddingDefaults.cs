namespace Sportarr.Api.Helpers;

/// <summary>
/// Sport- and league-aware default DVR padding. Sports overrun
/// predictably and that overrun pattern is what determines a sane
/// default - NFL games run long, soccer rarely does, F1 sessions are
/// punctual but the wrap-up commentary spills over.
///
/// Resolution order at scheduling time:
///   1. League-level override (League.DvrPrePadMinutes /
///      DvrPostRollMinutes)
///   2. Sport-level default below
///   3. Caller's caller-supplied default (typically the global
///      DVR config setting)
///
/// Numbers below are derived from common community guidance for
/// commercial DVR products and from public broadcast schedules
/// (e.g. NFL window typically scheduled 3:00 but games average
/// 3:11 with overruns to 3:30+; EPL allots 105 min for a 90 min
/// match plus stoppage).
/// </summary>
public static class DvrPaddingDefaults
{
    public record Padding(int PrePadMinutes, int PostRollMinutes);

    private static readonly Dictionary<string, Padding> SportDefaults =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "American Football", new Padding(2, 30) },
            { "Football",          new Padding(2, 30) }, // some sources tag NFL as "Football"
            { "Basketball",        new Padding(2, 15) },
            { "Baseball",          new Padding(2, 20) },
            { "Ice Hockey",        new Padding(2, 15) },
            { "Hockey",            new Padding(2, 15) },
            { "Soccer",            new Padding(2, 15) }, // 90 min + stoppage + post-match
            { "Rugby",             new Padding(2, 15) },
            { "Cricket",           new Padding(2, 30) }, // notoriously variable
            { "Tennis",            new Padding(2, 60) }, // five-set Slams
            { "Golf",              new Padding(5, 60) }, // playoff potential
            { "Motorsport",        new Padding(5, 15) },
            { "Fighting",          new Padding(5, 30) }, // PPV pre-show + multiple cards
            { "MMA",               new Padding(5, 30) },
            { "Boxing",            new Padding(5, 30) },
            { "Wrestling",         new Padding(2, 15) },
            { "eSports",           new Padding(2, 30) },
        };

    /// <summary>
    /// Resolve the effective padding for a league, blending the
    /// league override, sport default, and a caller fallback.
    /// </summary>
    public static Padding Resolve(string? sport, int? leaguePre, int? leaguePost, int fallbackPre, int fallbackPost)
    {
        var sportPad = !string.IsNullOrWhiteSpace(sport) && SportDefaults.TryGetValue(sport, out var s)
            ? s
            : new Padding(fallbackPre, fallbackPost);

        var pre = leaguePre ?? sportPad.PrePadMinutes;
        var post = leaguePost ?? sportPad.PostRollMinutes;
        return new Padding(pre, post);
    }
}
