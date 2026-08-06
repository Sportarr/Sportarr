using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Services;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class RootFolderAndNotificationEndpoints
{
    public static IEndpointRouteBuilder MapRootFolderAndNotificationEndpoints(this IEndpointRouteBuilder app)
    {
// API: Root Folders Management
app.MapGet("/api/rootfolder", async (SportarrDbContext db, DiskSpaceService diskSpaceService) =>
{
    var folders = await db.RootFolders.ToListAsync();
    // Accessible/FreeSpace/TotalSpace are NotMapped — populate them live
    // here so the UI shows current numbers instead of whatever the row
    // had when it was last read.
    diskSpaceService.RefreshLiveState(folders);
    return Results.Ok(folders);
});

app.MapPost("/api/rootfolder", async (RootFolder folder, SportarrDbContext db, DiskSpaceService diskSpaceService, ILogger<Program> logger) =>
{
    if (folder == null || string.IsNullOrWhiteSpace(folder.Path))
    {
        return Results.BadRequest(new { error = "Path is required" });
    }

    // Normalize to a canonical absolute path so we don't accept the same
    // folder twice via different spellings (./media vs /data/media etc).
    string normalized;
    try
    {
        normalized = Path.GetFullPath(folder.Path);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Invalid path: {ex.Message}" });
    }
    folder.Path = normalized;

    if (await db.RootFolders.AnyAsync(f => f.Path == folder.Path))
    {
        return Results.BadRequest(new { error = "Root folder already exists" });
    }

    var validation = RootFolderValidator.Validate(folder.Path);
    if (!validation.IsValid)
    {
        logger.LogInformation("[ROOTFOLDER] Rejected {Path}: {Reason}", folder.Path, validation.Reason);
        return Results.BadRequest(new { error = validation.Reason });
    }

    db.RootFolders.Add(folder);
    await db.SaveChangesAsync();

    // Populate live state on the response so the client doesn't need to
    // round-trip through GET to display free space.
    diskSpaceService.RefreshLiveState(new[] { folder });
    return Results.Created($"/api/rootfolder/{folder.Id}", folder);
});

// PUT /api/rootfolder/{id} — update the editable fields on an existing
// root folder. Currently the user-editable knobs are the per-root
// defaults (DefaultQualityProfileId and DefaultDownloadClientCategory);
// Path itself stays immutable because changing it would orphan every
// FilePath stored under it. To change Path, the right answer is to add
// a new root folder, run the league move flow, and delete the old one.
app.MapPut("/api/rootfolder/{id:int}", async (int id, RootFolder updates, SportarrDbContext db, DiskSpaceService diskSpaceService, ILogger<Program> logger) =>
{
    var folder = await db.RootFolders.FindAsync(id);
    if (folder is null) return Results.NotFound();
    if (updates is null) return Results.BadRequest(new { error = "Body required" });

    folder.DefaultQualityProfileId = updates.DefaultQualityProfileId;
    folder.DefaultDownloadClientCategory = string.IsNullOrWhiteSpace(updates.DefaultDownloadClientCategory)
        ? null
        : updates.DefaultDownloadClientCategory.Trim();

    await db.SaveChangesAsync();
    logger.LogInformation("[ROOTFOLDER] Updated defaults for {Id} ({Path}) — profile={Profile}, category={Category}",
        folder.Id, folder.Path, folder.DefaultQualityProfileId, folder.DefaultDownloadClientCategory ?? "(none)");

    diskSpaceService.RefreshLiveState(new[] { folder });
    return Results.Ok(folder);
});

// GET /api/rootfolder/{id}/unmappedfolders — list direct subfolders
// of the given root that don't correspond to any existing league. The
// upstream library-import flow uses this to surface "you have stuff
// on disk Sportarr doesn't know about" candidates so the user can
// adopt them in one click instead of hand-typing the path. Filesystem
// metadata folders (recycle bins, system volume info, lost+found) are
// always excluded.
app.MapGet("/api/rootfolder/{id:int}/unmappedfolders", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    var folder = await db.RootFolders.FindAsync(id);
    if (folder is null) return Results.NotFound();

    if (!Directory.Exists(folder.Path))
    {
        return Results.Ok(new
        {
            rootFolderId = folder.Id,
            rootFolderPath = folder.Path,
            accessible = false,
            unmapped = Array.Empty<object>(),
        });
    }

    // Build the league-name set the user has claimed. We normalize on
    // both sides (lowercase + strip illegal chars in the same way
    // FileNamingService does) so a league called "UFC" matches a folder
    // called "ufc/" or "UFC " etc. without false positives.
    var leagueNames = await db.Leagues
        .Select(l => l.Name)
        .ToListAsync();
    var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var name in leagueNames)
    {
        var normalized = NormalizeFolderName(name);
        if (!string.IsNullOrEmpty(normalized))
            claimed.Add(normalized);
    }

    var unmapped = new List<object>();
    try
    {
        foreach (var sub in Directory.EnumerateDirectories(folder.Path))
        {
            var name = Path.GetFileName(sub);
            if (string.IsNullOrEmpty(name)) continue;
            if (IsExcludedSubfolder(name)) continue;
            if (claimed.Contains(NormalizeFolderName(name))) continue;
            unmapped.Add(new
            {
                name,
                path = sub,
                relativePath = name,
            });
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "[ROOTFOLDER] Failed to enumerate {Path} for unmapped folders", folder.Path);
        return Results.Problem(detail: ex.Message, statusCode: 500, title: "Failed to enumerate root folder");
    }

    var sorted = unmapped
        .Cast<dynamic>()
        .OrderBy(u => (string)u.name, StringComparer.OrdinalIgnoreCase)
        .ToList<object>();

    return Results.Ok(new
    {
        rootFolderId = folder.Id,
        rootFolderPath = folder.Path,
        accessible = true,
        unmapped = sorted,
    });

    static string NormalizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        // Strip the same set of characters FileNamingService.CleanFileName
        // strips, then lowercase + collapse internal whitespace so the
        // comparison is invariant to user-driven naming variations.
        var cleaned = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch == ':' || ch == '*' || ch == '?' || ch == '"' || ch == '<' || ch == '>' || ch == '|')
                continue;
            cleaned.Append(ch);
        }
        var s = cleaned.ToString().Trim().ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
    }

    static bool IsExcludedSubfolder(string name)
    {
        return name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)
            || name.Equals("lost+found", StringComparison.Ordinal)
            || name.Equals(".Trash", StringComparison.Ordinal)
            || name.Equals(".Trashes", StringComparison.Ordinal)
            || name.StartsWith(".", StringComparison.Ordinal);
    }
});

