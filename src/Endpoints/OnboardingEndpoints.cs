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
        app.MapGet("/api/onboarding/status", async (SportarrDbContext db, Services.ConfigService configService) =>
        {
            var config = await configService.GetConfigAsync();
            var hasRootFolder = await db.RootFolders.AnyAsync();
            var hasQualityProfile = await db.QualityProfiles.AnyAsync();
            var hasEnabledIndexer = await db.Indexers.AnyAsync(i => i.Enabled);
            var hasDownloadClient = await db.DownloadClients.AnyAsync(d => d.Enabled);
            var hasIptvSource = await db.IptvSources.AnyAsync();
            var hasEpgSource = await db.EpgSources.AnyAsync();
            // A mapping somewhere is not the same as a mapping for a league
            // the user actually follows. Any mapping at all counted, so an
            // install whose monitored league had no channel was reported ready
            // to record when nothing it cared about could be recorded. Rows
            // with a negative priority are admin exclusions, not mappings.
            var hasChannelLeagueMappings = await db.ChannelLeagueMappings
                .AnyAsync(m => m.Priority >= 0 && m.League != null && m.League.Monitored);
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
                // Dismissal used to live in the browser, so every new machine
                // showed the guide again on an install the user had already
                // set up and closed it on.
                dismissed = config.OnboardingDismissed,
            });
        });

        app.MapPost("/api/onboarding/dismiss", async (Services.ConfigService configService) =>
        {
            var config = await configService.GetConfigAsync();
            if (!config.OnboardingDismissed)
            {
                config.OnboardingDismissed = true;
                await configService.SaveConfigAsync(config);
            }
            return Results.Ok(new { dismissed = true });
        });

        return app;
    }
}
