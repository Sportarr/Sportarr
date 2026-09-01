using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Services;

/// <summary>
/// Matches external downloads to events using filename parsing and fuzzy matching.
/// Calculates confidence scores and suggests best event matches for manual imports.
/// </summary>
public class ImportMatchingService
{
    private readonly SportarrDbContext _db;
    private readonly MediaFileParser _parser;
    private readonly SportsFileNameParser _sportsParser;
    private readonly EventPartDetector _partDetector;
    private readonly ILogger<ImportMatchingService> _logger;

    /// <summary>Built on first use, then reused for the rest of this scope.</summary>
    private IReadOnlyList<(string Needle, string Sport)>? _leagueSports;

    /// <summary>
    /// Single words that say nothing about which league a file belongs to.
    ///
    /// A league really is named ONE, and "Race One" appears in motorsport
    /// releases, so matching that league on the bare word turned a superbike
    /// round into a fight card. The parser already refuses to read a trailing
    /// "one" as ONE Championship, and this fallback has to refuse it too. A
    /// league whose whole name is one of these needs the filename patterns,
    /// because the name alone cannot identify it.
    /// </summary>
    private static readonly HashSet<string> AmbiguousLeagueNames = new(StringComparer.Ordinal)
    {
        "one", "two", "three", "race", "round", "cup", "open", "final", "finals",
        "world", "super", "pro", "elite", "league", "series", "tour", "master",
        "masters", "classic", "national", "international", "championship",
        "championships", "game", "games", "match", "event", "night", "week"
    };

    public ImportMatchingService(
        SportarrDbContext db,
        MediaFileParser parser,
        SportsFileNameParser sportsParser,
        EventPartDetector partDetector,
        ILogger<ImportMatchingService> logger)
    {
        _db = db;
        _parser = parser;
        _sportsParser = sportsParser;
        _partDetector = partDetector;
        _logger = logger;
    }

    /// <summary>
    /// The sport for a file: what the filename patterns recognise, else what
    /// the library says, else nothing.
    ///
    /// Nothing means nothing. This used to answer "Fighting" when it could
    /// not tell, so an unrecognised league was read as a fight card and cut
    /// into segments no other sport has. Saying we do not know is both
    /// honest and harmless, because the caller then skips the sport-specific
    /// work instead of doing the wrong sport's.
    /// </summary>
    private async Task<string?> ResolveSportAsync(string title, string? parsedSport)
    {
        if (!string.IsNullOrEmpty(parsedSport)) return parsedSport;
        return await DetectSportFromLibraryAsync(title);
    }

    /// <summary>
    /// Work out a sport by finding which of this library's leagues the name
    /// belongs to.
    ///
    /// The league is the authority on its own sport, so a league that exists
    /// locally never needs a hand-written filename pattern. Matching is on
    /// whole words so "ONE" does not match "Bones" and a short league name
    /// cannot claim an unrelated file. The longest league name wins, which
    /// keeps "Premier League Darts" away from "Premier League".
    /// </summary>
    private async Task<string?> DetectSportFromLibraryAsync(string title)
    {
        var haystack = NormalizeForLeagueMatch(title);
        if (string.IsNullOrWhiteSpace(haystack)) return null;

        var leagues = await GetLeagueSportsAsync();

        string? best = null;
        var bestLength = 0;

        foreach (var (needle, sport) in leagues)
        {
            if (!ContainsWholeWords(haystack, needle)) continue;

            if (needle.Length > bestLength)
            {
                bestLength = needle.Length;
                best = sport;
            }
        }

        if (best != null)
        {
            _logger.LogDebug(
                "[Import Matching] No filename pattern matched; the library says this is {Sport}", best);
        }

        return best;
    }

