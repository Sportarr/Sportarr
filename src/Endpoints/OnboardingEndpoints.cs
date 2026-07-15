using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;

namespace Sportarr.Api.Endpoints;

/// <summary>
/// Backs the first-run setup guide. Reports what a fresh install still needs so
/// the guide can walk a user straight from install to "it records games" without
/// hopping across the settings pages by hand.
/// </summary>
public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/onboarding/status", async (SportarrDbContext db) =>
        {
            var hasRootFolder = await db.RootFolders.AnyAsync();
            var hasQualityProfile = await db.QualityProfiles.AnyAsync();
            var hasEnabledIndexer = await db.Indexers.AnyAsync(i => i.Enabled);
            var hasDownloadClient = await db.DownloadClients.AnyAsync(d => d.Enabled);
            var hasIptvSource = await db.IptvSources.AnyAsync();
            var hasEpgSource = await db.EpgSources.AnyAsync();
            var hasChannelLeagueMappings = await db.ChannelLeagueMappings.AnyAsync();
            var monitoredLeagueCount = await db.Leagues.CountAsync(l => l.Monitored);

            // Two independent ways to actually acquire an event: grab it from an
            // indexer via a download client, or record it off an IPTV channel
            // mapped to the league. Either path satisfies "ready".
            var downloadReady = hasEnabledIndexer && hasDownloadClient;
            var dvrReady = hasIptvSource && hasChannelLeagueMappings;

            return Results.Ok(new
            {
                hasRootFolder,
                hasQualityProfile,
                hasEnabledIndexer,
                hasDownloadClient,
                hasIptvSource,
                hasEpgSource,
                hasChannelLeagueMappings,
                monitoredLeagueCount,
                downloadReady,
                dvrReady,
                // Fully set up: somewhere to put files, something to follow, and a
                // way to get it. The guide can stop nagging once this is true.
                isReady = hasRootFolder && monitoredLeagueCount > 0 && (downloadReady || dvrReady),
            });
        });

        return app;
    }
}
