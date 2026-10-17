using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

public sealed class NascarVenueMatchContext
{
    private static readonly Regex Separators = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly HashSet<string> VenueWords = new(StringComparer.Ordinal)
    {
        "motor", "motors", "international", "speedway", "superspeedway", "raceway", "circuit",
        "autodrome", "track", "park"
    };
    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal)
    {
        "of", "the", "at"
    };

    private readonly Dictionary<string, HashSet<string>> _venueKeysByAlias = new(StringComparer.Ordinal);

    private NascarVenueMatchContext(IEnumerable<string?> venues)
    {
        foreach (var venue in venues.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalizedVenue = Normalize(venue!);
            var key = VenueKey(normalizedVenue);
            if (string.IsNullOrEmpty(key))
                continue;

            foreach (var alias in VenueAliases(normalizedVenue, key))
            {
                if (!_venueKeysByAlias.TryGetValue(alias, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.Ordinal);
                    _venueKeysByAlias.Add(alias, keys);
                }
                keys.Add(key);
            }
        }
    }

    public static NascarVenueMatchContext FromVenues(params string[] venues) => new(venues);

    public static async Task<NascarVenueMatchContext?> LoadAsync(
        SportarrDbContext db, Event evt, CancellationToken cancellationToken = default)
    {
        if (evt.League?.Name.StartsWith("NASCAR Cup", StringComparison.OrdinalIgnoreCase) != true)
            return null;

        if (!evt.LeagueId.HasValue || string.IsNullOrWhiteSpace(evt.Season))
            return new NascarVenueMatchContext(new[] { evt.Venue });

        var venues = await db.Events.AsNoTracking()
            .Where(candidate => candidate.LeagueId == evt.LeagueId && candidate.Season == evt.Season)
            .Select(candidate => candidate.Venue)
            .ToListAsync(cancellationToken);
        venues.Add(evt.Venue);
        return new NascarVenueMatchContext(venues);
    }

    internal NascarVenueMatch Evaluate(string releaseTitle, string? expectedVenue)
    {
        if (string.IsNullOrWhiteSpace(expectedVenue))
            return NascarVenueMatch.Unknown;

        var expectedKey = VenueKey(Normalize(expectedVenue));
        if (string.IsNullOrEmpty(expectedKey))
            return NascarVenueMatch.Unknown;

        var normalizedRelease = $" {Normalize(releaseTitle)} ";
        var matched = false;
        foreach (var (alias, keys) in _venueKeysByAlias)
        {
            if (!normalizedRelease.Contains($" {alias} ", StringComparison.Ordinal))
                continue;

            if (keys.Count != 1)
                continue;

            if (!keys.Contains(expectedKey))
                return NascarVenueMatch.Conflict;

            matched = true;
        }

        return matched ? NascarVenueMatch.Match : NascarVenueMatch.Unknown;
    }

    private static IEnumerable<string> VenueAliases(string venue, string key)
    {
        yield return venue;
        yield return key;

        if (venue.Contains("circuit of the americas", StringComparison.Ordinal))
        {
            yield return "cota";
            yield return "austin";
        }

        if (venue.Contains("indianapolis motor speedway", StringComparison.Ordinal))
            yield return "indy";

        if (venue.StartsWith("homestead miami ", StringComparison.Ordinal))
            yield return "miami";
    }

    private static string VenueKey(string normalizedVenue)
    {
        var words = normalizedVenue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var marker = Array.FindIndex(words, VenueWords.Contains);
        var core = marker > 0 ? words.Take(marker) : words.AsEnumerable();
        var terms = core.Where(word => !VenueWords.Contains(word) && !FillerWords.Contains(word))
            .ToArray();
        if (terms.Length == 0)
            return string.Empty;

        if (terms.Length > 1 && terms[0] == "las")
            terms = terms[1..];

        return string.Join(' ', terms);
    }

    private static string Normalize(string value) =>
        Separators.Replace(SearchNormalizationService.RemoveDiacritics(value).ToLowerInvariant(), " ").Trim();
}

internal enum NascarVenueMatch
{
    Unknown,
    Match,
    Conflict
}
