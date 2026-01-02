using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Service for calculating match scores between releases and events.
/// Used by both ReleaseCacheService (cached releases) and IndexerSearchService (live searches).
///
/// Scoring system (0-100):
/// - Year match: 15 points (required - 0 if mismatch)
/// - Sport prefix match: 15 points (required - 0 if mismatch)
/// - Round number match: +25 points (motorsport)
/// - Location match: 0-25 points (motorsport)
/// - Team match: 0-30 points (team sports)
/// - Date match: 0-20 points (team sports)
/// - Fighting event match: 0-30 points (UFC/boxing)
/// </summary>
public class ReleaseMatchScorer
{
    // Minimum match score threshold for a release to be considered a match
    // Releases below this score are filtered out from results
    public const int MinimumMatchScore = 30;

    // Minimum match score for auto-grab (higher threshold for automatic downloads)
    public const int AutoGrabMatchScore = 50;

    /// <summary>
    /// Calculate match score for a release against an event.
    /// Returns 0-100, higher is better.
    /// </summary>
    public int CalculateMatchScore(string releaseTitle, Event evt)
    {
        var parsed = ParseReleaseTitle(releaseTitle);
        return CalculateMatchScoreInternal(releaseTitle, parsed, evt);
    }

    /// <summary>
    /// Calculate match score with pre-parsed release metadata (for cached releases).
    /// </summary>
    public int CalculateMatchScore(string releaseTitle, int? year, int? month, int? day,
        int? roundNumber, string? sportPrefix, Event evt)
    {
        var parsed = new ParsedRelease
        {
            Year = year,
            Month = month,
            Day = day,
            RoundNumber = roundNumber,
            SportPrefix = sportPrefix
        };
        return CalculateMatchScoreInternal(releaseTitle, parsed, evt);
    }

