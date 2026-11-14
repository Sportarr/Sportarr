using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Makes the file match that an install reports when the user imports a
/// file to an event. The metadata service counts installs per (event,
/// stem), and an approved count becomes shared match memory.
/// </summary>
public static partial class FileMatchSuggestionCollector
{
    public const string Manual = "manual";
    public const string Confirmed = "confirmed";

    /// <summary>
    /// The confidence that the edit endpoint gives a pending import when
    /// the user picks its event.
    /// </summary>
    public const int UserChoiceConfidence = 100;

    // The metadata service accepts only its own short ids.
    [GeneratedRegex(@"^ev-[0-9]{6,29}$", RegexOptions.CultureInvariant)]
    private static partial Regex EventShortId();

    [GeneratedRegex(@"^lg-[0-9]{6,29}$", RegexOptions.CultureInvariant)]
    private static partial Regex LeagueShortId();

    /// <summary>
    /// The match for one imported file, or null when it must not be reported.
    /// The kind is "manual" when the user picked an event other than the
    /// scan's suggestion, and "confirmed" when the user kept it.
    /// </summary>
    /// <param name="filePath">The path of the file before the import.</param>
    /// <param name="evt">The event, with its league loaded.</param>
    /// <param name="suggestedEventId">The scan's suggestion, or null when it had none.</param>
    /// <param name="createNew">True when the import made a new event.</param>
    /// <param name="carriedSportarrId">The Sportarr id in the file's sidecar or tag, if any.</param>
    public static FileMatchSuggestion? FromImport(
        string filePath,
        Event evt,
        int? suggestedEventId,
        bool createNew,
        string? carriedSportarrId = null)
    {
        // A new event has no hub id that other installs can share.
        if (createNew)
            return null;

        // A Sportarr id names its event already. Match memory adds nothing.
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrWhiteSpace(carriedSportarrId) || CarriesIdToken(stem))
            return null;

        var eventId = evt.ExternalId;
        var leagueId = evt.League?.ExternalId;
        if (eventId == null || !EventShortId().IsMatch(eventId) ||
            leagueId == null || !LeagueShortId().IsMatch(leagueId))
            return null;

        var stemNorm = FileStemNormalizer.Normalize(stem);
        if (stemNorm.Length == 0)
            return null;

        var kind = suggestedEventId == evt.Id ? Confirmed : Manual;
        return new FileMatchSuggestion(leagueId, eventId, stemNorm, stem,
            string.IsNullOrWhiteSpace(evt.Season) ? null : evt.Season, kind);
    }

    /// <summary>
    /// The suggestion of a pending import before the user changed it. The
    /// row does not keep the original event, so an edited row gives null.
    /// </summary>
    public static int? SuggestionBeforeUserChoice(PendingImport import) =>
        import.SuggestionConfidence == UserChoiceConfidence ? null : import.SuggestedEventId;

    /// <summary>
    /// The match for an accepted pending import, or null when it must not
    /// be reported. The kind is "manual" when the user edited the event.
    /// </summary>
    public static FileMatchSuggestion? FromAcceptedPendingImport(
        PendingImport import,
        string sourceFilePath,
        Event evt,
        string? carriedSportarrId = null)
    {
        // The release name can carry the id when the file name does not.
        if (CarriesIdToken(import.Title))
            return null;
        return FromImport(sourceFilePath, evt, SuggestionBeforeUserChoice(import), createNew: false,
            carriedSportarrId);
    }

    /// <summary>True when the name holds a Sportarr event or league id.</summary>
    public static bool CarriesIdToken(string? name) =>
        SportarrIdToken.ExtractEventId(name) != null || SportarrIdToken.ExtractLeagueId(name) != null;
}
