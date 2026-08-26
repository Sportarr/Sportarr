using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

/// <summary>
/// HDHomeRun network tuner emulation. Lets Plex DVR / Jellyfin Live
/// TV / Emby / Channels DVR auto-discover Sportarr as a tuner and
/// pull our IPTV channel list as their channel lineup. Each downstream
/// player then talks to our /api/iptv/stream/{channelId} proxy as if
/// it were tuning a real RF channel.
///
/// Three required endpoints per the SiliconDust HDHomeRun HTTP API:
///   GET /discover.json     - device manifest (model, tuner count,
///                            BaseURL, LineupURL)
///   GET /lineup.json       - channel array (GuideNumber, GuideName,
///                            URL)
///   GET /lineup_status.json - tuner state (idle / scanning)
///
/// We also expose /device.xml (UPnP description) so Plex's SSDP-less
/// discovery flow can fall back to the manual "add network tuner"
/// path with just the host:port.
/// </summary>
public static class HdHomeRunEndpoints
{
    // Static, deterministic device id per install. Many media servers
    // cache by device id so this needs to be stable across restarts.
    // We derive it from the data path so two Sportarr installs on
    // the same LAN don't collide.
    private static string? _cachedDeviceId;
    private const int DefaultTunerCount = 4;

    public static IEndpointRouteBuilder MapHdHomeRunEndpoints(this IEndpointRouteBuilder app)
    {
        // Plex/Emby/Jellyfin all hit /discover.json on whatever host:port
        // the user typed. We respond with the same host they came in on
        // so any reverse-proxy setup picks the right BaseURL.
        app.MapGet("/discover.json", (HttpContext ctx, IConfiguration config) =>
        {
            var baseUrl = ResolveBaseUrl(ctx);
            var deviceId = GetDeviceId(config);
            return Results.Json(new
            {
                FriendlyName = "Sportarr",
                Manufacturer = "Sportarr",
                ModelNumber = "HDTC-2US",
                FirmwareName = "hdhomeruntc_atsc",
                FirmwareVersion = "20240425",
                DeviceID = deviceId,
                DeviceAuth = "sportarr",
                BaseURL = baseUrl,
                LineupURL = $"{baseUrl}/lineup.json",
                TunerCount = DefaultTunerCount
            });
        }).AllowAnonymous();

        // Lineup is built from enabled, sports-tagged IPTV channels
        // with stable channel numbers. Anything missing a channel
        // number gets one generated from its DB id (>= 9000) so Plex
        // doesn't reject the lineup for duplicate or zero numbers.
        app.MapGet("/lineup.json", async (
            HttpContext ctx,
            SportarrDbContext db,
            CancellationToken ct) =>
        {
            var baseUrl = ResolveBaseUrl(ctx);
            var channels = await db.IptvChannels
                .Include(c => c.Source)
                .Where(c => c.IsEnabled && !c.IsHidden && c.Source != null && c.Source.IsActive)
                .OrderBy(c => c.ChannelNumber ?? int.MaxValue)
                .ThenBy(c => c.Name)
                .ToListAsync(ct);

            var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lineup = new List<object>(channels.Count);

            foreach (var ch in channels)
            {
                var guideNumber = (ch.ChannelNumber > 0 ? ch.ChannelNumber!.ToString() : null)
                                  ?? (9000 + ch.Id).ToString();

                // Dedup channel numbers within a single lineup -
                // duplicates make Plex skip the entire response.
                while (seenNumbers.Contains(guideNumber))
                {
                    guideNumber = (int.Parse(guideNumber) + 1).ToString();
                }
                seenNumbers.Add(guideNumber);

                lineup.Add(new
                {
                    GuideNumber = guideNumber,
                    GuideName = ch.Name ?? $"Channel {ch.Id}",
                    URL = $"{baseUrl}/api/iptv/stream/{ch.Id}",
                    HD = ch.QualityScore >= 200 ? 1 : 0,
                    Favorite = ch.IsFavorite ? 1 : 0,
                    VideoCodec = "H264",
                    AudioCodec = "AAC"
                });
            }

            return Results.Json(lineup);
        }).AllowAnonymous();

        // Tuner status. We don't model multi-tuner state; report
        // ScanInProgress=0 always so consumers don't think we're
        // mid-scan. The Plex client reads ScanPossible to decide
        // whether to offer the user a "rescan" button - we say yes.
        app.MapGet("/lineup_status.json", () => Results.Json(new
        {
            ScanInProgress = 0,
            ScanPossible = 1,
            Source = "Cable",
            SourceList = new[] { "Cable" }
        })).AllowAnonymous();

        // Plex POSTs /lineup.post?scan=start to trigger a rescan.
        // We treat it as a no-op since our lineup is always live;
        // 200 OK is the right answer.
        app.MapPost("/lineup.post", () => Results.Ok()).AllowAnonymous();

        // /device.xml is the UPnP/SSDP description doc. Plex uses
        // this when adding a tuner manually by IP without SSDP
        // discovery. The minimum shape Plex accepts is below.
        app.MapGet("/device.xml", (HttpContext ctx, IConfiguration config) =>
        {
            var baseUrl = ResolveBaseUrl(ctx);
            var deviceId = GetDeviceId(config);
            var xml =
$@"<?xml version=""1.0"" encoding=""UTF-8""?>
<root xmlns=""urn:schemas-upnp-org:device-1-0"">
  <specVersion><major>1</major><minor>0</minor></specVersion>
  <URLBase>{baseUrl}</URLBase>
  <device>
    <deviceType>urn:schemas-upnp-org:device:MediaServer:1</deviceType>
    <friendlyName>Sportarr</friendlyName>
    <manufacturer>Sportarr</manufacturer>
    <modelName>HDTC-2US</modelName>
    <modelNumber>HDTC-2US</modelNumber>
    <serialNumber>{deviceId}</serialNumber>
    <UDN>uuid:{FormatUuid(deviceId)}</UDN>
  </device>
</root>";
            return Results.Content(xml, "application/xml");
        }).AllowAnonymous();

        return app;
    }

    private static string ResolveBaseUrl(HttpContext ctx)
    {
        var scheme = ctx.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? ctx.Request.Scheme;
        var host = ctx.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? ctx.Request.Host.Value;
        return $"{scheme}://{host}".TrimEnd('/');
    }

    private static string GetDeviceId(IConfiguration config)
    {
        if (_cachedDeviceId != null) return _cachedDeviceId;

        // The data path is not unique per install: every container install
        // uses the default, so two of them advertised the very same tuner
        // identity and a media server would merge them, serve one the other's
        // cached lineup, or lose one of them entirely. The API key is
        // generated per install and persists, so it is what actually
        // distinguishes them. The path remains the fallback for the case where
        // no key has been generated yet.
        var seed = config["Sportarr:ApiKey"];
        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = config["Sportarr:DataPath"] ?? "/data";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        _cachedDeviceId = Convert.ToHexString(bytes, 0, 4); // 8 hex chars
        return _cachedDeviceId;
    }

    private static string FormatUuid(string deviceId)
    {
        // Synthesize a deterministic UPnP UUID from the 8-char device
        // id by padding to a v4-shape string.
        var pad = deviceId.PadRight(32, '0');
        return $"{pad.Substring(0, 8)}-{pad.Substring(8, 4)}-{pad.Substring(12, 4)}-{pad.Substring(16, 4)}-{pad.Substring(20, 12)}";
    }
}