    private int CalculateMatchScoreInternal(string releaseTitle, ParsedRelease parsed, Event evt)
    {
        var score = 0;
        var eventSportPrefix = GetSportPrefix(evt.League?.Name, evt.Sport);

        // === REQUIRED CRITERIA (score 0 if these don't match) ===

        // Year must match - this is required
        if (parsed.Year.HasValue && parsed.Year != evt.EventDate.Year)
            return 0;

        // Sport prefix is REQUIRED for known sports events
        // If the event has a sport prefix (NFL, NBA, etc.), the release MUST also have that prefix
        // This prevents TV shows like "The.Truth.2025" from matching NFL games
        if (!string.IsNullOrEmpty(eventSportPrefix))
        {
            // Release MUST have a sport prefix that matches
            if (string.IsNullOrEmpty(parsed.SportPrefix))
                return 0; // No sport prefix = not a sports release = doesn't match

            if (!parsed.SportPrefix.Equals(eventSportPrefix, StringComparison.OrdinalIgnoreCase))
                return 0; // Different sport = doesn't match
        }

        // === SCORING CRITERIA ===

        // Base score for matching year (if year info exists)
        if (parsed.Year.HasValue && parsed.Year == evt.EventDate.Year)
            score += 15;

        // Sport prefix match bonus
        if (!string.IsNullOrEmpty(parsed.SportPrefix) && !string.IsNullOrEmpty(eventSportPrefix) &&
            parsed.SportPrefix.Equals(eventSportPrefix, StringComparison.OrdinalIgnoreCase))
            score += 15;

        // Round number match (for motorsport)
        if (IsRoundBasedSport(eventSportPrefix) && !string.IsNullOrEmpty(evt.Round))
        {
            var eventRound = ExtractRoundNumber(evt.Round);
            if (eventRound.HasValue && parsed.RoundNumber.HasValue)
            {
                if (parsed.RoundNumber == eventRound)
                    score += 25; // Strong match
                else
                    score -= 10; // Wrong round is a significant penalty
            }
        }

        // Location matching (for motorsport)
        if (IsMotorsport(eventSportPrefix))
        {
            var locationScore = GetLocationMatchScore(releaseTitle, evt.Title);
            score += locationScore; // 0-25 points
        }

        // Team name matching (for team sports)
        if (IsTeamSport(eventSportPrefix))
        {
            var teamScore = GetTeamMatchScore(releaseTitle, evt);
            score += teamScore; // 0-30 points
        }

        // Date matching (for team sports with specific dates)
        if (IsDateBasedSport(eventSportPrefix))
        {
            var dateScore = GetDateMatchScore(parsed, evt);
            score += dateScore; // 0-20 points
        }

        // Fighting event matching (UFC number, fighters)
        if (IsFightingSport(eventSportPrefix))
        {
            var fightScore = GetFightingEventMatchScore(releaseTitle, evt.Title);
            score += fightScore; // 0-30 points
        }

        // Ensure score is within bounds
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Parse a release title to extract structured metadata.
    /// </summary>
    public ParsedRelease ParseReleaseTitle(string title)
    {
        var parsed = new ParsedRelease();

        // Extract year (4 digits, 2020+)
        var yearMatch = Regex.Match(title, @"\b(20[2-9]\d)\b");
        if (yearMatch.Success)
            parsed.Year = int.Parse(yearMatch.Groups[1].Value);

        // Extract round/week number
        var roundMatch = Regex.Match(title, @"(?:Round|R|Week|W)\.?(\d{1,2})\b", RegexOptions.IgnoreCase);
        if (roundMatch.Success)
            parsed.RoundNumber = int.Parse(roundMatch.Groups[1].Value);

        // Extract date (YYYY.MM.DD or YYYY-MM-DD)
        var dateMatch = Regex.Match(title, @"\b(20[2-9]\d)[.\-](\d{2})[.\-](\d{2})\b");
        if (dateMatch.Success)
        {
            parsed.Year = int.Parse(dateMatch.Groups[1].Value);
            parsed.Month = int.Parse(dateMatch.Groups[2].Value);
            parsed.Day = int.Parse(dateMatch.Groups[3].Value);
        }

        // Detect sport prefix
        parsed.SportPrefix = DetectSportPrefix(title);

        return parsed;
    }

    /// <summary>
    /// Detect the sport/league prefix from a title.
    /// </summary>
    public string? DetectSportPrefix(string title)
    {
        var normalized = title.ToUpperInvariant();

        // Common motorsport prefixes
        if (normalized.Contains("FORMULA1") || normalized.Contains("FORMULA.1") || normalized.Contains("F1."))
            return "Formula1";
        if (normalized.Contains("MOTOGP") || normalized.Contains("MOTO.GP"))
            return "MotoGP";
        if (normalized.Contains("INDYCAR"))
            return "IndyCar";
        if (normalized.Contains("NASCAR"))
            return "NASCAR";
        if (normalized.Contains("WEC") || normalized.Contains("WORLD.ENDURANCE"))
            return "WEC";

        // Fighting sports
        if (normalized.Contains("UFC"))
            return "UFC";
        if (normalized.Contains("BELLATOR"))
            return "Bellator";
        if (normalized.Contains("PFL"))
            return "PFL";
        if (normalized.Contains("BOXING") || normalized.Contains("DAZN"))
            return "Boxing";
        if (normalized.Contains("WWE"))
            return "WWE";

        // Team sports
        if (normalized.Contains("NFL") && !normalized.Contains("UEFA"))
            return "NFL";
        if (normalized.Contains("NBA"))
            return "NBA";
        if (normalized.Contains("NHL"))
            return "NHL";
        if (normalized.Contains("MLB"))
            return "MLB";
        if (normalized.Contains("MLS"))
            return "MLS";
        if (normalized.Contains("EPL") || normalized.Contains("PREMIER.LEAGUE"))
            return "EPL";
        if (normalized.Contains("CHAMPIONS.LEAGUE") || normalized.Contains("UCL"))
            return "UCL";
        if (normalized.Contains("LA.LIGA") || normalized.Contains("LALIGA"))
            return "LaLiga";

        return null;
    }

    /// <summary>
    /// Get the sport prefix for an event.
    /// </summary>
    public string? GetSportPrefix(string? leagueName, string? sport)
    {
        if (!string.IsNullOrEmpty(leagueName))
        {
            var upper = leagueName.ToUpperInvariant();
            if (upper.Contains("FORMULA 1") || upper.Contains("F1"))
                return "Formula1";
            if (upper.Contains("UFC"))
                return "UFC";
            if (upper.Contains("NFL"))
                return "NFL";
            if (upper.Contains("NBA"))
                return "NBA";
            if (upper.Contains("NHL"))
                return "NHL";
            if (upper.Contains("MLB"))
                return "MLB";
            if (upper.Contains("PREMIER LEAGUE") || upper.Contains("EPL"))
                return "EPL";
            if (upper.Contains("MLS"))
                return "MLS";
            // Add more mappings as needed
        }

        return DetectSportPrefix(sport ?? "");
    }

    #region Scoring Helper Methods

    /// <summary>
    /// Get location match score (0-25 points).
    /// </summary>
    private int GetLocationMatchScore(string releaseTitle, string eventTitle)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var locationTerms = SearchNormalizationService.ExtractKeyTerms(eventTitle);
        var matchedTerms = 0;
        var totalTerms = 0;

        foreach (var term in locationTerms)
        {
            if (IsCommonWord(term) || term.Length <= 2)
                continue;

            totalTerms++;

            // Direct match
            if (normalizedRelease.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                matchedTerms++;
                continue;
            }

            // Check aliases
            var variations = SearchNormalizationService.GenerateSearchVariations(term);
            foreach (var variation in variations)
            {
                var normalizedVariation = NormalizeTitle(variation);
                if (normalizedRelease.Contains(normalizedVariation, StringComparison.OrdinalIgnoreCase))
                {
                    matchedTerms++;
                    break;
                }
            }
        }

        if (totalTerms == 0) return 10; // No location terms to match, give partial credit

        // Scale: 0-25 points based on percentage of terms matched
        var percentage = (double)matchedTerms / totalTerms;
        return (int)(percentage * 25);
    }

