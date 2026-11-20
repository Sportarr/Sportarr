using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// The app's verdict on one release for one event.
/// </summary>
/// <param name="MatchScore">The match score, 0 to 100.</param>
/// <param name="IdentityRejected">True when the identity check hard-rejected the release.</param>
public sealed record ReleaseVerdict(int MatchScore, bool IdentityRejected);

/// <summary>
/// Computes again the verdict that the manual search list showed for a
/// release. The grab cannot trust the posted score, so it uses this.
/// </summary>
public class ReleaseVerdictService
{
    private readonly SportarrDbContext _db;
    private readonly ConfigService _configService;
    private readonly ReleaseMatchingService _releaseMatchingService;
    private readonly ReleaseMatchScorer _releaseMatchScorer;

    public ReleaseVerdictService(SportarrDbContext db, ConfigService configService,
        ReleaseMatchingService releaseMatchingService, ReleaseMatchScorer releaseMatchScorer)
    {
        _db = db;
        _configService = configService;
        _releaseMatchingService = releaseMatchingService;
        _releaseMatchScorer = releaseMatchScorer;
    }

    /// <summary>
    /// Gives the score and the identity verdict with the same inputs as the
    /// manual search. A change to the search inputs must also come here. The
    /// parity tests fail when the two differ.
    /// </summary>
    /// <param name="release">The release as the search list posted it.</param>
    /// <param name="eventId">The event the user grabbed it for.</param>
    /// <param name="part">The part that the search gave the release.</param>
    public virtual async Task<ReleaseVerdict> ComputeAsync(ReleaseSearchResult release, int eventId, string? part,
        CancellationToken cancellationToken)
    {
        var config = await _configService.GetConfigAsync();
        var evt = await _db.Events
            .AsNoTracking()
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Include(e => e.League)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            ?? throw new InvalidOperationException($"Event {eventId} not found");

        var usesParts = EventPartDetector.UsesParts(config.EnableMultiPartEpisodes, evt);
        part = !usesParts || !EventPartDetector.EventUsesMultiPart(evt.Title, evt.Sport ?? string.Empty, evt.League?.Name)
            ? null
            : EventPartDetector.CanonicalPartName(part, evt.Sport, evt.Title, evt.League?.Name);

        var earlyReleaseLimits = await _db.Indexers
            .AsNoTracking()
            .Where(i => i.EarlyReleaseLimit.HasValue)
            .Select(i => new { i.Id, i.EarlyReleaseLimit })
            .ToDictionaryAsync(i => i.Id, i => i.EarlyReleaseLimit, cancellationToken);
        var knownLeagues = await LeagueMatchContext.LoadAsync(_db, cancellationToken);
        var roundRaceNumbers = await RoundRaceNumberContext.LoadAsync(_db, evt, cancellationToken);
        var datePeers = await EventDateMatchContext.LoadAsync(_db, evt, cancellationToken);
        var venueContext = await NascarVenueMatchContext.LoadAsync(_db, evt, cancellationToken);

        var validation = _releaseMatchingService.ValidateRelease(release, evt, part, usesParts,
            earlyReleaseLimitDays: ReleaseMatchingService.ResolveEarlyReleaseLimit(release, earlyReleaseLimits),
            roundRaceNumbers: roundRaceNumbers, knownLeagues: knownLeagues, datePeers: datePeers);
        var score = _releaseMatchScorer.CalculateMatchScore(
            release.Title, evt, knownLeagues, part, usesParts,
            roundRaceNumbers, venueContext, release.SportarrEventId);

        return new ReleaseVerdict(score, validation.IsHardRejection);
    }
}
