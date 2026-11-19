using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

internal enum MotoGpGrandPrixMatch
{
    NotApplicable,
    Location,
    RoundOrDate,
    Conflict,
    Insufficient
}

internal static class MotoGpGrandPrixIdentity
{
    private sealed class Place
    {
        public Place(string name, Regex names, string? demonym = null)
        {
            Name = name;
            Names = names;
            if (demonym == null)
                return;

            var escaped = Regex.Escape(demonym);
            EventDemonym = new Regex($@"\b{escaped}\b", RegexOptions.Compiled);
            ReleaseDemonym = new Regex(
                $@"\b{escaped}\s+(?:gp|grand prix)\b|\b(?:gp|grand prix)\s+(?:of\s+)?{escaped}\b",
                RegexOptions.Compiled);
        }

        public string Name { get; }
        public Regex Names { get; }
        public Regex? EventDemonym { get; }
        public Regex? ReleaseDemonym { get; }
    }

    // "Italia" also names a broadcaster, as in "Sky Sport Italia". Such a
    // name counts only after a race name, as in "Gran Premio d Italia".
    private static string AfterRaceName(string name) =>
        @"(?:gp|gran premio|gran premi|grand prix|grosser preis|grande premio)(?:\s+(?:de|do|da|di|del|della|du|von|of|d))?\s+" + name;

    private static readonly Place[] Places =
    [
        new("San Marino", Names(@"san marino|saint marin|misano")),
        new("Aragon", Names(@"aragon|motorland")),
        new("Catalonia", Names(@"catalonia|catalunya|catalogne|katalonien|cataluna|barcelona|montmelo"), "catalan"),
        new("Austria", Names(@"austria|autriche|oesterreich|osterreich|spielberg|red bull ring"), "austrian"),
        new("Great Britain", Names(@"great britain|britain|united kingdom|grande bretagne|angleterre|grossbritannien|gran bretagna|gran bretana|gra bretanha|reino unido|silverstone"), "british"),
        new("Germany", Names(@"germany|allemagne|germania|alemania|alemanha|sachsenring|" + AfterRaceName("deutschland")), "german"),
        new("Netherlands", Names(@"netherlands|pays bas|niederlande|olanda|paesi bassi|paises bajos|paises baixos|holanda|assen"), "dutch"),
        new("Czechia", Names(@"czechia|czech republic|tchequie|tschechien|chequia|republica checa|brno"), "czech"),
        new("Hungary", Names(@"hungary|hongrie|ungarn|ungheria|hungria|balaton park"), "hungarian"),
        new("France", Names(@"france|frankreich|francia|franca|le mans"), "french"),
        new("Valencia", Names(@"valencia|valence|valenciana|ricardo tormo")),
        new("Spain", Names(@"spain|espagne|spanien|spagna|espanha|jerez|" + AfterRaceName("espana")), "spanish"),
        new("Americas", Names(@"americas|ameriques|united states|estados unidos|cota|austin|circuit of the americas"), "american"),
        new("Brazil", Names(@"brazil|bresil|brasilien|brasile|goiania|" + AfterRaceName("brasil")), "brazilian"),
        new("Italy", Names(@"italy|italie|italien|mugello|" + AfterRaceName("italia")), "italian"),
        new("Thailand", Names(@"thailand|thailande|tailandia|buriram"), "thai"),
        new("Portugal", Names(@"portugal|portimao"), "portuguese"),
        new("Qatar", Names(@"qatar|katar|catar|lusail|losail"), "qatari"),
        new("Japan", Names(@"japan|japon|japao|giappone|motegi"), "japanese"),
        new("Australia", Names(@"australia|australie|australien|phillip island"), "australian"),
        new("Malaysia", Names(@"malaysia|malaisie|malasia|sepang"), "malaysian"),
        new("Indonesia", Names(@"indonesia|indonesie|indonesien|mandalika"), "indonesian"),
        new("Argentina", Names(@"argentina|argentine|argentinien|termas de rio hondo"), "argentinian")
    ];

