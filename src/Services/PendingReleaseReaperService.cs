using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Sportarr.Api.Services;

/// <summary>
/// Walks PendingReleases whose delay window has expired and promotes the best
/// release per event into the download queue, cancelling the rest.
///
/// Sonarr/Radarr's delay profile feature: when a release shows up but a delay
/// is configured, hold it briefly so a higher-quality release can supersede it.
/// Without this reaper, RSS sync would have to grab the first matching release
/// immediately - which is what it did before this service existed.
/// </summary>
public class PendingReleaseReaperService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PendingReleaseReaperService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    public PendingReleaseReaperService(
        IServiceProvider serviceProvider,
        ILogger<PendingReleaseReaperService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Pending Release Reaper] Service started (poll interval: {Interval})", PollInterval);

        // Allow the host to fully initialize before first pass.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReapAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Pending Release Reaper] Pass failed - retrying in {Interval}", PollInterval);
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[Pending Release Reaper] Service stopped");
    }

    private async Task ReapAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        var downloadClientService = scope.ServiceProvider.GetRequiredService<DownloadClientService>();

        var now = DateTime.UtcNow;

        // Pull all expired pending releases. The DB index on (Status, ReleasableAt)
        // keeps this cheap even with many entries.
        var ready = await db.PendingReleases
            .Include(p => p.Event)
                .ThenInclude(e => e!.League)
            .Where(p => p.Status == PendingReleaseStatus.Pending && p.ReleasableAt <= now)
            .ToListAsync(cancellationToken);

        if (ready.Count == 0) return;

        // Group by event - the per-event winner is the highest combined score.
        // Cancel the losers so they don't all get grabbed.
        var groups = ready.GroupBy(p => p.EventId);

        foreach (var group in groups)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var evt = group.First().Event;
            if (evt == null)
            {
                // Orphan - mark all as cancelled and move on.
                foreach (var orphan in group) orphan.Status = PendingReleaseStatus.Cancelled;
                continue;
            }

            // If the event already has a file or is no longer monitored, drop everything.
            if (!evt.Monitored || evt.HasFile)
            {
                foreach (var p in group)
                {
                    p.Status = PendingReleaseStatus.Cancelled;
                    p.Reason = evt.HasFile ? "Event already has file" : "Event no longer monitored";
                }
                continue;
            }

            var winner = group
                .OrderByDescending(p => p.QualityScore)
                .ThenByDescending(p => p.CustomFormatScore)
                .ThenByDescending(p => p.Score)
                .ThenByDescending(p => p.MatchScore)
                .ThenByDescending(p => p.Seeders ?? 0)
                .First();

            var grabbed = await TryGrabPendingAsync(db, downloadClientService, evt, winner, cancellationToken);

            if (grabbed)
            {
                winner.Status = PendingReleaseStatus.Released;
                foreach (var loser in group.Where(p => p.Id != winner.Id))
                {
                    loser.Status = PendingReleaseStatus.Cancelled;
                    loser.Reason = $"Superseded by {winner.Title}";
                }
                _logger.LogInformation(
                    "[Pending Release Reaper] Released best-of-window for '{Event}': {Winner} (score {Score})",
                    evt.Title, winner.Title, winner.QualityScore + winner.CustomFormatScore);
            }
            else
            {
                winner.Status = PendingReleaseStatus.Failed;
                winner.Reason = "Grab attempt failed";
                _logger.LogWarning("[Pending Release Reaper] Grab failed for '{Title}' - leaving losers pending for next pass",
                    winner.Title);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> TryGrabPendingAsync(
        SportarrDbContext db,
        DownloadClientService downloadClientService,
        Event evt,
        PendingRelease pending,
        CancellationToken cancellationToken)
    {
        var supportedTypes = DownloadClientService.GetClientTypesForProtocol(pending.Protocol);
        if (supportedTypes.Count == 0)
        {
            _logger.LogWarning("[Pending Release Reaper] Unknown protocol '{Protocol}' for '{Title}'", pending.Protocol, pending.Title);
            return false;
        }

        var leagueTags = evt.League?.Tags ?? new List<int>();
        var allClients = await db.DownloadClients
            .Where(dc => dc.Enabled && supportedTypes.Contains(dc.Type))
            .OrderBy(dc => dc.Priority)
            .ToListAsync(cancellationToken);
        var downloadClient = allClients
            .FirstOrDefault(dc => Helpers.TagHelper.TagsMatch(dc.Tags, leagueTags));

        if (downloadClient == null)
        {
            _logger.LogWarning("[Pending Release Reaper] No {Protocol} download client for '{Event}'",
                pending.Protocol, evt.Title);
            return false;
        }

        var indexerRecord = await db.Indexers
            .FirstOrDefaultAsync(i => i.Name == pending.Indexer, cancellationToken);

        var downloadId = await downloadClientService.AddDownloadAsync(
            downloadClient,
            pending.DownloadUrl,
            downloadClient.Category,
            pending.Title,
            indexerRecord?.SeedRatio,
            indexerRecord?.SeedTime);

        if (string.IsNullOrEmpty(downloadId))
        {
            _logger.LogError("[Pending Release Reaper] Download client refused '{Title}'", pending.Title);
            return false;
        }

        db.DownloadQueue.Add(new DownloadQueueItem
        {
            EventId = evt.Id,
            Title = pending.Title,
            DownloadId = downloadId,
            DownloadClientId = downloadClient.Id,
            Status = DownloadStatus.Queued,
            Quality = pending.Quality,
            Codec = pending.Codec,
            Source = pending.Source,
            Size = pending.Size,
            Downloaded = 0,
            Progress = 0,
            Indexer = pending.Indexer,
            IndexerId = indexerRecord?.Id,
            Protocol = pending.Protocol,
            TorrentInfoHash = pending.TorrentInfoHash,
            RetryCount = 0,
            LastUpdate = DateTime.UtcNow,
            QualityScore = pending.QualityScore,
            CustomFormatScore = pending.CustomFormatScore,
            Part = pending.Part,
            IsManualSearch = false
        });

        return true;
    }
}