    /// <summary>
    /// Get team match score (-50 to 40 points).
    /// Returns negative score if neither team matches (to reject wrong games).
    /// </summary>
    private int GetTeamMatchScore(string releaseTitle, Event evt)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var homeScore = 0;
        var awayScore = 0;
        var homeHasMatch = false;
        var awayHasMatch = false;

        // Check home team (20 points max)
        if (!string.IsNullOrEmpty(evt.HomeTeamName))
        {
            var homeWords = NormalizeTitle(evt.HomeTeamName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !IsCommonWord(w))
                .ToList();

            var homeMatches = homeWords.Count(w => normalizedRelease.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (homeWords.Count > 0 && homeMatches > 0)
            {
                homeHasMatch = true;
                homeScore = (int)(20.0 * homeMatches / homeWords.Count);
            }
        }

        // Check away team (20 points max)
        if (!string.IsNullOrEmpty(evt.AwayTeamName))
        {
            var awayWords = NormalizeTitle(evt.AwayTeamName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !IsCommonWord(w))
                .ToList();

            var awayMatches = awayWords.Count(w => normalizedRelease.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (awayWords.Count > 0 && awayMatches > 0)
            {
                awayHasMatch = true;
                awayScore = (int)(20.0 * awayMatches / awayWords.Count);
            }
        }

        // CRITICAL: For team sports events with team names, at least ONE team must match
        // Otherwise it's a different game entirely (e.g., searching Chiefs vs Broncos but finding Patriots vs Jets)
        var hasTeamInfo = !string.IsNullOrEmpty(evt.HomeTeamName) || !string.IsNullOrEmpty(evt.AwayTeamName);
        if (hasTeamInfo && !homeHasMatch && !awayHasMatch)
        {
            // Check if this even looks like a game release (has "vs", "@", "at", or team matchup indicators)
            var looksLikeGame = normalizedRelease.Contains(" vs ") ||
                               normalizedRelease.Contains(".vs.") ||
                               normalizedRelease.Contains(" at ") ||
                               normalizedRelease.Contains(".at.") ||
                               normalizedRelease.Contains(" @ ");

            if (!looksLikeGame)
            {
                // This is likely a documentary, highlight show, etc. (e.g., "NFL.Turning.Point", "NFL.PrimeTime")
                return -100; // Very strong penalty - not even a game
            }

            return -50; // Strong penalty - this is a different game
        }

        return homeScore + awayScore;
    }

    /// <summary>
    /// Get date match score (0-20 points).
    /// </summary>
    private int GetDateMatchScore(ParsedRelease parsed, Event evt)
    {
        var score = 0;

        // Month match (10 points)
        if (parsed.Month.HasValue && parsed.Month == evt.EventDate.Month)
            score += 10;

        // Day match (10 points)
        if (parsed.Day.HasValue && parsed.Day == evt.EventDate.Day)
            score += 10;

        // If release has date info but it doesn't match, small penalty
        if (parsed.Month.HasValue && parsed.Day.HasValue)
        {
            if (parsed.Month != evt.EventDate.Month || parsed.Day != evt.EventDate.Day)
                score -= 5;
        }

        return Math.Max(0, score);
    }

    /// <summary>
    /// Get fighting event match score (0-30 points).
    /// </summary>
    private int GetFightingEventMatchScore(string releaseTitle, string eventTitle)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);
        var score = 0;

