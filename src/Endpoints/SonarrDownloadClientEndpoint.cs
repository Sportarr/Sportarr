using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class SonarrDownloadClientEndpoint
{
    public static IEndpointRouteBuilder MapSonarrDownloadClientEndpoint(this IEndpointRouteBuilder app)
    {

// GET /api/v3/downloadclient - Get download clients (Sonarr v3 API for Prowlarr)
// Prowlarr uses this to determine which protocols are supported (torrent vs usenet)
// Returns actual download clients configured by the user
app.MapGet("/api/v3/downloadclient", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogWarning("[PROWLARR] *** GET /api/v3/downloadclient - ENDPOINT WAS CALLED! ***");

    var downloadClients = await db.DownloadClients.ToListAsync();
    logger.LogWarning("[PROWLARR] Found {Count} download clients in database", downloadClients.Count);

    var radarrClients = downloadClients.Select(dc =>
    {
        // Map Sportarr download client type to protocol (torrent vs usenet)
        var protocol = dc.Type switch
        {
            DownloadClientType.QBittorrent => "torrent",
            DownloadClientType.Transmission => "torrent",
            DownloadClientType.Deluge => "torrent",
            DownloadClientType.RTorrent => "torrent",
            DownloadClientType.UTorrent => "torrent",
            DownloadClientType.Sabnzbd => "usenet",
            DownloadClientType.NzbGet => "usenet",
            _ => "torrent"
        };

        // Map type to Sonarr implementation name
        var (implementation, implementationName, configContract, infoLink) = dc.Type switch
        {
            DownloadClientType.QBittorrent => ("QBittorrent", "qBittorrent", "QBittorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Transmission => ("Transmission", "Transmission", "TransmissionSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Deluge => ("Deluge", "Deluge", "DelugeSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.RTorrent => ("RTorrent", "rTorrent", "RTorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.UTorrent => ("UTorrent", "uTorrent", "UTorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Sabnzbd => ("Sabnzbd", "SABnzbd", "SabnzbdSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.NzbGet => ("NzbGet", "NZBGet", "NzbGetSettings", "https://github.com/Sportarr/Sportarr"),
            _ => ("QBittorrent", "qBittorrent", "QBittorrentSettings", "https://github.com/Sportarr/Sportarr")
        };

        return new
        {
            enable = dc.Enabled,
            protocol = protocol,
            priority = dc.Priority,
            removeCompletedDownloads = true,
            removeFailedDownloads = true,
            name = dc.Name,
            fields = new object[] { },
            implementationName = implementationName,
            implementation = implementation,
            configContract = configContract,
            infoLink = infoLink,
            tags = new int[] { },
            id = dc.Id
        };
    }).ToList();

    logger.LogInformation("[PROWLARR] Returning {Count} download clients", radarrClients.Count);
    return Results.Ok(radarrClients);
});

        return app;
    }
}
