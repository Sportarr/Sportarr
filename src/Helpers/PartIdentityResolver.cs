using System.Text.RegularExpressions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Helpers;

public enum PartIdentityKind
{
    NotApplicable,
    ContextUnavailable,
    ExplicitPart,
    ExplicitFullEvent,
    UnsupportedExplicitPart,
    InferredPart,
    Unlabelled,
    CompleteEventLabel,
    UnsupportedLabel,
    Ambiguous
}

public sealed record PartIdentityResolution(PartIdentityKind Kind, EventPartInfo? Part = null);

public static class PartIdentityResolver
{
    private enum LabelScope { Any, Wwe, AewOrRoh, One }

    private sealed record Label(string Text, string[] Names, LabelScope Scope = LabelScope.Any)
    {
        public string[] Words { get; } = WordsIn(Text);
    }

    private sealed record LabelMatch(Label Label, int Start, int End);

    private static readonly Label[] Labels =
    {
        new("early prelims", new[] { "Early Prelims" }),
        new("early prelim", new[] { "Early Prelims" }),
        new("early preliminary", new[] { "Early Prelims" }),
        new("prelims", new[] { "Prelims" }),
        new("prelim", new[] { "Prelims" }),
        new("preliminary", new[] { "Prelims" }),
        new("main card", new[] { "Main Card", "Main Show" }),
        new("main show", new[] { "Main Show", "Main Card" }),
        new("post show", new[] { "Post Show" }),
        new("countdown", new[] { "Countdown" }),
        new("pre show", new[] { "Countdown" }),
        new("zero hour", new[] { "Countdown" }, LabelScope.AewOrRoh),
        new("buy in", new[] { "Countdown" }, LabelScope.AewOrRoh),
        new("kick off", new[] { "Countdown" }, LabelScope.Wwe),
        new("kickoff", new[] { "Countdown" }, LabelScope.Wwe),
        new("lead card", new[] { "Prelims" }, LabelScope.One)
    };

    private static readonly string[][] CompleteLabels = new[]
    {
        "full event", "complete event", "full card", "complete card", "full show", "complete show",
        "fullevent", "completeevent", "fullcard", "completecard"
    }.Select(WordsIn).ToArray();

    public static bool HasNamedPartLabel(string? sourceFileName)
    {
        var words = WordsIn(Basename(sourceFileName));
        return Labels.Any(label => FindStarts(words, label.Words).Any());
    }

    public static PartIdentityResolution Resolve(
        string? requestedPart,
        string? releaseTitle,
        string? sourceFileName,
        string? sport,
        string? eventTitle,
        string? leagueName,
        bool enableMultiPartEpisodes,
        bool isPack = false)
    {
        if (!enableMultiPartEpisodes || !EventPartDetector.IsFightingSport(sport ?? string.Empty))
            return new(PartIdentityKind.NotApplicable);

        if (string.IsNullOrWhiteSpace(eventTitle) || string.IsNullOrWhiteSpace(leagueName))
            return new(PartIdentityKind.ContextUnavailable);

        if (!EventPartDetector.EventUsesMultiPart(eventTitle, sport!, leagueName))
            return new(PartIdentityKind.NotApplicable);

        var definitions = EventPartDetector.GetSegmentDefinitions(sport!, eventTitle, leagueName)
            .Where(d => d.PartNumber > 0)
            .ToArray();
        if (definitions.Length == 0)
            return new(PartIdentityKind.NotApplicable);

        if (!string.IsNullOrWhiteSpace(requestedPart))
        {
            var normalized = string.Join(' ', WordsIn(requestedPart));
            if (normalized == "full event")
                return new(PartIdentityKind.ExplicitFullEvent);

            var requested = definitions.FirstOrDefault(d =>
                string.Join(' ', WordsIn(d.Name)) == normalized);
            return requested == null
                ? new(PartIdentityKind.UnsupportedExplicitPart)
                : new(PartIdentityKind.ExplicitPart, ToPartInfo(requested));
        }

        var fromFile = Analyze(Basename(sourceFileName), definitions, leagueName);
        if (isPack)
            return fromFile;

        var fromRelease = Analyze(releaseTitle, definitions, leagueName);
        if (fromRelease.Kind == PartIdentityKind.Unlabelled)
            return fromFile;
        if (fromRelease.Kind != PartIdentityKind.InferredPart)
            return fromRelease;

        if (fromFile.Kind == PartIdentityKind.Unlabelled)
            return fromRelease;
        if (fromFile.Kind == PartIdentityKind.InferredPart &&
            fromFile.Part!.SegmentName == fromRelease.Part!.SegmentName)
            return fromRelease;

        return new(PartIdentityKind.Ambiguous);
    }