app.MapDelete("/api/rootfolder/{id:int}", async (int id, bool? force, SportarrDbContext db, ILogger<Program> logger) =>
{
    var folder = await db.RootFolders.FindAsync(id);
    if (folder is null) return Results.NotFound();

    // Block delete when leagues are still bound to this root folder. The
    // FK constraint would already refuse to cascade, but surfacing a 409
    // with the offending league IDs gives the UI something to render so
    // the user can either rebind those leagues or pass force=true to
    // detach the binding (set RootFolderId=null) before deleting.
    var boundLeagues = await db.Leagues
        .Where(l => l.RootFolderId == id)
        .Select(l => new { l.Id, l.Name })
        .ToListAsync();

    if (boundLeagues.Count > 0)
    {
        if (force == true)
        {
            logger.LogWarning("[ROOTFOLDER] Force-deleting root folder {Id} ({Path}) — detaching {Count} bound leagues first.",
                id, folder.Path, boundLeagues.Count);
            // Detach bindings: legacy fallback (free-space heuristic) will
            // pick a destination at next import, mirroring pre-binding
            // behavior. The user has been warned via the UI.
            await db.Leagues
                .Where(l => l.RootFolderId == id)
                .ExecuteUpdateAsync(setter => setter.SetProperty(l => l.RootFolderId, (int?)null));
        }
        else
        {
            logger.LogInformation("[ROOTFOLDER] Refusing delete of root folder {Id} ({Path}) — {Count} leagues are still bound to it.",
                id, folder.Path, boundLeagues.Count);
            return Results.Conflict(new
            {
                error = "Root folder is still bound to one or more leagues.",
                folder = new { folder.Id, folder.Path },
                leagues = boundLeagues,
                hint = "Rebind the leagues to a different root folder, or call DELETE /api/rootfolder/{id}?force=true to detach the bindings before deleting.",
            });
        }
    }

    db.RootFolders.Remove(folder);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Filesystem Browser (for root folder selection)
app.MapGet("/api/filesystem", (string? path, bool? includeFiles) =>
{
    try
    {
        // Default to root drives if no path provided
        if (string.IsNullOrEmpty(path))
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new
                {
                    type = "drive",
                    name = d.Name,
                    path = d.RootDirectory.FullName,
                    freeSpace = d.AvailableFreeSpace,
                    totalSpace = d.TotalSize
                })
                .ToList();

            return Results.Ok(new
            {
                parent = (string?)null,
                directories = drives
            });
        }

        // Ensure path exists
        if (!Directory.Exists(path))
        {
            return Results.BadRequest(new { error = "Directory does not exist" });
        }

        var directoryInfo = new DirectoryInfo(path);
        var parent = directoryInfo.Parent?.FullName;

        // Get subdirectories
        var directories = directoryInfo.GetDirectories()
            .Where(d => !d.Attributes.HasFlag(FileAttributes.System) && !d.Attributes.HasFlag(FileAttributes.Hidden))
            .Select(d => new
            {
                type = "folder",
                name = d.Name,
                path = d.FullName,
                lastModified = d.LastWriteTimeUtc
            })
            .OrderBy(d => d.name)
            .ToList();

        // Optionally include files
        object? files = null;
        if (includeFiles == true)
        {
            files = directoryInfo.GetFiles()
                .Where(f => !f.Attributes.HasFlag(FileAttributes.System) && !f.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(f => new
                {
                    type = "file",
                    name = f.Name,
                    path = f.FullName,
                    size = f.Length,
                    lastModified = f.LastWriteTimeUtc
                })
                .OrderBy(f => f.name)
                .ToList();
        }

        return Results.Ok(new
        {
            parent,
            directories,
            files
        });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.BadRequest(new { error = "Access denied to this directory" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API: Notifications Management
app.MapGet("/api/notification", async (SportarrDbContext db) =>
{
    var notifications = await db.Notifications.ToListAsync();
    return Results.Ok(notifications);
});

app.MapPost("/api/notification", async (Notification notification, SportarrDbContext db) =>
{
    notification.Created = DateTime.UtcNow;
    notification.LastModified = DateTime.UtcNow;
    db.Notifications.Add(notification);
    await db.SaveChangesAsync();
    return Results.Created($"/api/notification/{notification.Id}", notification);
});

app.MapPut("/api/notification/{id:int}", async (int id, Notification updatedNotification, SportarrDbContext db) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    notification.Name = updatedNotification.Name;
    notification.Implementation = updatedNotification.Implementation;
    notification.Enabled = updatedNotification.Enabled;
    notification.ConfigJson = updatedNotification.ConfigJson;
    notification.Tags = updatedNotification.Tags;
    notification.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(notification);
});

app.MapDelete("/api/notification/{id:int}", async (int id, SportarrDbContext db) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    db.Notifications.Remove(notification);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Test Notification
app.MapPost("/api/notification/{id:int}/test", async (int id, SportarrDbContext db, NotificationService notificationService) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    var (success, message) = await notificationService.TestNotificationAsync(notification);
    await db.SaveChangesAsync();

    return success
        ? Results.Ok(new { success = true, message })
        : Results.BadRequest(new { success = false, message });
});

// API: Test Notification with payload (for testing before saving)
app.MapPost("/api/notification/test", async (Notification notification, NotificationService notificationService) =>
{
    var (success, message) = await notificationService.TestNotificationAsync(notification);

    return success
        ? Results.Ok(new { success = true, message })
        : Results.BadRequest(new { success = false, message });
});

        return app;
    }
}
