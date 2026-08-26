using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

/// <summary>
/// EventFile metadata editor endpoints. Mirrors Sonarr's three-endpoint shape
/// (single, editor, bulk) so the frontend modal logic can match the established
/// pattern, with Sportarr-only additions (Codec, Source, IndexerFlags, Languages).
///
/// Quality recalculation: every endpoint that mutates Quality runs the new value
/// through ReleaseEvaluator.CalculateQualityScoreFromName so the stored score
/// stays consistent with the displayed string. Frontend doesn't need to compute it.
/// </summary>
public static class EventFileEditorEndpoints
{
    /// <summary>
    /// Editable fields. All optional — null means "leave the existing value alone".
    /// Languages is a special case: a non-null empty list explicitly clears the
    /// list, matching Sonarr's editor semantics.
    /// </summary>
    public class EventFileEditRequest
    {
        public string? Quality { get; set; }
        public string? Source { get; set; }
        public string? Codec { get; set; }
        public string? ReleaseGroup { get; set; }
        public string? OriginalTitle { get; set; }
        public List<string>? Languages { get; set; }
        public string? IndexerFlags { get; set; }
        public string? PartName { get; set; }
        public int? PartNumber { get; set; }
    }

    public class EventFileEditorRequest : EventFileEditRequest
    {
        public List<int> EventFileIds { get; set; } = new();
    }

    public class EventFileBulkItem : EventFileEditRequest
    {
        public int Id { get; set; }
    }

