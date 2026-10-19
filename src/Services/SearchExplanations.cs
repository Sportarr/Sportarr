using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// A plain explanation of one rejection reason. Title groups the reasons for
/// the counts. Help says what the reason means and what you can do.
/// </summary>
public sealed record ReasonExplanation(string Title, string? Help);

/// <summary>
/// A plain explanation of how a search ended.
/// </summary>
public sealed record OutcomeExplanation(string Headline, string? Help);

/// <summary>
/// Turns the internal search messages and rejection reasons into sentences
/// for people who do not know how the matcher works. The raw text stays in
/// the record and in the logs for support.
/// </summary>
public static class SearchExplanations
{
    private const string ManualSearchTip = "Use Manual Search to look at the releases and grab one yourself.";
    private static readonly Regex Digits = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Regex Quoted = new(@"'([^']+)'", RegexOptions.Compiled);

    private static bool Starts(string reason, params string[] prefixes) =>
        prefixes.Any(prefix => reason.StartsWith(prefix, StringComparison.Ordinal));

    private static readonly ReasonExplanation NameDoesNotSayEvent = new("Name does not say which event",
        "The release name does not say which race, round or game it is, so Sportarr cannot tell it is this one. " + ManualSearchTip);

    private static readonly ReasonExplanation OtherEvent = new("Belongs to another event",
        "The release name points to another competition, league, series or event.");

    // The order matters. Specific rules come before the broad word rules.
    private static readonly (Func<string, bool> Match, ReasonExplanation Explanation)[] Reasons =
    {
        (r => r.StartsWith("Quality ", StringComparison.Ordinal) && r.EndsWith(" is not wanted in quality profile", StringComparison.Ordinal),
            new("Quality not allowed",
                "Your quality profile does not allow this quality, for example 720p when only 1080p is allowed. You can allow more qualities under Settings > Profiles.")),
        (r => Starts(r, "Size "),
            new("File size outside the limits",
                "The file is bigger or smaller than the size limits for its quality. You can change the limits under Settings > Quality.")),
        (r => Starts(r, "Custom format score "),
            new("Custom format score too low",
                "A custom format in your quality profile gave this release a low score, so it fell below the minimum. Check the custom format scores under Settings > Profiles.")),
        (r => Starts(r, "Release profile "),
            new("Blocked by a release profile",
                "A release profile asks for or bans certain words in the release name. You can change it under Settings > Profiles.")),
        (r => r == "Release is blocklisted",
            new("On your blocklist",
                "This release was blocked before, usually after a failed download. You can remove it under Activity > Blocklist.")),
        (r => r == "No seeders available",
            new("Nobody is sharing it",
                "The torrent has no seeders right now, so it cannot download.")),
        (r => Starts(r, SearchHistoryService.LowConfidencePrefix),
            new("Not sure it is this event",
                "The release passed every check, but its name only loosely matches the event. Automatic searches skip these. " + ManualSearchTip)),
        (r => Starts(r, SearchHistoryService.MinimumAgePrefix),
            new("Too new",
                "The release came out a short while ago. The Minimum Age setting under Settings > Indexers holds it back for now.")),
        (r => Starts(r, "Multi-part disabled"),
            new("Part of an event",
                "The release is only one part of the event. Part files are skipped while Enable Multi-Part Episodes is off under Settings > Media Management.")),
        (r => Starts(r, "Requested part ", "Wrong part"),
            new("Wrong part of the event",
                "The release is a different part of the event, for example the prelims when you want the main card.")),
        (r => Starts(r, "Release is ") && r.Contains(" days old ", StringComparison.Ordinal),
            new("Older than your retention setting",
                "The release is older than the Retention setting under Settings > Indexers. Raise it, or set it to 0, if your provider keeps posts longer.")),
        (r => Starts(r, "Non-event content"),
            new("Highlights or extra content",
                "The name suggests highlights, a preview or a show about the event, not the event itself. If you want highlights, turn on Allow highlights releases when you edit the league.")),
        (r => Starts(r, "Release posted ") && r.Contains("before event aired", StringComparison.Ordinal),
            new("Posted before the event happened",
                "The release appeared days before the event started, so it is probably fake or named wrong.")),
        (r => Starts(r, "Insufficient evidence", "Game identity is ambiguous", "No league identity")
              || r.Contains("does not identify", StringComparison.Ordinal),
            NameDoesNotSayEvent),
        (r => (Starts(r, "Event is '") && r.EndsWith("but release has no session indicator", StringComparison.Ordinal))
              || r.Contains("does not name the selected support session", StringComparison.Ordinal)
              || Starts(r, "Rally stage missing"),
            new("Name does not say which session",
                "The release name does not say which session or stage it is, for example practice, qualifying or race, so Sportarr cannot tell it is this one.")),
        (r => Starts(r, "Release doesn't match event"),
            new("Different event",
                "The release name does not match this event closely enough. It is most likely about another event.")),
        (r => Starts(r, "Different sport"),
            new("Different sport",
                "The release name points to another sport.")),
        (r => Starts(r, "Participant category", "Cycling category", "Women's league identity"),
            new("Different category",
                "The release is for another category of the sport, for example the women's or youth competition.")),
        (r => Starts(r, "Date mismatch", "Date matches another game", "Year mismatch", "Day mismatch", "Release specifies Day"),
            new("Different date",
                "The date or year in the release name belongs to another event.")),
        (r => Starts(r, "Round mismatch", "Stage mismatch", "Rally stage mismatch", "Race mismatch", "Session mismatch",
                  "Release is pre-season testing", "Release is a race weekend"),
            new("Different round or session",
                "The round, race, stage or session in the release name is not this event's.")),
        (r => Starts(r, "Location mismatch"),
            new("Different place",
                "The venue or race in the release name is not this event's.")),
        (r => Starts(r, "Could not confirm both teams in a non-Latin"),
            new("Team names in another alphabet",
                "Sportarr could not find both teams in a release name written in another alphabet. You can add team names in that language as team aliases on the league page.")),
        (r => Starts(r, "Team names not found", "Only one team name found", "Only one participant name found"),
            new("Teams or players do not match",
                "Sportarr could not find this event's teams or players in the release name.")),
    };

