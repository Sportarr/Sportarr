using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;

namespace Sportarr.Api.Services;

/// <summary>
/// Library imports used to store a custom format score of 0, so every release
/// with a positive score looked like an upgrade over the file and got grabbed
/// again. This gives each such file the score its name earns against its
/// event's quality profile, the same score an import stores today.
/// </summary>
public static class LibraryFileFormatScores
{
    /// <summary>
    /// Rescore library files stored at 0. A file that really earns 0 stays at 0, so
    /// running this again changes nothing. Returns how many files changed.
    /// </summary>
    public static async Task<int> RescoreUnscoredAsync(
        SportarrDbContext db, CustomFormatService customFormatService, CancellationToken ct = default)
    {
        var profiles = await db.QualityProfiles.AsNoTracking().ToListAsync(ct);
        if (!profiles.Any(p => p.FormatItems?.Count > 0)) return 0;

        var files = await db.EventFiles
            .Include(f => f.Event).ThenInclude(e => e!.League)
            // A grabbed file's score came from its release at grab time, and a
            // DVR recording's from the DVR's probe title.
            .Where(f => f.CustomFormatScore == 0 && f.ReleaseTitle == null && f.Source != "IPTV")
            .ToListAsync(ct);
        if (files.Count == 0) return 0;

        var formats = await db.CustomFormats.AsNoTracking().ToListAsync(ct);

        var changed = 0;
        foreach (var file in files)
        {
            if (file.Event == null) continue;
            var score = customFormatService.ScoreName(
                file.OriginalTitle ?? Path.GetFileNameWithoutExtension(file.FilePath),
                RssSyncService.ResolveQualityProfile(file.Event, profiles), formats);
            if (score == 0) continue;

            file.CustomFormatScore = score;
            changed++;
        }

        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }
}