    public static IEndpointRouteBuilder MapEventFileEditorEndpoints(this IEndpointRouteBuilder app)
    {
        // ----------------------------------------------------------------
        // PUT /api/event-files/{id}
        // Single-file edit. Returns the updated EventFile DTO.
        // ----------------------------------------------------------------
        app.MapPut("/api/event-files/{id:int}", async (
            int id,
            EventFileEditRequest request,
            SportarrDbContext db,
            ILogger<Program> logger) =>
        {
            var file = await db.EventFiles.FirstOrDefaultAsync(f => f.Id == id);
            if (file == null)
            {
                logger.LogWarning("[EventFile Editor] PUT /event-files/{Id}: not found", id);
                return Results.NotFound(new { error = $"EventFile {id} not found" });
            }

            // Log incoming patch and pre-edit state. Critical for diagnosing
            // "save toast appeared but file looks unchanged" reports.
            logger.LogInformation(
                "[EventFile Editor] PUT /event-files/{Id} request: Quality='{ReqQ}', Source='{ReqS}', Codec='{ReqC}', ReleaseGroup='{ReqRG}', OriginalTitle='{ReqOT}', Languages={ReqL}, IndexerFlags='{ReqIF}', PartName='{ReqPN}', PartNumber={ReqPNum}",
                id,
                request.Quality ?? "(unset)",
                request.Source ?? "(unset)",
                request.Codec ?? "(unset)",
                request.ReleaseGroup ?? "(unset)",
                request.OriginalTitle ?? "(unset)",
                request.Languages == null ? "(unset)" : $"[{string.Join(",", request.Languages)}]",
                request.IndexerFlags ?? "(unset)",
                request.PartName ?? "(unset)",
                request.PartNumber?.ToString() ?? "(unset)");

            logger.LogInformation(
                "[EventFile Editor] Pre-edit  file {Id}: Quality='{Q}', Source='{S}', Codec='{C}', ReleaseGroup='{RG}'",
                id, file.Quality ?? "null", file.Source ?? "null", file.Codec ?? "null", file.ReleaseGroup ?? "null");

            ApplyEdits(file, request);
            var rowsWritten = await db.SaveChangesAsync();

            logger.LogInformation(
                "[EventFile Editor] Post-edit file {Id} ({Rows} rows written): Quality='{Q}', Source='{S}', Codec='{C}', ReleaseGroup='{RG}', QualityScore={QS}",
                id, rowsWritten, file.Quality ?? "null", file.Source ?? "null", file.Codec ?? "null", file.ReleaseGroup ?? "null", file.QualityScore);

            return Results.Ok(EventFileResponse.FromEventFile(file));
        });

        // ----------------------------------------------------------------
        // PUT /api/event-files/editor
        // Apply ONE set of values to MANY ids. Sonarr-equivalent shape.
        // ----------------------------------------------------------------
        app.MapPut("/api/event-files/editor", async (
            EventFileEditorRequest request,
            SportarrDbContext db,
            ILogger<Program> logger) =>
        {
            if (request.EventFileIds == null || request.EventFileIds.Count == 0)
                return Results.BadRequest(new { error = "eventFileIds is required and must contain at least one id" });

            var files = await db.EventFiles.Where(f => request.EventFileIds.Contains(f.Id)).ToListAsync();
            if (files.Count == 0)
            {
                logger.LogWarning("[EventFile Editor] PUT /event-files/editor: no matching files for ids {Ids}",
                    string.Join(",", request.EventFileIds));
                return Results.NotFound(new { error = "No matching EventFiles found" });
            }

            logger.LogInformation(
                "[EventFile Editor] PUT /event-files/editor request for {Count} ids ({Ids}): Quality='{Q}', Source='{S}', Codec='{C}', ReleaseGroup='{RG}', OriginalTitle='{OT}', Languages={L}, IndexerFlags='{IF}', PartName='{PN}', PartNumber={PNum}",
                files.Count, string.Join(",", request.EventFileIds),
                request.Quality ?? "(unset)",
                request.Source ?? "(unset)",
                request.Codec ?? "(unset)",
                request.ReleaseGroup ?? "(unset)",
                request.OriginalTitle ?? "(unset)",
                request.Languages == null ? "(unset)" : $"[{string.Join(",", request.Languages)}]",
                request.IndexerFlags ?? "(unset)",
                request.PartName ?? "(unset)",
                request.PartNumber?.ToString() ?? "(unset)");

            foreach (var file in files)
            {
                ApplyEdits(file, request);
            }
            var rowsWritten = await db.SaveChangesAsync();

            logger.LogInformation("[EventFile Editor] Bulk-applied edits to {Count} files ({Rows} rows written, ids: {Ids})",
                files.Count, rowsWritten, string.Join(",", files.Select(f => f.Id)));

            return Results.Ok(files.Select(EventFileResponse.FromEventFile).ToList());
        });

        // ----------------------------------------------------------------
        // PUT /api/event-files/bulk
        // List of {id, fields} — different values per file. The advantage
        // over /editor for sports use cases: each part of a multi-part
        // event can come from a different release group / quality / etc.
        // ----------------------------------------------------------------
        app.MapPut("/api/event-files/bulk", async (
            List<EventFileBulkItem> items,
            SportarrDbContext db,
            ILogger<Program> logger) =>
        {
            if (items == null || items.Count == 0)
                return Results.BadRequest(new { error = "Body must be a non-empty list of edit items" });

            var ids = items.Select(i => i.Id).ToList();
            var files = await db.EventFiles.Where(f => ids.Contains(f.Id)).ToListAsync();
            var byId = files.ToDictionary(f => f.Id);

            var updated = new List<EventFile>();
            var missing = new List<int>();

            foreach (var item in items)
            {
                if (!byId.TryGetValue(item.Id, out var file))
                {
                    missing.Add(item.Id);
                    continue;
                }
                ApplyEdits(file, item);
                updated.Add(file);
            }

            await db.SaveChangesAsync();

            logger.LogInformation("[EventFile Editor] Per-id bulk edit: {Updated} updated, {Missing} missing",
                updated.Count, missing.Count);

            return Results.Ok(new
            {
                updated = updated.Select(EventFileResponse.FromEventFile).ToList(),
                missingIds = missing
            });
        });

        // ----------------------------------------------------------------
        // GET /api/event-files/known-qualities?leagueId=...&eventId=...
        // Surface the canonical quality strings, sources, codecs, indexer
        // flags, plus DB-mined release groups and (when leagueId/eventId
        // is passed) the league-specific part list for PartName/PartNumber
        // dropdowns. Optional params let the same endpoint serve every
        // editor use case without an explosion of routes.
        // ----------------------------------------------------------------
        app.MapGet("/api/event-files/known-qualities", async (
            int? leagueId,
            int? eventId,
            SportarrDbContext db) =>
        {
            // Curated, user-friendly list of canonical quality names. Ordered
            // by ascending quality so dropdowns read low-to-high.
            var qualities = new[]
            {
                "Unknown", "SDTV", "DVD",
                "WEBDL-480p", "WEBRip-480p", "Bluray-480p",
                "HDTV-720p", "WEBDL-720p", "WEBRip-720p", "Bluray-720p",
                "HDTV-1080p", "WEBDL-1080p", "WEBRip-1080p", "Bluray-1080p", "Bluray-1080p Remux",
                "HDTV-2160p", "WEBDL-2160p", "WEBRip-2160p", "Bluray-2160p", "Bluray-2160p Remux",
                "Raw-HD"
            };

            // Source list covers both the parser's canonical normalized form
            // (WEBDL, BLURAY) and the dash-form variants Sonarr scenes use
            // (WEB-DL, Blu-Ray) so users can pick whichever matches their
            // existing files without falling into Custom mode.
            var sources = new[]
            {
                "WEBDL", "WEB-DL", "WEBRip", "BLURAY", "Blu-Ray",
                "BDRip", "HDTV", "PDTV", "DVDRIP", "DVD", "RAWHD"
            };

            // Codec list covers both Sportarr's canonical name (x264 / x265)
            // and the common alternates ffprobe / scene names produce (H.264,
            // H.265, HEVC, AVC). Same dropdown, no Custom-mode trip.
            var codecs = new[]
            {
                "x264", "H.264", "AVC",
                "x265", "H.265", "HEVC",
                "AV1", "VP9", "MPEG2", "XviD", "DivX"
            };

            var indexerFlags = new[] { "Freeleech", "Halfleech", "Internal", "Scene", "Nuked", "DoubleUpload" };

            // Pull existing release groups from the database so the dropdown
            // surfaces the user's actual library, not a hard-coded guess.
            // Order by frequency so the most-used groups appear first.
            var releaseGroups = await db.EventFiles
                .Where(f => f.ReleaseGroup != null && f.ReleaseGroup != "")
                .GroupBy(f => f.ReleaseGroup!)
                .Select(g => new { Group = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(50)
                .Select(g => g.Group)
                .ToListAsync();

            // League-aware part list. If eventId is passed we look up the
            // event's title (so UFC PPV vs Fight Night returns the right
            // segment set); falling back to leagueId resolves to the league
            // name. Without either we return an empty list — the editor
            // falls back to free-text PartName/PartNumber for those events.
            var parts = new List<string>();
            int maxPartNumber = 0;
            string? sport = null;
            string? leagueName = null;
            string? eventTitle = null;

            if (eventId.HasValue)
            {
                var evt = await db.Events
                    .Include(e => e.League)
                    .Where(e => e.Id == eventId.Value)
                    .Select(e => new { e.Sport, e.Title, LeagueName = e.League != null ? e.League.Name : null })
                    .FirstOrDefaultAsync();
                if (evt != null)
                {
                    sport = evt.Sport;
                    leagueName = evt.LeagueName;
                    eventTitle = evt.Title;
                }
            }

            // Fall back to the league whenever the event did not answer. The
            // league branch only ran when no event id was supplied at all, so
            // passing both and having the event lookup come up empty, which is
            // what happens for a deleted event or a file not tied to one, left
            // the part selector blank even though the caller had named a
            // perfectly good league.
            if (string.IsNullOrEmpty(sport) && leagueId.HasValue)
            {
                var lg = await db.Leagues
                    .Where(l => l.Id == leagueId.Value)
                    .Select(l => new { l.Sport, l.Name })
                    .FirstOrDefaultAsync();
                if (lg != null)
                {
                    sport = lg.Sport;
                    leagueName = lg.Name;
                }
            }

            if (!string.IsNullOrEmpty(sport))
            {
                // EventPartDetector.GetAvailableSegments returns ["Full Event", ...]
                // for fighting sports, [] for others. We strip "Full Event" because
                // PartName=null already represents the full-event case in the model.
                var segs = EventPartDetector.GetAvailableSegments(sport, eventTitle, leagueName);
                parts = segs.Where(s => !s.Equals("Full Event", StringComparison.OrdinalIgnoreCase)).ToList();
                maxPartNumber = parts.Count;
            }

            return Results.Ok(new
            {
                qualities,
                sources,
                codecs,
                indexerFlags,
                releaseGroups,
                parts,
                maxPartNumber
            });
        });

        return app;
    }

    /// <summary>
    /// Apply non-null fields from the request to the file. Recomputes QualityScore
    /// when Quality is touched so the stored score stays in sync.
    /// Public so unit tests can exercise the merge logic without spinning up the
    /// full HTTP pipeline.
    /// </summary>
    public static void ApplyEdits(EventFile file, EventFileEditRequest req)
    {
        if (req.Quality != null)
        {
            file.Quality = req.Quality;
            file.QualityScore = ReleaseEvaluator.CalculateQualityScoreFromName(req.Quality);
        }
        if (req.Source != null) file.Source = req.Source;
        if (req.Codec != null) file.Codec = req.Codec;
        if (req.ReleaseGroup != null) file.ReleaseGroup = req.ReleaseGroup;
        if (req.OriginalTitle != null) file.OriginalTitle = req.OriginalTitle;
        if (req.Languages != null) file.Languages = req.Languages;
        if (req.IndexerFlags != null) file.IndexerFlags = req.IndexerFlags;
        // An empty part name means "this is the whole event". Null still means
        // "leave it as it is", which is what every other field here does, but
        // that left no way at all to undo a part once one had been set: the
        // stale part stuck to the file and went on shaping its name and how it
        // was presented in the library.
        if (req.PartName != null)
        {
            var trimmed = req.PartName.Trim();
            if (trimmed.Length == 0)
            {
                file.PartName = null;
                file.PartNumber = null;
            }
            else
            {
                file.PartName = trimmed;
            }
        }

        // A part number of zero clears it, for the same reason.
        if (req.PartNumber.HasValue)
        {
            file.PartNumber = req.PartNumber.Value <= 0 ? null : req.PartNumber;
            if (file.PartNumber == null && req.PartName == null)
            {
                file.PartName = null;
            }
        }
    }
}