    private static readonly string[] OtherEventWords =
    {
        "does not match", "conflicts with", "different competition", "tagged for a different", "conflicting event",
        "another series", "cannot satisfy", "not found in release", "Event type mismatch", "Event number mismatch",
    };

    private static readonly ReasonExplanation[] Known = Reasons.Select(r => r.Explanation).Append(OtherEvent).Distinct().ToArray();

    /// <summary>
    /// Explains one raw rejection reason. An unknown reason keeps its own
    /// text as the title and has no help.
    /// </summary>
    public static ReasonExplanation ForReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return new("Other", null);
        foreach (var (match, explanation) in Reasons)
        {
            if (match(reason))
                return explanation;
        }
        if (OtherEventWords.Any(word => reason.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return OtherEvent;

        var shape = Digits.Replace(reason, "#");
        if (shape.Length > 60)
        {
            var cut = shape.LastIndexOf(' ', 60);
            shape = (cut > 20 ? shape[..cut] : shape[..60]) + "...";
        }
        return new(shape, null);
    }

    /// <summary>
    /// The plain title for a reason, or null when no rule knows the reason.
    /// </summary>
    public static string? PlainTitle(string reason) =>
        ForReason(reason) is { Help: not null } explanation ? explanation.Title : null;

    /// <summary>
    /// The help text for a group title, for records that only stored the title.
    /// </summary>
    public static string? HelpForTitle(string title) =>
        Known.FirstOrDefault(e => e.Title == title)?.Help;

    /// <summary>
    /// Explains how a search ended in one or two plain sentences.
    /// </summary>
    /// <param name="indexersAsked">How many indexers the search asked.</param>
    /// <param name="indexersAnswered">How many of them gave a full answer.</param>
    public static OutcomeExplanation ForOutcome(string outcome, string? message, string? selectedRelease,
        int indexersAsked = 1, int indexersAnswered = 1)
    {
        var text = (message ?? "").Trim();
        if (text.StartsWith("Error: ", StringComparison.Ordinal))
            text = text["Error: ".Length..].Trim();

        // These two messages explain a whole search better than the reasons do.
        if (text.StartsWith("Found ", StringComparison.Ordinal) && text.Contains("sufficient match confidence", StringComparison.Ordinal))
            return new("Releases passed the checks, but none matched this event closely enough to download automatically.", ManualSearchTip);
        if (text.StartsWith("No valid releases found", StringComparison.Ordinal))
            return new("Every release belonged to a different date, round or event.", ManualSearchTip);

        switch (outcome)
        {
            case Models.SearchOutcome.Grabbed:
                return new("Found a release and sent it to your download client.", selectedRelease);
            case Models.SearchOutcome.NoResults when indexersAsked == 0:
                return new("No indexer was searched.",
                    "Sportarr only searches an indexer when a download client can take its releases. Check Settings > Indexers and Settings > Download Clients.");
            case Models.SearchOutcome.NoResults when indexersAnswered == 0:
                return new("Your indexers did not answer, so nothing could be found.",
                    "See how each indexer answered below. Try again later, or check the indexers under Settings > Indexers.");
            case Models.SearchOutcome.NoResults:
                return new("Your indexers found no releases for this event.",
                    "The event may not be out yet, or your indexers name it differently. Manual Search lets you try your own search words.");
            case Models.SearchOutcome.NothingPassed:
                return new("Releases were found, but every one was rejected.",
                    "The reasons below show why. Start with the most common one.");
        }

        if (text.StartsWith("Only unmonitored parts available", StringComparison.Ordinal))
            return new("Only parts of the event you do not monitor were found.",
                "Sportarr waits for a part you monitor. You can change the monitored parts in the league settings.");
        if (text.StartsWith("Only releases for already downloaded parts", StringComparison.Ordinal))
            return new("Only parts you already have were found.", null);
        if (text.StartsWith("No releases found matching existing parts", StringComparison.Ordinal))
            return new("Releases were found, but none match the quality and format of the parts you already have.",
                "Parts of one event must match to play back as one. " + ManualSearchTip);
        if (text.StartsWith("No quality profile configured", StringComparison.Ordinal))
            return new("No quality profile is set up, so Sportarr cannot judge releases.",
                "Add a quality profile under Settings > Profiles.");
        if (text.StartsWith("All candidate releases were already grabbed", StringComparison.Ordinal))
            return new("Every good release was downloaded before but never imported, so Sportarr waits before it downloads one again.",
                "Look in Activity for the import that failed and fix that first.");
        if (text.StartsWith("Only releases already held", StringComparison.Ordinal))
            return new("Only releases you already have were found.", null);
        if (text.StartsWith("Automatic retry is disabled", StringComparison.Ordinal))
            return new("A download for this event failed, and automatic retry is off.",
                "Turn on Redownload under Settings > Download Clients, or use Manual Search.");
        if (text.StartsWith("Download retry limit", StringComparison.Ordinal))
            return new("A download for this event failed 3 times, so Sportarr stopped retrying.", ManualSearchTip);
        if (text.StartsWith("Recent failed download", StringComparison.Ordinal) || text.StartsWith("No releases available for retry", StringComparison.Ordinal))
            return new("A download for this event failed recently, so Sportarr waits a while before it tries again.", null);
        if (text.StartsWith("Only releases above the reached quality cutoff", StringComparison.Ordinal))
            return new("You already have a file at the quality you aim for, so a better one is not needed.", null);
        if (text.StartsWith("No releases available", StringComparison.Ordinal))
            return new("Good releases were found, but none could be picked yet, usually because a delay profile holds them back.",
                "A later search can grab one once the delay ends. You can change delays under Settings > Profiles.");
        if (text.StartsWith("Already downloading", StringComparison.Ordinal))
            return new("A download for this event is already running.", Quoted.Match(text) is { Success: true } m ? m.Groups[1].Value : null);
        if (text.StartsWith("Full event file already exists", StringComparison.Ordinal))
            return new("You already have the full event, which covers every part.", null);
        if (text.StartsWith("Existing file quality is unrecognized", StringComparison.Ordinal))
            return new("Sportarr cannot read the quality of the file you already have, so it will not replace it on its own.", ManualSearchTip);
        if (text.StartsWith("Upgrades disabled", StringComparison.Ordinal))
            return new("You already have a file, and your quality profile does not allow upgrades.",
                "You can turn on upgrades in the quality profile under Settings > Profiles.");
        if (text.StartsWith("Cutoff met", StringComparison.Ordinal))
            return new("You already have a file that meets your quality profile, so no upgrade is needed.", null);
        if (text.Contains("is already good enough", StringComparison.Ordinal))
            return new("The file you already have is as good as or better than what was found.", null);
        if (text.StartsWith("The source indexer", StringComparison.Ordinal))
            return new("The indexer that found the release is turned off for this kind of search.",
                "Check the indexer under Settings > Indexers.");
        if (text.StartsWith("No ", StringComparison.Ordinal) && text.EndsWith(" download client configured", StringComparison.Ordinal))
            return new("No download client is set up for this kind of release, so it could not be sent.",
                "Add one under Settings > Download Clients.");
        if (text.StartsWith("Failed to add to ", StringComparison.Ordinal))
            return new("The release could not be sent to your download client.",
                $"{text.TrimEnd('.')}. Check that the download client is running and Sportarr can reach it.");
        if (text.StartsWith("Download added to ", StringComparison.Ordinal))
            return new("The download started, but Sportarr could not keep track of it.",
                "It will finish but not import on its own. Import it by hand when it is done.");
        if (text.StartsWith("The client job ", StringComparison.Ordinal))
            return new("Your download client already has this download for another event or part, so Sportarr could not add it again.",
                "This happens when one release fits two events. Remove the other download, or pick another release with Manual Search.");
        if (text.StartsWith("Event no longer exists", StringComparison.Ordinal))
            return new("The event was removed while the search was running.", null);
        if (outcome == Models.SearchOutcome.Failed)
            return new("Something went wrong during the search.",
                string.IsNullOrEmpty(text) ? "See System > Logs for details." : $"{text.TrimEnd('.')}. See System > Logs for details.");
        return new(string.IsNullOrEmpty(text) ? "No release was downloaded." : text, null);
    }
}
