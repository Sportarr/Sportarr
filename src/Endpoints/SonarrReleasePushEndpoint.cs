using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sportarr.Api.Endpoints;

/// <summary>
/// Sonarr v3-compatible release push endpoint (see docs/API_VERSIONING.md).
/// External release watchers (autobrr and similar IRC announce / RSS tools)
/// push individual releases here the moment they appear on a tracker. The
/// release runs through the exact same match → evaluate → grab decision
/// engine as RSS sync, and the response tells the pusher whether it was
/// grabbed, held for a delay window, or rejected and why.
///
/// Contract notes (what pushers parse):
/// - Success is an ARRAY echoing the release with approved/rejected/
///   temporarilyRejected/rejections on element [0].
/// - Validation failures are HTTP 400 with an ARRAY of
///   {propertyName, errorMessage, errorCode, attemptedValue, severity}.
/// </summary>
public static class SonarrReleasePushEndpoint
{
    // Indexer flag bitmask (as sent by pushers) → the flag names Sportarr's
    // custom format IndexerFlag specifications match against (ReleaseEvaluator
    // uses the same numeric mapping).
    private static readonly (int Bit, string Name)[] FlagNames =
    {
        (1, "freeleech"),
        (2, "halfleech"),
        (4, "doubleupload"),
        (8, "internal"),
        (16, "scene"),
        (32, "freeleech75"),
        (64, "freeleech25"),
        (128, "nuked"),
        (256, "subtitles")
    };

    public static IEndpointRouteBuilder MapSonarrReleasePushEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v3/release/push", async (
            HttpRequest request,
            RssSyncService rssSyncService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);

            JsonElement body;
            try
            {
                body = JsonSerializer.Deserialize<JsonElement>(json);
                if (body.ValueKind != JsonValueKind.Object)
                    throw new JsonException("expected an object");
            }
            catch (JsonException)
            {
                return Results.BadRequest(new[] { ValidationFailure("Body", "Request body must be a JSON object", "") });
            }

            var title = GetString(body, "title");
            var downloadUrl = GetString(body, "downloadUrl");
            var magnetUrl = GetString(body, "magnetUrl");

            if (string.IsNullOrWhiteSpace(title))
                return Results.BadRequest(new[] { ValidationFailure("Title", "'Title' must not be empty.", title ?? "") });

            // Sonarr accepts either a direct download link or a magnet; the
            // grab path treats both as the fetch URL for the download client.
            var fetchUrl = !string.IsNullOrWhiteSpace(downloadUrl) ? downloadUrl : magnetUrl;
            if (string.IsNullOrWhiteSpace(fetchUrl))
                return Results.BadRequest(new[] { ValidationFailure("DownloadUrl", "Either 'DownloadUrl' or 'MagnetUrl' must be provided.", "") });

            var protocolRaw = GetString(body, "downloadProtocol") ?? GetString(body, "protocol") ?? "torrent";
            var protocol = protocolRaw.Equals("usenet", StringComparison.OrdinalIgnoreCase) ? "Usenet" : "Torrent";

            // Delay-profile and minimum-age math run off PublishDate; pushed
            // announces are seconds old, so a missing/unparseable date means
            // "just published".
            var publishDate = DateTime.UtcNow;
            var publishDateRaw = GetString(body, "publishDate");
            if (!string.IsNullOrWhiteSpace(publishDateRaw) &&
                DateTimeOffset.TryParse(publishDateRaw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                publishDate = parsedDate.UtcDateTime;
            }

            var indexer = GetString(body, "indexer");
            if (string.IsNullOrWhiteSpace(indexer))
                indexer = "release-push";

            // Magnet links carry the torrent hash inline; extract it so
            // blocklist checks and duplicate-grab tracking work for pushes.
            string? infoHash = null;
            if (!string.IsNullOrWhiteSpace(magnetUrl))
            {
                var hashMatch = Regex.Match(magnetUrl, @"btih:([0-9A-Fa-f]{40}|[A-Za-z2-7]{32})");
                if (hashMatch.Success)
                    infoHash = hashMatch.Groups[1].Value.ToUpperInvariant();
            }

            string? indexerFlags = null;
            var flagBits = GetLong(body, "indexerFlags");
            if (flagBits > 0)
            {
                var names = FlagNames.Where(f => (flagBits & f.Bit) != 0).Select(f => f.Name).ToList();
                if (names.Count > 0)
                    indexerFlags = string.Join(", ", names);
            }

            var release = new ReleaseSearchResult
            {
                Title = title,
                // The fetch URL uniquely identifies the exact payload, so it
                // doubles as the Guid for anti-churn and pending-release dedup.
                Guid = fetchUrl,
                DownloadUrl = fetchUrl,
                InfoUrl = GetString(body, "infoUrl"),
                Indexer = indexer,
                Protocol = protocol,
                Size = GetLong(body, "size"),
                PublishDate = publishDate,
                TorrentInfoHash = infoHash,
                IndexerFlags = indexerFlags
            };

            logger.LogInformation("[Release Push] Received '{Title}' from {Indexer} ({Protocol}, {Size:F2} GB)",
                release.Title, release.Indexer, release.Protocol, release.Size / 1024.0 / 1024.0 / 1024.0);

            var outcome = await rssSyncService.ProcessPushedReleaseAsync(release, cancellationToken);

            if (outcome.Grabbed)
            {
                logger.LogInformation("[Release Push] ✓ Grabbed '{Title}' for '{Event}'",
                    release.Title, outcome.MatchedEventTitle);
            }
            else if (outcome.Pending)
            {
                logger.LogInformation("[Release Push] Held '{Title}' for '{Event}': {Reason}",
                    release.Title, outcome.MatchedEventTitle, string.Join(", ", outcome.Rejections));
            }
            else
            {
                logger.LogInformation("[Release Push] Rejected '{Title}': {Reasons}",
                    release.Title, string.Join(", ", outcome.Rejections));
            }

            return Results.Ok(new[]
            {
                new
                {
                    title = release.Title,
                    infoUrl = release.InfoUrl,
                    downloadUrl = release.DownloadUrl,
                    size = release.Size,
                    indexer = release.Indexer,
                    downloadProtocol = protocolRaw.ToLowerInvariant(),
                    protocol = protocolRaw.ToLowerInvariant(),
                    publishDate = release.PublishDate.ToString("o"),
                    approved = outcome.Grabbed,
                    rejected = !outcome.Grabbed && !outcome.Pending,
                    temporarilyRejected = outcome.Pending,
                    rejections = outcome.Rejections
                }
            });
        });

        return app;
    }

    /// <summary>
    /// Case-insensitive string property lookup so both camelCase pushers and
    /// PascalCase test tools work.
    /// </summary>
    private static string? GetString(JsonElement body, string name)
    {
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }
        return null;
    }

    private static long GetLong(JsonElement body, string name)
    {
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt64(out var value))
                return value;
        }
        return 0;
    }

    private static object ValidationFailure(string propertyName, string errorMessage, string attemptedValue) => new
    {
        propertyName,
        errorMessage,
        errorCode = "NotEmptyValidator",
        attemptedValue,
        severity = "error"
    };
}