        // Check for UFC/event number match (20 points)
        var eventNumberMatch = Regex.Match(normalizedEvent, @"(?:ufc|bellator|pfl)\s*(?:fight\s*night\s*)?(\d+)", RegexOptions.IgnoreCase);
        if (eventNumberMatch.Success)
        {
            var eventNumber = eventNumberMatch.Groups[1].Value;
            var releaseNumberMatch = Regex.Match(normalizedRelease, @"(?:ufc|bellator|pfl)\s*(?:fight\s*night\s*)?(\d+)", RegexOptions.IgnoreCase);
            if (releaseNumberMatch.Success && releaseNumberMatch.Groups[1].Value == eventNumber)
                score += 20;
            else if (releaseNumberMatch.Success)
                score -= 10; // Wrong event number is a penalty
        }

        // Key term matching (10 points max)
        var eventWords = normalizedEvent.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !IsCommonWord(w))
            .ToList();

        if (eventWords.Count > 0)
        {
            var matchCount = eventWords.Count(w => normalizedRelease.Contains(w, StringComparison.OrdinalIgnoreCase));
            score += (int)(10.0 * matchCount / eventWords.Count);
        }

        return Math.Max(0, score);
    }

    /// <summary>
    /// Extract round number from round string (e.g., "Round 19" -> 19).
    /// </summary>
    private int? ExtractRoundNumber(string round)
    {
        var match = Regex.Match(round, @"(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var roundNum))
            return roundNum;
        return null;
    }

    #endregion

    #region Sport Type Helpers

    private bool IsRoundBasedSport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "Formula1" or "MotoGP" or "IndyCar" or "NASCAR" or "WEC";
    }

    private bool IsDateBasedSport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "NFL" or "NBA" or "NHL" or "MLB" or "MLS" or "EPL" or "UCL" or "LaLiga";
    }

    private bool IsMotorsport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "Formula1" or "MotoGP" or "IndyCar" or "NASCAR" or "WEC";
    }

    private bool IsTeamSport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "NFL" or "NBA" or "NHL" or "MLB" or "MLS" or "EPL" or "UCL" or "LaLiga";
    }

    private bool IsFightingSport(string? sportPrefix)
    {
        if (string.IsNullOrEmpty(sportPrefix)) return false;
        return sportPrefix is "UFC" or "Bellator" or "PFL" or "Boxing" or "WWE";
    }

    #endregion

    #region Utility Methods

    private string NormalizeTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return "";

        // Remove diacritics
        var normalized = SearchNormalizationService.RemoveDiacritics(title);

        // Replace common separators with spaces
        normalized = Regex.Replace(normalized, @"[\.\-_]", " ");

        // Collapse multiple spaces
        normalized = Regex.Replace(normalized, @"\s+", " ");

        return normalized.Trim().ToLowerInvariant();
    }

    private bool IsCommonWord(string word)
    {
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "of", "at", "in", "on", "for", "to", "and", "or",
            "vs", "versus", "grand", "prix", "race", "match", "game", "event"
        };
        return commonWords.Contains(word);
    }

    #endregion

    /// <summary>
    /// Parsed release metadata from title.
    /// </summary>
    public class ParsedRelease
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public int? RoundNumber { get; set; }
        public string? SportPrefix { get; set; }
    }
}