    /// <summary>
    /// The library's leagues, normalised once per scope.
    ///
    /// A library scan calls this for every file the patterns do not
    /// recognise, and a folder of unfamiliar files is exactly the case this
    /// exists for. Reading the whole league table each time turned one scan
    /// into thousands of identical queries. The service is scoped, so this
    /// lasts for the scan and is rebuilt for the next one.
    /// </summary>
    private async Task<IReadOnlyList<(string Needle, string Sport)>> GetLeagueSportsAsync()
    {
        if (_leagueSports != null) return _leagueSports;

        var rows = await _db.Leagues
            .AsNoTracking()
            .Where(l => l.Sport != null && l.Sport != "")
            .Select(l => new { l.Name, l.AlternateName, l.Sport })
            .ToListAsync();

        var built = new List<(string Needle, string Sport)>(rows.Count);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Sport)) continue;

            Add(row.Name, row.Sport);

            // Release groups publish the sponsor-branded name as often as the
            // plain one, and the league stores those aliases. Same delimiters
            // the channel mapper splits on.
            if (!string.IsNullOrEmpty(row.AlternateName))
            {
                foreach (var alias in row.AlternateName.Split(
                    new[] { ',', '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Add(alias, row.Sport);
                }
            }
        }

        void Add(string? name, string sport)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var needle = NormalizeForLeagueMatch(name);
            if (needle.Length < 2) return;

            // A one-word name that is also an ordinary word cannot identify a
            // file on its own. Names of two words or more are specific enough.
            if (!needle.Contains(' ') && AmbiguousLeagueNames.Contains(needle)) return;

            built.Add((needle, sport));
        }

        _leagueSports = built;
        return _leagueSports;
    }

    /// <summary>Separators in release names become spaces so words line up.</summary>
    private static string NormalizeForLeagueMatch(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return string.Join(" ", new string(chars.ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>True when every word of the needle appears in order in the haystack.</summary>
    private static bool ContainsWholeWords(string haystack, string needle)
    {
        var padded = " " + haystack + " ";
        return padded.Contains(" " + needle + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Find best event match for an external download
    /// Returns suggestion with confidence score and quality info
    /// </summary>
    public async Task<ImportSuggestion?> FindBestMatchAsync(string title, string filePath)
    {
        _logger.LogInformation("[Import Matching] Finding match for: {Title}", title);

        // First try sports-specific parser for better accuracy
        var sportsResult = _sportsParser.Parse(title);

        // Fall back to generic parser, with ffprobe inspection so an
        // embedded SPORTARR tag is surfaced even when the name carries
        // no token (the file may have been renamed arbitrarily).
        var parsed = await _parser.ParseWithInspectionAsync(title, filePath);

        // Detect quality from parsed info
        var quality = parsed.Quality;
        var qualityScore = CalculateQualityScore(quality);

        // Try to detect part for fighting sports
        string? detectedPart = null;
        // The filename patterns only know the leagues someone wrote a
        // pattern for. Anything else used to be called Fighting, which is
        // wrong for most of a library and made the part detector read a
        // soccer file as though it had rounds. Ask the library what league
        // the name belongs to before falling back.
        var sportType = await ResolveSportAsync(title, sportsResult.Sport);
        // No sport, no part. Guessing one here is how a soccer file came back
        // with a fight card's segments.
        var partInfo = sportType is null ? null : _partDetector.DetectPart(title, sportType);
        if (partInfo != null)
        {
            detectedPart = partInfo.SegmentName;
            _logger.LogDebug("[Import Matching] Detected part: {Part}", detectedPart);
        }

        // Use sports parser result if it has high confidence, otherwise fall back to generic
        var eventTitle = sportsResult.Confidence >= 60 && !string.IsNullOrEmpty(sportsResult.EventTitle)
            ? sportsResult.EventTitle
            : parsed.EventTitle;

        _logger.LogDebug("[Import Matching] Using event title: {EventTitle} (Sports parser confidence: {Confidence}%)",
            eventTitle, sportsResult.Confidence);

        // AUTHORITATIVE ID TOKEN (docs/RELEASE_NAMING.md): a name tagged
        // {sportarr-ev-XXXXXXX}, or a file carrying an embedded SPORTARR
        // tag, identifies its event exactly - resolve it directly and skip
        // fuzzy matching. Unknown ids (unsynced league, legacy install)
        // fall through to the normal strategies.
        var matchTokenId = sportsResult.SportarrEventId ?? parsed.SportarrEventId;
        if (!string.IsNullOrEmpty(matchTokenId))
        {
            var tokenEventId = matchTokenId;
            var tokenEvent = await _db.Events
                .Include(e => e.League)
                .FirstOrDefaultAsync(e => e.ExternalId == tokenEventId);
            if (tokenEvent != null)
            {
                _logger.LogInformation("[Import Matching] Id token match: '{Title}' is tagged {Token} = '{Event}' (ID: {EventId})",
                    title, tokenEventId, tokenEvent.Title, tokenEvent.Id);
                return new ImportSuggestion
                {
                    EventId = tokenEvent.Id,
                    EventTitle = tokenEvent.Title,
                    League = tokenEvent.League?.Name,
                    Season = tokenEvent.Season,
                    EventDate = tokenEvent.EventDate,
                    Quality = quality,
                    QualityScore = qualityScore,
                    Part = detectedPart,
                    Confidence = 100,
                    ParsedSport = sportsResult.Sport,
                    ParsedOrganization = sportsResult.Organization
                };
            }
            _logger.LogWarning("[Import Matching] '{Title}' carries id token {Token} but no local event has that id - falling back to fuzzy matching",
                title, tokenEventId);
        }

        // Search for matching events in database
        var matches = await FindEventMatchesAsync(eventTitle, detectedPart, sportsResult.Organization, sportsResult.EventDate, sportsResult.RoundNumber);

        if (!matches.Any())
        {
            _logger.LogWarning("[Import Matching] No events found matching: {EventTitle}", eventTitle);
            return new ImportSuggestion
            {
                Quality = quality,
                QualityScore = qualityScore,
                Part = detectedPart,
                Confidence = 0,
                // Include parsed info for potential new event creation
                ParsedSport = sportsResult.Sport,
                ParsedOrganization = sportsResult.Organization,
                ParsedEventDate = sportsResult.EventDate,
                ParsedEventTitle = eventTitle
            };
        }

        // Calculate confidence score for each match, boosting if sports parser matched
        var scoredMatches = matches.Select(evt =>
        {
            var score = ScoreMatch(eventTitle, evt.Title, detectedPart, evt, sportsResult);
            return new
            {
                Event = evt,
                Score = Math.Min(100, score.Core + score.TieBreak),
                score.Core,
                score.TieBreak
            };
        }).OrderByDescending(m => m.Core).ThenByDescending(m => m.TieBreak).ToList();

        var bestMatch = scoredMatches.First();

        _logger.LogInformation("[Import Matching] Best match ({Confidence}%): {EventTitle} (ID: {EventId})",
            bestMatch.Score, bestMatch.Event.Title, bestMatch.Event.Id);

        // Don't suggest low-confidence matches - they're almost always wrong
        if (bestMatch.Score < 50)
        {
            _logger.LogInformation("[Import Matching] Best match confidence {Confidence}% is below threshold (50%) - no suggestion",
                bestMatch.Score);
            return new ImportSuggestion
            {
                Quality = quality,
                QualityScore = qualityScore,
                Part = detectedPart,
                Confidence = 0,
                ParsedSport = sportsResult.Sport,
                ParsedOrganization = sportsResult.Organization,
                ParsedEventDate = sportsResult.EventDate,
                ParsedEventTitle = eventTitle
            };
        }

        return new ImportSuggestion
        {
            EventId = bestMatch.Event.Id,
            EventTitle = bestMatch.Event.Title,
            League = bestMatch.Event.League?.Name,
            Season = bestMatch.Event.Season,
            EventDate = bestMatch.Event.EventDate,
            Quality = quality,
            QualityScore = qualityScore,
            Part = detectedPart,
            Confidence = bestMatch.Score,
            ParsedSport = sportsResult.Sport,
            ParsedOrganization = sportsResult.Organization
        };
    }

    /// <summary>
    /// Find potential event matches from database
    /// </summary>
    private async Task<List<Event>> FindEventMatchesAsync(string searchTitle, string? part, string? organization = null, DateTime? eventDate = null, int? roundNumber = null)
    {
        // Clean the search title
        var cleanTitle = CleanSearchString(searchTitle);

        // Build query with multiple search strategies
        var query = _db.Events
            .Include(e => e.League)
            .AsQueryable();

        // Strategy 1: Direct title match first. A second word-join pass only
        // fills remaining slots, so a separator difference ("Monte Carlo" vs
        // "Monte-Carlo") cannot hide an event, and broad wildcard hits cannot
        // evict a direct substring match from the candidate cap.
        var titleMatches = await query
            .Where(e => EF.Functions.Like(e.Title, $"%{EscapeLikePattern(cleanTitle)}%", "\\"))
            .OrderByDescending(e => e.EventDate)
            .Take(10)
            .ToListAsync();

        var searchWords = cleanTitle.Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (titleMatches.Count < 10 && searchWords.Length > 0)
        {
            var wordPattern = "%" + string.Join("%", searchWords.Select(EscapeLikePattern)) + "%";
            var wordJoinMatches = await query
                .Where(e => EF.Functions.Like(e.Title, wordPattern, "\\"))
                .OrderByDescending(e => e.EventDate)
                .Take(10)
                .ToListAsync();

            foreach (var match in wordJoinMatches)
            {
                if (titleMatches.Count >= 10)
                    break;
                if (!titleMatches.Any(m => m.Id == match.Id))
                {
                    titleMatches.Add(match);
                }
            }
        }

        // Strategy 1b: the events played on the date in the name. The window
        // pass further down keeps the ten latest events within three days,
        // and in a league that plays fifteen games a day those all sit at
        // the far edge of the window. The game the file names was never a
        // candidate, so the scorer chose among the wrong ones (issue #256
        // follow-up). A release is named with the broadcast-local date.
        if (eventDate.HasValue)
        {
            var day = eventDate.Value.Date;
            var nextDay = day.AddDays(1);
            var previousDay = day.AddDays(-1);
            // A row whose date is not yet verified may still hold the UTC
            // day from a legacy backfill, one day either side of the date
            // in the name. The scorer keeps its grace for those rows, so
            // the search has to reach them too. Exact days come first,
            // then the next day, which is the UTC day of a game played
            // west of UTC.
            var sameDayQuery = query
                .Where(e => e.BroadcastDate == day ||
                            (e.BroadcastDate == null && e.EventDate >= day && e.EventDate < nextDay) ||
                            (!e.BroadcastDateVerified && (e.BroadcastDate == nextDay || e.BroadcastDate == previousDay)))
                .OrderBy(e => e.BroadcastDate == day ? 0 : e.BroadcastDate == nextDay ? 1 : 2)
                .ThenBy(e => e.EventDate);

            // A library with several leagues can hold more events on one
            // date than the cap, so the named league's games come first.
            if (!string.IsNullOrEmpty(organization))
            {
                var leagueDayMatches = await sameDayQuery
                    .Where(e => e.League != null && EF.Functions.Like(e.League.Name, $"%{organization}%"))
                    .Take(40)
                    .ToListAsync();

                foreach (var match in leagueDayMatches)
                {
                    if (!titleMatches.Any(m => m.Id == match.Id))
                    {
                        titleMatches.Add(match);
                    }
                }
            }

            var sameDayMatches = await sameDayQuery
                .Take(40)
                .ToListAsync();

            foreach (var match in sameDayMatches)
            {
                if (!titleMatches.Any(m => m.Id == match.Id))
                {
                    titleMatches.Add(match);
                }
            }
        }

        // Strategy 2: If organization/league is known, search by league name
        if (!string.IsNullOrEmpty(organization))
        {
            var leagueMatches = await query
                .Where(e => e.League != null && EF.Functions.Like(e.League.Name, $"%{organization}%"))
                .OrderByDescending(e => e.EventDate)
                .Take(10)
                .ToListAsync();

            // Merge results, avoiding duplicates
            foreach (var match in leagueMatches)
            {
                if (!titleMatches.Any(m => m.Id == match.Id))
                {
                    titleMatches.Add(match);
                }
            }
        }

        // Strategy 3: If we have a date, look for events around that date
        if (eventDate.HasValue)
        {
            var dateMatches = await query
                .Where(e => e.EventDate >= eventDate.Value.AddDays(-3) && e.EventDate <= eventDate.Value.AddDays(3))
                .OrderByDescending(e => e.EventDate)
                .Take(10)
                .ToListAsync();

            foreach (var match in dateMatches)
            {
                if (!titleMatches.Any(m => m.Id == match.Id))
                {
                    titleMatches.Add(match);
                }
            }
        }

        // Strategy 4: Round number match (motorsport — same round has multiple sessions)
        // Search by Event.Round field (e.g., "2"), NOT EpisodeNumber (which is sequential: E10-E16 for Round 2)
        // This returns ALL sessions for the round (FP1, FP2, Qualifying, Race, etc.)
        // Session disambiguation happens later in CalculateMatchConfidence via Session scoring
        if (roundNumber.HasValue && !string.IsNullOrEmpty(organization))
        {
            var roundStr = roundNumber.Value.ToString();
            // Pre-season testing: indexers use Round 0 but Sportarr API uses Round 500
            // Search for both to ensure testing events are found
            var altRoundStr = roundStr == "0" ? "500" : (roundStr == "500" ? "0" : null);
            var roundMatches = await query
                .Where(e => (e.Round == roundStr || (altRoundStr != null && e.Round == altRoundStr)) &&
                            e.League != null && EF.Functions.Like(e.League.Name, $"%{organization}%"))
                .OrderByDescending(e => e.EventDate)
                .Take(20) // More results since one round has multiple sessions
                .ToListAsync();

            foreach (var match in roundMatches)
            {
                if (!titleMatches.Any(m => m.Id == match.Id))
                {
                    titleMatches.Add(match);
                }
            }
        }

        // Strategy 5: Extract words and search more broadly
        var words = cleanTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2) // Skip short words
            .Take(3) // Use top 3 significant words
            .ToList();

        if (words.Any() && titleMatches.Count < 5)
        {
            foreach (var word in words)
            {
                var wordMatches = await query
                    .Where(e => EF.Functions.Like(e.Title, $"%{word}%"))
                    .OrderByDescending(e => e.EventDate)
                    .Take(5)
                    .ToListAsync();

                foreach (var match in wordMatches)
                {
                    if (!titleMatches.Any(m => m.Id == match.Id))
                    {
                        titleMatches.Add(match);
                    }
                }
            }
        }

        // The cap bounds scoring work. It has to hold a full day of a busy
        // league behind the title matches, or the on-date pass is cut off.
        return titleMatches.Take(60).ToList();
    }

    /// <summary>
    /// Calculate confidence score (0-100) for how well a file matches an event
    /// </summary>
    internal int CalculateMatchConfidence(string searchTitle, string eventTitle, string? detectedPart, Event evt, SportsParseResult? sportsResult = null)
    {
        var score = ScoreMatch(searchTitle, eventTitle, detectedPart, evt, sportsResult);
        return Math.Min(100, score.Core + score.TieBreak);
    }

    /// <summary>
    /// Score a candidate in two parts. Core holds the evidence in the name:
    /// title, league, sport, date, part, round and session. TieBreak holds
    /// what the library knows about the event: recency and whether it has
    /// a file. Candidates rank by core first, so the tie-break separates
    /// only events the name cannot tell apart. Added into one number it
    /// outweighed the date, and a file re-imported over an earlier mistake
    /// landed on the next game along, the one still empty.
    /// </summary>
    internal ImportMatchScore ScoreMatch(string searchTitle, string eventTitle, string? detectedPart, Event evt, SportsParseResult? sportsResult = null)
    {
        int confidence = 0;
        int tieBreak = 0;

        // Normalize titles for comparison
        var normalizedSearch = NormalizeTitle(searchTitle);
        var normalizedEvent = NormalizeTitle(eventTitle);

        // A degenerate title carries no signal - and the contains branch
        // below would award EVERY event 40 points for an empty search string
        // (string.Contains("") is true).
        if (string.IsNullOrWhiteSpace(normalizedSearch) || string.IsNullOrWhiteSpace(normalizedEvent))
        {
            return new ImportMatchScore(0, 0);
        }

        // The parser prefixes a dated fixture with its league and date, and
        // event titles carry neither, so the prefix only dilutes the word
        // overlap. Compare the fixture itself.
        var searchFixture = StripDatedPrefix(normalizedSearch);

        // Exact title match = 60 points
        var fixturePoints = FixturePoints(searchFixture, normalizedEvent);
        if (searchFixture.Equals(normalizedEvent, StringComparison.OrdinalIgnoreCase))
        {
            confidence += 60;
        }
        // The same two clubs, side by side = 50 to 55 points
        else if (fixturePoints > 0)
        {
            confidence += fixturePoints;
        }
        // Contains match = 40 points
        else if ((searchFixture.Length >= 3 && normalizedEvent.Contains(searchFixture, StringComparison.OrdinalIgnoreCase)) ||
                 (normalizedEvent.Length >= 3 && searchFixture.Contains(normalizedEvent, StringComparison.OrdinalIgnoreCase)))
        {
            confidence += 40;
        }
        // Partial word match = up to 30 points
        else
        {
            var searchWords = searchFixture.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var eventWords = normalizedEvent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchingWords = searchWords.Intersect(eventWords, StringComparer.OrdinalIgnoreCase).Count();
            var totalWords = Math.Max(searchWords.Length, eventWords.Length);

            if (matchingWords > 0 && totalWords > 0)
            {
                // Score based on percentage of matching words
                var matchPercent = (double)matchingWords / totalWords;
                confidence += (int)(30 * matchPercent);
            }
        }

        // Special-stage token agreement (rally SSn releases, issue #102).
        // The stage number is the only difference between sixteen otherwise
        // identical stage titles, so it outweighs generic word overlap.
        // Gated to motorsport so an SS-like token in another sport's title
        // cannot swing an unrelated match.
        if (EventPartDetector.IsMotorsport(evt.Sport ?? "") ||
            (sportsResult != null && EventPartDetector.IsMotorsport(sportsResult.Sport ?? "")))
        {
            var searchStage = ExtractStageNumber(normalizedSearch);
            var eventStage = ExtractStageNumber(normalizedEvent);
            if (searchStage.HasValue && eventStage.HasValue)
            {
                confidence += searchStage == eventStage ? 15 : -40;
            }
            else if (searchStage.HasValue || eventStage.HasValue)
            {
                confidence -= 15;
            }
        }

        // Sport mismatch penalty: If sports parser detected a sport and event is a different sport, heavy penalty.
        //
        // Fall back to the league's sport when the event has none. An event
        // with a blank Sport skipped this check completely, so it stayed a
        // candidate for every release and the manual import dialog offered
        // NFL games for a baseball file (reported 2026-08-29).
        // The league's sport wins. Event rows and League rows disagree on a real
        // library: NFL events say "Football" while the NFL league says "American
        // Football", and UFC events say "Combat" while the league says
        // "Fighting". The parser speaks the league's vocabulary, so comparing
        // against the event penalised a release 50 points against its own
        // correct event.
        var eventSport = !string.IsNullOrEmpty(evt.League?.Sport) ? evt.League!.Sport : evt.Sport;
        if (sportsResult != null && !string.IsNullOrEmpty(sportsResult.Sport) && !string.IsNullOrEmpty(eventSport))
        {
            if (!LeagueSportRules.AreEquivalentSports(eventSport, sportsResult.Sport))
            {
                confidence -= 50;
                _logger.LogDebug("[Import Matching] Sport mismatch penalty: parsed '{ParsedSport}' vs event '{EventSport}' for '{EventTitle}'",
                    sportsResult.Sport, eventSport, evt.Title);
            }
        }
        // Neither the event nor its league names a sport, so the league itself
        // is the only signal left. A release that names one competition does
        // not belong to a different one.
        else if (sportsResult != null && string.IsNullOrEmpty(eventSport) &&
                 !string.IsNullOrEmpty(sportsResult.Organization) && evt.League != null &&
                 !string.IsNullOrEmpty(evt.League.Name))
        {
            var org = sportsResult.Organization;
            var leagueName = evt.League.Name;
            if (!leagueName.Contains(org, StringComparison.OrdinalIgnoreCase) &&
                !org.Contains(leagueName, StringComparison.OrdinalIgnoreCase))
            {
                confidence -= 50;
                _logger.LogDebug("[Import Matching] League mismatch penalty: parsed '{ParsedOrg}' vs league '{League}' for '{EventTitle}'",
                    org, leagueName, evt.Title);
            }
        }

        // Sports parser bonus: If organization matches league = +15 points
        if (sportsResult != null && !string.IsNullOrEmpty(sportsResult.Organization) && evt.League != null)
        {
            if (evt.League.Name.Contains(sportsResult.Organization, StringComparison.OrdinalIgnoreCase) ||
                sportsResult.Organization.Contains(evt.League.Name, StringComparison.OrdinalIgnoreCase))
            {
                confidence += 15;
            }
        }

        // Date match bonus.
        //
        // Compare broadcast-local dates, not raw timestamps. An evening game
        // in the United States is stored in UTC as the following calendar day
        // (a 19:05 first pitch is 00:05Z), so measuring from EventDate put
        // every night game one day ahead of the date its release is named
        // with. The previous day's game then scored higher than the right
        // one, which is how a file landed on the wrong date (issue #256).
        // The grab side already compares this way.
        if (sportsResult?.EventDate != null)
        {
            var eventDate = (evt.BroadcastDate ?? evt.EventDate.Date).Date;
            var daysDiff = Math.Abs((eventDate - sportsResult.EventDate.Value.Date).TotalDays);

            // An exact date has to beat a neighbouring one outright. A
            // baseball series puts the same two teams on the field on
            // consecutive days, so the title says nothing that tells those
            // events apart and the date is the only thing that does. The
            // lead is ten points so a title with a suffix on the named day
            // still beats a plain-titled neighbour.
            if (daysDiff == 0)
            {
                confidence += 20;
            }
            // A fixture with a verified broadcast date is the game played on
            // that date, and the day beside it is the next game of the same
            // series, not a near miss. The grab side applies the same rule.
            // An unverified date keeps the grace because a legacy backfill
            // may still hold the UTC day.
            else if (evt.BroadcastDateVerified && evt.HomeTeamId.HasValue && evt.AwayTeamId.HasValue)
            {
                confidence -= 100;
                _logger.LogDebug("[Import Matching] Date mismatch REJECT: release {ReleaseDate} vs fixture {EventDate} for '{EventTitle}'",
                    sportsResult.EventDate.Value.ToString("yyyy-MM-dd"), eventDate.ToString("yyyy-MM-dd"), evt.Title);
            }
            else if (daysDiff <= 1) confidence += 10;
            else if (daysDiff <= 3) confidence += 8;
            else if (daysDiff <= 7) confidence += 5;
        }

        // Part match for fighting sports = 20 points
        if (!string.IsNullOrEmpty(detectedPart))
        {
            if (evt.MonitoredParts == null || string.IsNullOrEmpty(evt.MonitoredParts))
            {
                // Event monitors all parts
                confidence += 15;
            }
            else if (evt.MonitoredParts.Contains(detectedPart, StringComparison.OrdinalIgnoreCase))
            {
                // Event specifically monitors this part
                confidence += 20;
            }
        }

        // Motorsport round match. The round only ever picked candidates out of
        // the database. Candidates also arrive from the title, date and word
        // searches, so an event from a different round competed on equal terms
        // and collected the same session boost below, which is how a race file
        // could import against the race of another round. Only compare a
        // numeric round: other sports keep bracket names such as "Semi-final"
        // in the same field.
        if (sportsResult?.RoundNumber != null && int.TryParse(evt.Round, out var eventRound))
        {
            var parsedRound = sportsResult.RoundNumber.Value;
            // Indexers number pre-season testing 0 where the API uses 500.
            var roundsAgree = eventRound == parsedRound ||
                              (parsedRound == 0 && eventRound == 500) ||
                              (parsedRound == 500 && eventRound == 0);

            if (roundsAgree)
            {
                confidence += 25;
            }
            else
            {
                confidence -= 100;
                _logger.LogDebug("[Import Matching] Round mismatch REJECT: parsed round {Parsed} vs event round {EventRound} for {EventTitle}",
                    parsedRound, eventRound, evt.Title);
            }
        }

        // Motorsport session match: if parsed filename has a session (Race, Qualifying, etc.),
        // compare against the event's title to disambiguate events sharing the same round number
        if (sportsResult != null && !string.IsNullOrEmpty(sportsResult.Session) && sportsResult.RoundNumber.HasValue)
        {
            var eventSession = EventPartDetector.DetectMotorsportSessionType(evt.Title, evt.League?.Name ?? "");
            if (!string.IsNullOrEmpty(eventSession))
            {
                if (eventSession.Equals(sportsResult.Session, StringComparison.OrdinalIgnoreCase))
                {
                    confidence += 20; // Session matches — strong signal
                    _logger.LogDebug("[Import Matching] Session match boost: '{Session}' matches event '{EventTitle}'",
                        sportsResult.Session, evt.Title);
                }
                else
                {
                    confidence -= 100; // Session mismatch — hard reject (e.g., file is "Practice 1" but event is "Race")
                    _logger.LogDebug("[Import Matching] Session mismatch REJECT: parsed '{ParsedSession}' vs event '{EventSession}' for '{EventTitle}'",
                        sportsResult.Session, eventSession, evt.Title);
                }
            }
            else
            {
                // File has a session but event title has no detectable session — likely wrong match
                // e.g., "Practice 1" file matching a generic "Grand Prix" event
                confidence -= 30;
                _logger.LogDebug("[Import Matching] File has session '{Session}' but event '{EventTitle}' has no session — penalizing",
                    sportsResult.Session, evt.Title);
            }
        }

        // Event is recent (within 30 days) = 10 points
        if (Math.Abs((DateTime.UtcNow - evt.EventDate).TotalDays) <= 30)
        {
            tieBreak += 10;
        }

        // Event doesn't have file yet = 10 points (more likely to want this)
        if (!evt.HasFile)
        {
            tieBreak += 10;
        }

        return new ImportMatchScore(confidence, tieBreak);
    }

    private static int? ExtractStageNumber(string normalizedTitle)
    {
        var match = Regex.Match(normalizedTitle, @"\bSS(\d+)\b", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var stage) ? stage : null;
    }

    // LIKE metacharacters in a filename must stay literal, or an odd release
    // name widens the pattern and floods the candidate cap.
    private static string EscapeLikePattern(string input) =>
        input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    // "MLB 2026 08 12 " and its kin, as NormalizeTitle leaves them.
    private static readonly Regex DatedPrefix = new(@"^[A-Za-z0-9]{1,6}\s+\d{4}\s+\d{2}\s+\d{2}\s+", RegexOptions.Compiled);

    private static readonly Regex FixtureSides = new(@"^(?<a>.+?)\s+(?:vs?|@)\s+(?<b>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string StripDatedPrefix(string normalizedTitle)
    {
        var stripped = DatedPrefix.Replace(normalizedTitle, "", 1).Trim();
        return stripped.Length > 0 ? stripped : normalizedTitle;
    }

    /// <summary>
    /// How two fixtures relate, side by side. A release names the away
    /// side first as often as the home side, so the same clubs the other
    /// way round are the same fixture, a shade behind the home order so a
    /// dateless name still prefers the leg it names. A name that gives
    /// only the nicknames ("Cubs vs Pirates") sits inside each side of
    /// the full title, which word overlap alone scored too low to pass
    /// the suggestion gate.
    /// </summary>
    private static int FixturePoints(string search, string eventTitle)
    {
        var a = FixtureSides.Match(search);
        var b = FixtureSides.Match(eventTitle);
        if (!a.Success || !b.Success) return 0;

        var searchHome = Words(a.Groups["a"].Value);
        var searchAway = Words(a.Groups["b"].Value);
        var eventHome = Words(b.Groups["a"].Value);
        var eventAway = Words(b.Groups["b"].Value);
        if (searchHome.Length == 0 || searchAway.Length == 0 || eventHome.Length == 0 || eventAway.Length == 0) return 0;

        if (SameWords(searchHome, eventHome) && SameWords(searchAway, eventAway)) return 60;
        if (SameWords(searchHome, eventAway) && SameWords(searchAway, eventHome)) return 55;
        if (Subset(searchHome, eventHome) && Subset(searchAway, eventAway)) return 52;
        if (Subset(searchHome, eventAway) && Subset(searchAway, eventHome)) return 50;
        return 0;
    }

    private static string[] Words(string side) => side.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool SameWords(string[] first, string[] second) =>
        first.SequenceEqual(second, StringComparer.OrdinalIgnoreCase);

    private static bool Subset(string[] part, string[] whole) =>
        part.All(word => whole.Contains(word, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Normalize title for better comparison
    /// </summary>
    private string NormalizeTitle(string title)
    {
        // Remove common separators and normalize
        var normalized = title
            .Replace(":", " ")
            .Replace("-", " ")
            .Replace(".", " ")
            .Replace("_", " ")
            .Replace("  ", " ")
            .Trim();

        // Remove common prefixes that might not be in the database
        var prefixes = new[] { "UFC", "WWE", "AEW", "NFL", "NBA", "NHL", "MLB", "F1", "PFL" };
        foreach (var prefix in prefixes)
        {
            if (normalized.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                // Keep the prefix but ensure consistent formatting
                normalized = prefix + " " + normalized.Substring(prefix.Length + 1).Trim();
                break;
            }
        }

        return normalized;
    }

    /// <summary>
    /// Clean search string for better matching
    /// </summary>
    private string CleanSearchString(string input)
    {
        // Remove common release group suffixes
        var cleaned = Regex.Replace(input, @"-[A-Z0-9]+$", "", RegexOptions.IgnoreCase);

        // Remove year if present
        cleaned = Regex.Replace(cleaned, @"\b(19|20)\d{2}\b", "");

        // Remove quality indicators
        cleaned = Regex.Replace(cleaned, @"\b(720p|1080p|2160p|4K|BluRay|WEB-DL|HDTV|WEBRip)\b", "", RegexOptions.IgnoreCase);

        // Clean up extra spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    /// <summary>
    /// Calculate quality score (matching ReleaseEvaluator logic)
    /// </summary>
    private static int CalculateQualityScore(string? quality)
    {
        return ReleaseEvaluator.CalculateQualityScoreFromName(quality);
    }

    /// <summary>
    /// Get list of possible event matches for user to choose from
    /// </summary>
    public async Task<List<ImportSuggestion>> GetAllPossibleMatchesAsync(string title)
    {
        // Manual review has to see the same candidates the automatic matcher
        // weighs. Parsing with the generic parser alone dropped the sport,
        // the championship, the round, and the session, so a name like
        // "BSB 2026 Round01 Oulton Park Race One" was ranked on its title
        // text alone and could suggest events from another championship.
        var sportsResult = _sportsParser.Parse(title);
        var parsed = _parser.Parse(title);

        var eventTitle = sportsResult.Confidence >= 60 && !string.IsNullOrEmpty(sportsResult.EventTitle)
            ? sportsResult.EventTitle
            : parsed.EventTitle;

        // The candidate picker has to read a file the same way the first scan
        // did. Left on the old default it called an unfamiliar league a fight
        // card and scored its parts as though it had rounds.
        var sportType = await ResolveSportAsync(title, sportsResult.Sport);
        var detectedPart = sportType is null
            ? null
            : _partDetector.DetectPart(title, sportType)?.SegmentName;

        var events = await FindEventMatchesAsync(
            eventTitle, detectedPart, sportsResult.Organization, sportsResult.EventDate, sportsResult.RoundNumber);

        var suggestions = new List<(ImportSuggestion Suggestion, ImportMatchScore Score)>();

        foreach (var evt in events)
        {
            var score = ScoreMatch(eventTitle, evt.Title, detectedPart, evt, sportsResult);
            var confidence = Math.Min(100, score.Core + score.TieBreak);

            suggestions.Add((new ImportSuggestion
            {
                EventId = evt.Id,
                EventTitle = evt.Title,
                League = evt.League?.Name,
                Season = evt.Season,
                EventDate = evt.EventDate,
                Part = detectedPart,
                Confidence = confidence,
                ParsedSport = sportsResult.Sport,
                ParsedOrganization = sportsResult.Organization,
                // Null when the release name carries no date. The dialog says so,
                // because that is why the same fixture appears on several dates
                // and why the person has to pick one.
                ParsedEventDate = sportsResult.EventDate
            }, score));
        }

        // Same order as the automatic pick, so the list leads with the
        // event the importer would choose.
        return suggestions
            .Where(s => s.Suggestion.Confidence > 0)
            .OrderByDescending(s => s.Score.Core)
            .ThenByDescending(s => s.Score.TieBreak)
            .Select(s => s.Suggestion)
            .ToList();
    }
}

/// <summary>
/// The two halves of a candidate's score. Core is the evidence in the name.
/// TieBreak is what the library knows about the event.
/// </summary>
internal readonly record struct ImportMatchScore(int Core, int TieBreak);

/// <summary>
/// Suggested event match for an import
/// </summary>
public class ImportSuggestion
{
    public int? EventId { get; set; }
    public string? EventTitle { get; set; }
    public string? League { get; set; }
    public string? Season { get; set; }
    public DateTime? EventDate { get; set; }
    public string? Quality { get; set; }
    public int QualityScore { get; set; }
    public string? Part { get; set; }
    public int Confidence { get; set; } // 0-100

    // Parsed info from sports-specific parser (for creating new events)
    public string? ParsedSport { get; set; }
    public string? ParsedOrganization { get; set; }
    public DateTime? ParsedEventDate { get; set; }
    public string? ParsedEventTitle { get; set; }
}
