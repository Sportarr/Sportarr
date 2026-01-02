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
        // CRITICAL: Location matching can return negative scores for wrong locations
        // This prevents "Qatar Grand Prix" from matching "Brazil Grand Prix" releases
        if (IsMotorsport(eventSportPrefix))
        {
            var locationScore = GetLocationMatchScore(releaseTitle, evt.Title);
            if (locationScore < 0)
                return 0; // Wrong location - reject immediately
            score += locationScore; // 0-25 points for matching locations
        }

        // Team name matching (for team sports)
        // CRITICAL: Team matching can return negative scores for wrong games/non-games
        // These negative scores should cause immediate rejection (return 0)
        if (IsTeamSport(eventSportPrefix))
        {
            var teamScore = GetTeamMatchScore(releaseTitle, evt);
            if (teamScore < 0)
                return 0; // Wrong game or not a game at all - reject immediately
            score += teamScore; // 0-40 points for matching teams
        }

        // Date matching (for team sports with specific dates)
        if (IsDateBasedSport(eventSportPrefix))
        {
            var dateScore = GetDateMatchScore(parsed, evt);
            score += dateScore; // 0-20 points
        }

        // Fighting event matching (UFC number, fighters)
        // CRITICAL: Fighting matching can return negative scores for wrong events
        if (IsFightingSport(eventSportPrefix))
        {
            var fightScore = GetFightingEventMatchScore(releaseTitle, evt.Title);
            if (fightScore < 0)
                return 0; // Wrong event - reject immediately
            score += fightScore; // 0-40 points for matching events
        }

        // Ensure score is within bounds (0-100)
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
    /// Get location match score (-50 to 25 points).
    /// Returns NEGATIVE score if release contains a DIFFERENT known motorsport location.
    /// This prevents "Qatar Grand Prix" from matching "Brazil Grand Prix Sprint" releases.
    /// </summary>
    private int GetLocationMatchScore(string releaseTitle, string eventTitle)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);

        // CRITICAL: ALWAYS check for conflicting locations FIRST
        // Even if "Sprint" matches, "Brazil Sprint" should NOT match "Qatar Sprint"
        var differentLocationFound = CheckForDifferentLocation(normalizedRelease, normalizedEvent);
        if (differentLocationFound != null)
        {
            // Release has a different location - this is the wrong race
            return -50;
        }

        // Now check if the event location matches the release
        var locationTerms = SearchNormalizationService.ExtractKeyTerms(eventTitle);
        var matchedTerms = 0;
        var totalTerms = 0;

        foreach (var term in locationTerms)
        {
            if (IsCommonWord(term) || term.Length <= 2)
                continue;

            // Skip common motorsport terms that aren't location-specific
            if (IsMotorsportCommonTerm(term))
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

        // If we matched location terms, return positive score
        if (matchedTerms > 0)
        {
            var percentage = (double)matchedTerms / Math.Max(totalTerms, 1);
            return (int)(percentage * 25);
        }

        // No location terms to match, give partial credit
        if (totalTerms == 0) return 10;

        // Location not matched but no conflicting location found - neutral
        return 0;
    }

    /// <summary>
    /// Check if a term is a common motorsport term that shouldn't count for location matching.
    /// These terms appear in all races and don't indicate a specific location.
    /// </summary>
    private bool IsMotorsportCommonTerm(string term)
    {
        var commonTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "grand", "prix", "sprint", "race", "qualifying", "practice", "fp1", "fp2", "fp3",
            "shootout", "main", "pre", "post", "round", "season", "championship",
            "f1tv", "sky", "espn", "web", "dl", "hdtv", "webrip"
        };
        return commonTerms.Contains(term);
    }

    /// <summary>
    /// Check if a release contains a DIFFERENT known motorsport location than the event.
    /// Returns the conflicting location name if found, null otherwise.
    /// </summary>
    private string? CheckForDifferentLocation(string normalizedRelease, string normalizedEvent)
    {
        // Known motorsport locations and their variations
        // These are locations that appear in F1, MotoGP, and other motorsport releases
        var motorsportLocations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Qatar", new[] { "Lusail", "Qatari" } },
            { "Brazil", new[] { "Brazilian", "Interlagos", "Sao Paulo" } },
            { "Mexico", new[] { "Mexican", "Mexico City" } },
            { "China", new[] { "Chinese", "Shanghai" } },
            { "USA", new[] { "United States", "American", "COTA", "Austin", "Circuit of the Americas" } },
            { "Las Vegas", new[] { "Vegas" } },
            { "Miami", new[] { "Miami Gardens" } },
            { "Abu Dhabi", new[] { "AbuDhabi", "Yas Marina" } },
            { "Monaco", new[] { "Monte Carlo", "Monegasque" } },
            { "Austria", new[] { "Austrian", "Spielberg" } },
            { "Britain", new[] { "British", "Silverstone", "UK", "Great Britain" } },
            { "Italy", new[] { "Italian", "Monza", "Imola", "Mugello" } },
            { "Belgium", new[] { "Belgian", "Spa", "Spa-Francorchamps" } },
            { "Japan", new[] { "Japanese", "Suzuka" } },
            { "Singapore", new[] { "Singaporean", "Marina Bay" } },
            { "Australia", new[] { "Australian", "Melbourne" } },
            { "Canada", new[] { "Canadian", "Montreal" } },
            { "Azerbaijan", new[] { "Azerbaijani", "Baku" } },
            { "Saudi Arabia", new[] { "Saudi", "Jeddah" } },
            { "Netherlands", new[] { "Dutch", "Zandvoort" } },
            { "Hungary", new[] { "Hungarian", "Budapest", "Hungaroring" } },
            { "Spain", new[] { "Spanish", "Barcelona", "Catalunya" } },
            { "Bahrain", new[] { "Bahraini", "Sakhir" } },
            { "Emilia Romagna", new[] { "Emilia-Romagna", "San Marino" } },
        };

        // Find which location is in the EVENT (so we can exclude it from the wrong-location check)
        var eventLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (location, aliases) in motorsportLocations)
        {
            if (normalizedEvent.Contains(location, StringComparison.OrdinalIgnoreCase))
            {
                eventLocations.Add(location);
                continue;
            }
            foreach (var alias in aliases)
            {
                if (normalizedEvent.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    eventLocations.Add(location);
                    break;
                }
            }
        }

        // Now check if release contains a DIFFERENT location
        foreach (var (location, aliases) in motorsportLocations)
        {
            // Skip if this location is the event's location
            if (eventLocations.Contains(location))
                continue;

            // Check if this different location appears in the release
            if (normalizedRelease.Contains(location, StringComparison.OrdinalIgnoreCase))
            {
                return location;
            }

            foreach (var alias in aliases)
            {
                if (normalizedRelease.Contains(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{location} ({alias})";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get team match score (-100 to 40 points).
    /// Returns negative score if both teams don't match (to reject wrong games).
    /// CRITICAL: For "Team A vs Team B" events, BOTH teams must be present in the release.
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
            var (hasMatch, score) = CheckTeamMatch(normalizedRelease, evt.HomeTeamName);
            homeHasMatch = hasMatch;
            homeScore = score;
        }

        // Check away team (20 points max)
        if (!string.IsNullOrEmpty(evt.AwayTeamName))
        {
            var (hasMatch, score) = CheckTeamMatch(normalizedRelease, evt.AwayTeamName);
            awayHasMatch = hasMatch;
            awayScore = score;
        }

        // Check if this looks like a game release (has "vs", "@", "at", or team matchup indicators)
        var looksLikeGame = normalizedRelease.Contains(" vs ") ||
                           normalizedRelease.Contains(".vs.") ||
                           normalizedRelease.Contains(" at ") ||
                           normalizedRelease.Contains(".at.") ||
                           normalizedRelease.Contains(" @ ");

        // Determine if we have both teams in the event
        var hasBothTeams = !string.IsNullOrEmpty(evt.HomeTeamName) && !string.IsNullOrEmpty(evt.AwayTeamName);
        var hasAnyTeamInfo = !string.IsNullOrEmpty(evt.HomeTeamName) || !string.IsNullOrEmpty(evt.AwayTeamName);

        // CRITICAL: For "Team A vs Team B" events with BOTH teams defined, BOTH must match
        // This prevents "Chiefs vs Broncos" from matching "Texans vs Chiefs" (only one team matches)
        if (hasBothTeams)
        {
            if (!homeHasMatch && !awayHasMatch)
            {
                // Neither team matches at all
                if (!looksLikeGame)
                {
                    // Documentary, highlight show, etc. (e.g., "NFL.Turning.Point", "NFL.PrimeTime")
                    return -100;
                }
                return -50; // Different game entirely
            }
            else if (!homeHasMatch || !awayHasMatch)
            {
                // Only ONE team matches - this is a DIFFERENT game
                // e.g., searching "Chiefs vs Broncos" but found "Texans vs Chiefs"
                return -40; // Strong penalty - wrong matchup
            }
            // Both teams match - fall through to return combined score
        }
        else if (hasAnyTeamInfo && !homeHasMatch && !awayHasMatch)
        {
            // Only one team defined in event, but it doesn't match
            if (!looksLikeGame)
            {
                return -100; // Not even a game
            }
            return -50; // Different game
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
    /// Get fighting event match score (-50 to 40 points).
    /// Handles: UFC PPV (UFC 299), Fight Nights, Dana White's Contender Series (DWCS), etc.
    /// </summary>
    private int GetFightingEventMatchScore(string releaseTitle, string eventTitle)
    {
        var normalizedRelease = NormalizeTitle(releaseTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);
        var score = 0;
        var hasEventIdentifier = false;

        // === DANA WHITE'S CONTENDER SERIES (DWCS) - Season/Episode based ===
        // Event title: "Dana White's Contender Series S07E01" or "DWCS Season 7 Episode 1"
        // Release title: "UFC.Dana.Whites.Contender.Series.S07E01" or "DWCS.S07E01"
        var dwcsEventMatch = Regex.Match(normalizedEvent, @"(?:dana\s*white|dwcs|contender\s*series).*?(?:s(\d+)e(\d+)|season\s*(\d+).*?episode\s*(\d+))", RegexOptions.IgnoreCase);
        if (dwcsEventMatch.Success)
        {
            hasEventIdentifier = true;
            var eventSeason = dwcsEventMatch.Groups[1].Success ? dwcsEventMatch.Groups[1].Value : dwcsEventMatch.Groups[3].Value;
            var eventEpisode = dwcsEventMatch.Groups[2].Success ? dwcsEventMatch.Groups[2].Value : dwcsEventMatch.Groups[4].Value;

            // Check if release has matching season/episode
            var dwcsReleaseMatch = Regex.Match(normalizedRelease, @"(?:dana\s*white|dwcs|contender\s*series).*?s(\d+)e(\d+)", RegexOptions.IgnoreCase);
            if (dwcsReleaseMatch.Success)
            {
                var releaseSeason = dwcsReleaseMatch.Groups[1].Value;
                var releaseEpisode = dwcsReleaseMatch.Groups[2].Value;

                if (releaseSeason == eventSeason && releaseEpisode == eventEpisode)
                    score += 30; // Strong match - correct season and episode
                else if (releaseSeason == eventSeason)
                    score -= 20; // Same season but wrong episode
                else
                    score -= 30; // Wrong season entirely
            }
            else
            {
                // Event is DWCS but release doesn't look like DWCS
                return -50;
            }
        }

        // === UFC PPV / Fight Night - Number based ===
        // Event: "UFC 299" or "UFC Fight Night 240"
        // Release: "UFC.299.Main.Card" or "UFC.Fight.Night.240"
        var eventNumberMatch = Regex.Match(normalizedEvent, @"(?:ufc|bellator|pfl)\s*(?:fight\s*night\s*)?(\d+)", RegexOptions.IgnoreCase);
        if (eventNumberMatch.Success && !hasEventIdentifier)
        {
            hasEventIdentifier = true;
            var eventNumber = eventNumberMatch.Groups[1].Value;

            // Check if event is specifically a "Fight Night" vs PPV
            var eventIsFightNight = Regex.IsMatch(normalizedEvent, @"fight\s*night", RegexOptions.IgnoreCase);

            var releaseNumberMatch = Regex.Match(normalizedRelease, @"(?:ufc|bellator|pfl)\s*(?:fight\s*night\s*)?(\d+)", RegexOptions.IgnoreCase);
            if (releaseNumberMatch.Success)
            {
                var releaseNumber = releaseNumberMatch.Groups[1].Value;
                var releaseIsFightNight = Regex.IsMatch(normalizedRelease, @"fight\s*night", RegexOptions.IgnoreCase);

                if (releaseNumber == eventNumber)
                {
                    // Numbers match - but verify Fight Night vs PPV type matches
                    if (eventIsFightNight == releaseIsFightNight)
                        score += 25; // Perfect match
                    else
                        score += 15; // Number matches but type differs (could still be correct)
                }
                else
                {
                    score -= 30; // Wrong event number - definitely wrong event
                }
            }
            else
            {
                // Event has a number but release doesn't - wrong release type
                return -40;
            }
        }

        // === Fighter name matching (for events named by headliners) ===
        // Event: "UFC Fight Night: Covington vs Buckley"
        // Release: "UFC.Fight.Night.Covington.vs.Buckley"
        var vsMatch = Regex.Match(normalizedEvent, @"[:\s]([a-z]+)\s*(?:vs|v)\s*([a-z]+)", RegexOptions.IgnoreCase);
        if (vsMatch.Success)
        {
            var fighter1 = vsMatch.Groups[1].Value.ToLowerInvariant();
            var fighter2 = vsMatch.Groups[2].Value.ToLowerInvariant();

            var hasFighter1 = normalizedRelease.Contains(fighter1, StringComparison.OrdinalIgnoreCase);
            var hasFighter2 = normalizedRelease.Contains(fighter2, StringComparison.OrdinalIgnoreCase);

            if (hasFighter1 && hasFighter2)
                score += 15; // Both fighters match
            else if (hasFighter1 || hasFighter2)
                score += 5; // One fighter matches (might be on the card)
        }

        // === Generic term matching (fallback for non-standard naming) ===
        var eventWords = normalizedEvent.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !IsCommonWord(w) && !IsFightingCommonWord(w))
            .ToList();

        if (eventWords.Count > 0 && score == 0)
        {
            var matchCount = eventWords.Count(w => normalizedRelease.Contains(w, StringComparison.OrdinalIgnoreCase));
            score += (int)(10.0 * matchCount / eventWords.Count);
        }

        return score;
    }

    /// <summary>
    /// Check if a word is common in fighting sports (shouldn't be used for matching).
    /// </summary>
    private bool IsFightingCommonWord(string word)
    {
        var fightingCommon = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ufc", "bellator", "pfl", "boxing", "mma", "fight", "night", "card",
            "main", "prelims", "preliminary", "early", "dana", "white", "contender", "series"
        };
        return fightingCommon.Contains(word);
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

    /// <summary>
    /// Check if a team name matches in a release title.
    /// Returns (hasMatch, score) where hasMatch requires MAJORITY of significant words to match.
    /// This prevents "New Orleans Saints" from matching "New York Jets" just because "New" matches.
    ///
    /// Matching rules:
    /// 1. Team nickname (last word, e.g., "Saints", "Dolphins", "Jets") MUST match
    /// 2. OR at least 50% of all significant words must match
    /// 3. Single common city prefix words (New, Los, San) don't count as matches alone
    /// </summary>
    private (bool hasMatch, int score) CheckTeamMatch(string normalizedRelease, string teamName)
    {
        var teamWords = NormalizeTitle(teamName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !IsCommonWord(w))
            .ToList();

        if (teamWords.Count == 0)
            return (false, 0);

        // Common city prefix words that shouldn't count as a match alone
        var cityPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "new", "los", "san", "las", "st", "saint"
        };

        var matchedWords = teamWords
            .Where(w => normalizedRelease.Contains(w, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchedWords.Count == 0)
            return (false, 0);

        // Get the team nickname (typically the last word - "Saints", "Dolphins", "Jets", "Chiefs")
        var teamNickname = teamWords.Last();
        var nicknameMatches = normalizedRelease.Contains(teamNickname, StringComparison.OrdinalIgnoreCase);

        // Calculate match percentage
        var matchPercentage = (double)matchedWords.Count / teamWords.Count;

        // Determine if this is a real match:
        // 1. Team nickname must match, OR
        // 2. At least 50% of significant words must match
        // 3. But if ONLY city prefix words match (like just "New"), it's NOT a match
        var onlyCityPrefixesMatch = matchedWords.All(w => cityPrefixes.Contains(w));

        bool hasMatch;
        if (onlyCityPrefixesMatch)
        {
            // Only matched words like "New", "Los", "San" - not a real team match
            hasMatch = false;
        }
        else if (nicknameMatches)
        {
            // Nickname matches - definitely the right team
            hasMatch = true;
        }
        else if (matchPercentage >= 0.5)
        {
            // At least half the significant words match
            hasMatch = true;
        }
        else
        {
            // Not enough evidence this is the right team
            hasMatch = false;
        }

        // Score based on match percentage (max 20 points)
        var score = hasMatch ? (int)(20.0 * matchPercentage) : 0;

        return (hasMatch, score);
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