    private static PartIdentityResolution Analyze(
        string? value, SegmentDefinition[] definitions, string leagueName)
    {
        var words = WordsIn(value);
        if (CompleteLabels.Any(label => FindStarts(words, label).Any()))
            return new(PartIdentityKind.CompleteEventLabel);

        var matches = Labels.SelectMany(label => FindStarts(words, label.Words)
            .Select(start => new LabelMatch(label, start, start + label.Words.Length)))
            .ToArray();

        // A longer label owns its words, even if this event does not support it.
        var independent = matches.Where(match => !matches.Any(other =>
            other.Start <= match.Start && other.End >= match.End &&
            other.End - other.Start > match.End - match.Start)).ToArray();
        if (independent.Length == 0)
            return new(PartIdentityKind.Unlabelled);

        var resolved = new List<SegmentDefinition>();
        var unsupported = false;
        foreach (var match in independent)
        {
            var definition = Allows(match.Label.Scope, leagueName)
                ? match.Label.Names.Select(name => definitions.FirstOrDefault(d => d.Name == name))
                    .FirstOrDefault(d => d != null)
                : null;
            if (definition == null)
                unsupported = true;
            else
                resolved.Add(definition);
        }

        var distinct = resolved.DistinctBy(d => d.PartNumber).ToArray();
        if (distinct.Length > 1 || (unsupported && distinct.Length > 0))
            return new(PartIdentityKind.Ambiguous);
        if (unsupported)
            return new(PartIdentityKind.UnsupportedLabel);
        return new(PartIdentityKind.InferredPart, ToPartInfo(distinct.Single()));
    }

    private static bool Allows(LabelScope scope, string leagueName)
    {
        var promotion = EventPartDetector.DetectWrestlingPromotion(leagueName);
        return scope switch
        {
            LabelScope.Any => true,
            LabelScope.Wwe => promotion == EventPartDetector.WrestlingPromotion.Wwe,
            LabelScope.AewOrRoh => promotion is EventPartDetector.WrestlingPromotion.Aew
                or EventPartDetector.WrestlingPromotion.Roh,
            LabelScope.One => leagueName.Equals("ONE", StringComparison.OrdinalIgnoreCase)
                || leagueName.Contains("ONE Championship", StringComparison.OrdinalIgnoreCase)
                || leagueName.Contains("ONE FC", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static IEnumerable<int> FindStarts(string[] words, string[] label)
    {
        for (var start = 0; start <= words.Length - label.Length; start++)
        {
            if (label.Select((word, offset) => word == words[start + offset]).All(equal => equal))
                yield return start;
        }
    }

    private static string[] WordsIn(string? value) => Regex.Matches(value ?? string.Empty, @"[\p{L}\p{N}]+")
        .Select(match => match.Value.ToLowerInvariant()).ToArray();

    private static string? Basename(string? filename)
    {
        if (filename == null)
            return null;
        return filename.Replace('\\', '/').Split('/').Last();
    }

    private static EventPartInfo ToPartInfo(SegmentDefinition definition) => new()
    {
        SegmentName = definition.Name,
        PartNumber = definition.PartNumber,
        PartSuffix = $"pt{definition.PartNumber}",
        SportCategory = "Fighting"
    };
}