    private static readonly Regex Separators = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex UsaGrandPrix = new(
        @"\busa\s+(?:gp|grand prix)\b|\b(?:gp|grand prix)\s+(?:of\s+)?usa\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static MotoGpGrandPrixMatch Evaluate(
        string releaseTitle, Event evt, int? releaseRound, DateTime? releaseDate)
    {
        if (evt.League?.Name.Contains("MotoGP", StringComparison.OrdinalIgnoreCase) != true)
            return MotoGpGrandPrixMatch.NotApplicable;

        var eventSession = EventPartDetector.DetectMotorsportSessionIdentity(
            evt.Title, evt.League?.Name, releaseTitle: false);
        if (eventSession?.Contains("Test", StringComparison.OrdinalIgnoreCase) == true)
            return MotoGpGrandPrixMatch.NotApplicable;

        var expected = FindPlace(evt.Title, releaseTitle: false) ??
                       FindPlace(evt.Venue, releaseTitle: false) ??
                       FindPlace(evt.Location, releaseTitle: false);
        if (expected == null)
        {
            if (eventSession == null)
                return MotoGpGrandPrixMatch.NotApplicable;

            var knownReleasePlace = FindPlace(releaseTitle, releaseTitle: true);
            if (knownReleasePlace != null)
                return MotoGpGrandPrixMatch.Conflict;

            if (HasMatchingRoundOrDate(releaseRound, releaseDate, evt))
                return MotoGpGrandPrixMatch.RoundOrDate;

            var placeTerms = SearchNormalizationService.ExtractKeyTerms(evt.Title)
                .Where(term => term != "motogp" && !int.TryParse(term, out _))
                .ToArray();
            if (placeTerms.Length == 0)
                return MotoGpGrandPrixMatch.Insufficient;

            var placePattern = $@"\b{string.Join(@"\s+", placeTerms.Select(Regex.Escape))}\b";
            return Regex.IsMatch(Normalize(releaseTitle), placePattern)
                ? MotoGpGrandPrixMatch.Location
                : MotoGpGrandPrixMatch.Insufficient;
        }

        var actual = FindPlace(releaseTitle, releaseTitle: true);
        if (actual != null)
            return actual == expected ? MotoGpGrandPrixMatch.Location : MotoGpGrandPrixMatch.Conflict;

        if (HasMatchingRoundOrDate(releaseRound, releaseDate, evt))
            return MotoGpGrandPrixMatch.RoundOrDate;

        return MotoGpGrandPrixMatch.Insufficient;
    }

    private static bool HasMatchingRoundOrDate(int? releaseRound, DateTime? releaseDate, Event evt)
    {
        if (releaseRound.HasValue && int.TryParse(evt.Round, out var eventRound) &&
            releaseRound.Value == eventRound)
            return true;

        var eventDate = (evt.BroadcastDate ?? evt.EventDate).Date;
        return releaseDate.HasValue && eventDate != default &&
               Math.Abs((releaseDate.Value.Date - eventDate).TotalDays) <= 1;
    }

    private static string Normalize(string title) => Separators.Replace(
        SearchNormalizationService.RemoveDiacritics(title).ToLowerInvariant(), " ").Trim();

    private static Place? FindPlace(string? title, bool releaseTitle)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var normalized = Normalize(title);
        foreach (var place in Places)
        {
            if (place.Names.IsMatch(normalized))
                return place;

            if (releaseTitle && place.Name == "Americas" && UsaGrandPrix.IsMatch(normalized))
                return place;

            if ((releaseTitle ? place.ReleaseDemonym : place.EventDemonym)?.IsMatch(normalized) == true)
                return place;
        }

        return null;
    }

    private static Regex Names(string aliases) => new(
        $@"\b(?:{aliases})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
