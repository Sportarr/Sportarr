using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Result type for move operations. Status maps cleanly to HTTP status
/// codes at the endpoint layer so the controller stays thin.
/// </summary>
public class LeagueMoveResult
{
    public bool Success { get; set; }
    public LeagueMoveStatus Status { get; set; }
    public string? Message { get; set; }
    public int LeagueId { get; set; }
    public int? NewRootFolderId { get; set; }
    public int FilesMoved { get; set; }
    public string? OldPath { get; set; }
    public string? NewPath { get; set; }
}

public enum LeagueMoveStatus
{
    Ok,
    LeagueNotFound,
    RootFolderNotFound,
    RootFolderInaccessible,
    SameRootFolder,
    SourceFolderAmbiguous,
    DestinationExists,
    MoveFailed,
}

/// <summary>
/// Moves a league's media folder from one RootFolder to another, updating
/// the league's RootFolderId binding and rewriting every EventFile.FilePath
/// + Event.FilePath under it. Modeled after the upstream MoveSeriesService
/// in the same family of -arr apps: when moveFiles is true the on-disk
/// folder is moved before the DB updates land; on any failure the move is
/// rolled back so we don't end up with a DB pointing at one place and
/// files sitting in another.
/// </summary>
public class LeagueMoveService
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<LeagueMoveService> _logger;

    public LeagueMoveService(SportarrDbContext db, ILogger<LeagueMoveService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Move a single league to a new root folder. When moveFiles is true,
    /// the on-disk league folder is moved and every FilePath in the DB is
    /// rewritten. When moveFiles is false, only the binding changes — the
    /// caller is taking responsibility for re-organizing files manually.
    /// </summary>
    public async Task<LeagueMoveResult> MoveLeagueAsync(int leagueId, int newRootFolderId, bool moveFiles)
    {
        var league = await _db.Leagues.FirstOrDefaultAsync(l => l.Id == leagueId);
        if (league == null)
        {
            return new LeagueMoveResult { Status = LeagueMoveStatus.LeagueNotFound, LeagueId = leagueId };
        }

        var newRoot = await _db.RootFolders.FirstOrDefaultAsync(rf => rf.Id == newRootFolderId);
        if (newRoot == null)
        {
            return new LeagueMoveResult { Status = LeagueMoveStatus.RootFolderNotFound, LeagueId = leagueId, NewRootFolderId = newRootFolderId };
        }
        if (!Directory.Exists(newRoot.Path))
        {
            return new LeagueMoveResult { Status = LeagueMoveStatus.RootFolderInaccessible, LeagueId = leagueId, NewRootFolderId = newRootFolderId, Message = newRoot.Path };
        }

        if (league.RootFolderId == newRootFolderId)
        {
            return new LeagueMoveResult { Status = LeagueMoveStatus.SameRootFolder, LeagueId = leagueId, NewRootFolderId = newRootFolderId, Success = true };
        }

        // Pull every EventFile + Event whose FilePath we may need to rewrite.
        // We rewrite FilePath on both Events (denormalized convenience field)
        // and EventFiles (the canonical record per file on disk). Anything
        // not under the detected source folder is left alone — it was likely
        // imported under a different root before the binding existed and
        // would need a separate manual cleanup.
        var eventFiles = await _db.EventFiles
            .Include(ef => ef.Event)
            .Where(ef => ef.Event != null && ef.Event.LeagueId == leagueId && ef.Exists && ef.FilePath != null)
            .ToListAsync();

        // Fast path: nothing to move (no files, OR caller chose binding-only).
        if (!moveFiles || eventFiles.Count == 0)
        {
            league.RootFolderId = newRootFolderId;
            await _db.SaveChangesAsync();
            return new LeagueMoveResult
            {
                Success = true,
                Status = LeagueMoveStatus.Ok,
                LeagueId = leagueId,
                NewRootFolderId = newRootFolderId,
                NewPath = newRoot.Path,
                FilesMoved = 0,
                Message = moveFiles
                    ? "Binding updated; no on-disk files needed to move."
                    : "Binding updated; on-disk files left in place at the caller's request."
            };
        }

        // Detect the on-disk source: the longest configured RootFolder that
        // is a prefix of every existing FilePath. If files are scattered
        // across multiple roots we refuse to move — the user has to clean
        // that up manually. (Phase 3 will surface that as a Health warning.)
        var allRoots = await _db.RootFolders.ToListAsync();
        var roots = allRoots
            .Select(rf => rf.Path)
            .OrderByDescending(p => p.Length)
            .ToList();

        string? sourceRoot = null;
        foreach (var ef in eventFiles)
        {
            var match = roots.FirstOrDefault(r => IsUnderRoot(ef.FilePath!, r));
            if (match == null)
            {
                _logger.LogWarning(
                    "[League Move] {Title} ({EventFileId}) lives outside any configured root folder ({Path}); aborting move.",
                    ef.Event?.Title, ef.Id, ef.FilePath);
                return new LeagueMoveResult
                {
                    Status = LeagueMoveStatus.SourceFolderAmbiguous,
                    LeagueId = leagueId,
                    Message = $"Event file {ef.Id} ({ef.FilePath}) is outside every configured root folder. Move can't proceed until that's resolved manually."
                };
            }
            if (sourceRoot == null)
            {
                sourceRoot = match;
            }
            else if (!string.Equals(sourceRoot, match, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[League Move] League {LeagueId} has files under multiple roots ({Root1}, {Root2}); aborting move.",
                    leagueId, sourceRoot, match);
                return new LeagueMoveResult
                {
                    Status = LeagueMoveStatus.SourceFolderAmbiguous,
                    LeagueId = leagueId,
                    Message = "League has files spread across multiple root folders. Run the Reorganize action to consolidate them under a single root, then try the move again."
                };
            }
        }

        if (sourceRoot == null)
        {
            // Defensive — shouldn't be reachable after the loop above.
            return new LeagueMoveResult { Status = LeagueMoveStatus.SourceFolderAmbiguous, LeagueId = leagueId };
        }

        // Now find the league subfolder name. Every file should share the
        // same first path component under the source root. If they don't,
        // we refuse — that means the league spans multiple subfolders
        // (e.g. CreateLeagueFolders was off when the files were imported).
        string? leagueSubfolder = null;
        foreach (var ef in eventFiles)
        {
            var sub = FirstSegmentUnderRoot(ef.FilePath!, sourceRoot);
            if (sub == null)
            {
                // File sits directly under the root with no league folder.
                return new LeagueMoveResult
                {
                    Status = LeagueMoveStatus.SourceFolderAmbiguous,
                    LeagueId = leagueId,
                    Message = $"Event file {ef.FilePath} sits directly under the root folder with no league subdirectory. Enable league folders and run reorganize before moving."
                };
            }
            if (leagueSubfolder == null)
            {
                leagueSubfolder = sub;
            }
            else if (!string.Equals(leagueSubfolder, sub, StringComparison.OrdinalIgnoreCase))
            {
                return new LeagueMoveResult
                {
                    Status = LeagueMoveStatus.SourceFolderAmbiguous,
                    LeagueId = leagueId,
                    Message = $"League files are split across multiple subdirectories ({leagueSubfolder} and {sub}). Reorganize first."
                };
            }
        }

        var sourcePath = Path.Combine(sourceRoot, leagueSubfolder!);
        var destPath = Path.Combine(newRoot.Path, leagueSubfolder!);

        if (!Directory.Exists(sourcePath))
        {
            // Nothing on disk anyway — DB-only update.
            league.RootFolderId = newRootFolderId;
            await _db.SaveChangesAsync();
            return new LeagueMoveResult
            {
                Success = true,
                Status = LeagueMoveStatus.Ok,
                LeagueId = leagueId,
                NewRootFolderId = newRootFolderId,
                OldPath = sourcePath,
                NewPath = newRoot.Path,
                FilesMoved = 0,
                Message = "Source folder did not exist; binding updated."
            };
        }

        if (Directory.Exists(destPath))
        {
            return new LeagueMoveResult
            {
                Status = LeagueMoveStatus.DestinationExists,
                LeagueId = leagueId,
                Message = $"Destination already exists: {destPath}. Resolve the collision before moving."
            };
        }

        _logger.LogInformation("[League Move] Moving league {LeagueId} ({Name}) folder: {Src} -> {Dst}",
            league.Id, league.Name, sourcePath, destPath);

        bool moved = false;
        try
        {
            MoveDirectoryAcrossFilesystems(sourcePath, destPath);
            moved = true;

            // Rewrite every FilePath that lived under the moved tree. We
            // also rewrite Event.FilePath where it points at the moved file.
            var prefixOld = sourcePath;
            var prefixNew = destPath;
            var events = await _db.Events.Where(e => e.LeagueId == leagueId).ToListAsync();
            foreach (var ev in events)
            {
                if (ev.FilePath != null && IsUnderPath(ev.FilePath, prefixOld))
                {
                    ev.FilePath = RewritePath(ev.FilePath, prefixOld, prefixNew);
                }
            }
            foreach (var ef in eventFiles)
            {
                if (ef.FilePath != null && IsUnderPath(ef.FilePath, prefixOld))
                {
                    ef.FilePath = RewritePath(ef.FilePath, prefixOld, prefixNew);
                }
            }

            league.RootFolderId = newRootFolderId;
            await _db.SaveChangesAsync();

            return new LeagueMoveResult
            {
                Success = true,
                Status = LeagueMoveStatus.Ok,
                LeagueId = leagueId,
                NewRootFolderId = newRootFolderId,
                OldPath = sourcePath,
                NewPath = destPath,
                FilesMoved = eventFiles.Count,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[League Move] Failed for league {LeagueId}", leagueId);

            // Roll the on-disk move back if we successfully moved before
            // the DB update failed. If we get a second exception during
            // rollback, log loudly — at that point the user has to fix
            // the FS by hand, but at least the DB still points at the old
            // root so future imports use the right place.
            if (moved && Directory.Exists(destPath) && !Directory.Exists(sourcePath))
            {
                try
                {
                    MoveDirectoryAcrossFilesystems(destPath, sourcePath);
                    _logger.LogInformation("[League Move] Reverted folder move after DB failure.");
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogCritical(rollbackEx,
                        "[League Move] CRITICAL: rollback of folder move failed. League {LeagueId} files may now sit at {Dst} while the DB still points at {Src}. Manual intervention required.",
                        leagueId, destPath, sourcePath);
                }
            }

            return new LeagueMoveResult
            {
                Status = LeagueMoveStatus.MoveFailed,
                LeagueId = leagueId,
                Message = ex.Message,
                OldPath = sourcePath,
                NewPath = destPath,
            };
        }
    }

    /// <summary>
    /// Consolidate a league's files into a single root folder. Where
    /// MoveLeagueAsync refuses when files span multiple roots (the
    /// "SourceFolderAmbiguous" status), this walks the league's
    /// EventFiles and moves each one whose current path is NOT under the
    /// target root onto the target, preserving the relative path under
    /// its current root. Files already under the target root are left
    /// in place. The league's RootFolderId is updated to the target on
    /// success so a subsequent move/rename works on a clean state.
    ///
    /// Failures roll back the on-disk moves we already made so the DB
    /// state and disk state stay coherent.
    /// </summary>
    public async Task<LeagueMoveResult> ReorganizeLeagueAsync(int leagueId, int targetRootFolderId)
    {
        var league = await _db.Leagues.FirstOrDefaultAsync(l => l.Id == leagueId);
        if (league == null)
        {
            return new LeagueMoveResult { Status = LeagueMoveStatus.LeagueNotFound, LeagueId = leagueId };
        }

        var targetRoot = await _db.RootFolders.FirstOrDefaultAsync(rf => rf.Id == targetRootFolderId);
        if (targetRoot == null)
        {
            return new LeagueMoveResult { Status = LeagueMoveStatus.RootFolderNotFound, LeagueId = leagueId, NewRootFolderId = targetRootFolderId };
        }
        if (!Directory.Exists(targetRoot.Path))
        {
            return new LeagueMoveResult { Status = LeagueMoveStatus.RootFolderInaccessible, LeagueId = leagueId, NewRootFolderId = targetRootFolderId, Message = targetRoot.Path };
        }

        var eventFiles = await _db.EventFiles
            .Include(ef => ef.Event)
            .Where(ef => ef.Event != null && ef.Event.LeagueId == leagueId && ef.Exists && ef.FilePath != null)
            .ToListAsync();

        if (eventFiles.Count == 0)
        {
            league.RootFolderId = targetRootFolderId;
            await _db.SaveChangesAsync();
            return new LeagueMoveResult
            {
                Success = true,
                Status = LeagueMoveStatus.Ok,
                LeagueId = leagueId,
                NewRootFolderId = targetRootFolderId,
                NewPath = targetRoot.Path,
                FilesMoved = 0,
                Message = "No files to reorganize; binding updated."
            };
        }

        // Resolve every file's current root via longest-prefix match.
        var allRoots = await _db.RootFolders.ToListAsync();
        var rootPaths = allRoots
            .Select(rf => rf.Path)
            .OrderByDescending(p => p.Length)
            .ToList();

        // Build the per-file move plan. Files already under the target
        // root stay put (we still rewrite the league binding). Files
        // outside every configured root abort the operation up front
        // since we have no relative path to rewrite onto the target.
        var plan = new List<(EventFile File, string OldPath, string NewPath)>();
        foreach (var ef in eventFiles)
        {
            var match = rootPaths.FirstOrDefault(r => IsUnderRoot(ef.FilePath!, r));
            if (match == null)
            {
                return new LeagueMoveResult
                {
                    Status = LeagueMoveStatus.SourceFolderAmbiguous,
                    LeagueId = leagueId,
                    Message = $"Event file {ef.FilePath} is outside every configured root folder. Resolve manually before reorganizing."
                };
            }
            if (IsUnderRoot(ef.FilePath!, targetRoot.Path))
            {
                continue;
            }
            var rel = RelativeUnder(ef.FilePath!, match);
            var newPath = Path.Combine(targetRoot.Path, rel);
            plan.Add((ef, ef.FilePath!, newPath));
        }

        if (plan.Count == 0)
        {
            league.RootFolderId = targetRootFolderId;
            await _db.SaveChangesAsync();
            return new LeagueMoveResult
            {
                Success = true,
                Status = LeagueMoveStatus.Ok,
                LeagueId = leagueId,
                NewRootFolderId = targetRootFolderId,
                NewPath = targetRoot.Path,
                FilesMoved = 0,
                Message = "All files already lived under the target root; binding updated."
            };
        }

        // Pre-flight: refuse if the destination already has any of the
        // target paths occupied. Better to fail loud than to silently
        // leave the user with two files claiming the same final path.
        foreach (var (_, _, newPath) in plan)
        {
            if (File.Exists(newPath))
            {
                return new LeagueMoveResult
                {
                    Status = LeagueMoveStatus.DestinationExists,
                    LeagueId = leagueId,
                    Message = $"Destination already exists: {newPath}. Resolve the collision before reorganizing."
                };
            }
        }

        _logger.LogInformation("[League Reorganize] Moving {Count} file(s) for league {LeagueId} ({Name}) into {Target}",
            plan.Count, league.Id, league.Name, targetRoot.Path);

        var moved = new List<(EventFile File, string OldPath, string NewPath)>();
        var originalRootFolderId = league.RootFolderId;
        try
        {
            foreach (var (ef, oldPath, newPath) in plan)
            {
                var newDir = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrEmpty(newDir))
                {
                    Directory.CreateDirectory(newDir);
                }

                try
                {
                    File.Move(oldPath, newPath);
                }
                catch (IOException)
                {
                    // Cross-filesystem path: copy then delete.
                    File.Copy(oldPath, newPath, overwrite: false);
                    File.Delete(oldPath);
                }

                moved.Add((ef, oldPath, newPath));
                ef.FilePath = newPath;

                // Keep the denormalized Event.FilePath in sync for any
                // event whose canonical file we just moved.
                if (ef.Event != null && ef.Event.FilePath != null &&
                    string.Equals(NormalizePath(ef.Event.FilePath), NormalizePath(oldPath), StringComparison.OrdinalIgnoreCase))
                {
                    ef.Event.FilePath = newPath;
                }
            }

            league.RootFolderId = targetRootFolderId;
            await _db.SaveChangesAsync();

            return new LeagueMoveResult
            {
                Success = true,
                Status = LeagueMoveStatus.Ok,
                LeagueId = leagueId,
                NewRootFolderId = targetRootFolderId,
                NewPath = targetRoot.Path,
                FilesMoved = moved.Count,
                Message = $"Reorganized {moved.Count} file(s) into {targetRoot.Path}."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[League Reorganize] Failed for league {LeagueId}; rolling back {Count} on-disk move(s)",
                leagueId, moved.Count);

            league.RootFolderId = originalRootFolderId;

            foreach (var (ef, oldPath, newPath) in moved)
            {
                var restored = false;
                try
                {
                    if (File.Exists(newPath) && !File.Exists(oldPath))
                    {
                        var oldDir = Path.GetDirectoryName(oldPath);
                        if (!string.IsNullOrEmpty(oldDir))
                        {
                            Directory.CreateDirectory(oldDir);
                        }
                        File.Move(newPath, oldPath);
                    }

                    restored = File.Exists(oldPath);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogCritical(rollbackEx,
                        "[League Reorganize] CRITICAL: rollback failed for {OldPath} <- {NewPath}.",
                        oldPath, newPath);
                }

                // Point the record at wherever the file actually ended up. A
                // file that could not be moved back stays tracked at the new
                // root instead of leaving the library pointing at an empty
                // path, which is what turned a failed reorganize into files
                // the library could no longer see.
                var actualPath = restored ? oldPath : newPath;
                ef.FilePath = actualPath;

                if (ef.Event != null && ef.Event.FilePath != null &&
                    (string.Equals(NormalizePath(ef.Event.FilePath), NormalizePath(oldPath), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(NormalizePath(ef.Event.FilePath), NormalizePath(newPath), StringComparison.OrdinalIgnoreCase)))
                {
                    ef.Event.FilePath = actualPath;
                }

                if (!restored)
                {
                    _logger.LogWarning(
                        "[League Reorganize] {Path} stayed at the new root; its record now points there.",
                        newPath);
                }
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception saveEx)
            {
                _logger.LogCritical(saveEx,
                    "[League Reorganize] CRITICAL: could not record where the files ended up after rollback. Run a disk scan for league {LeagueId}.",
                    leagueId);
            }

            return new LeagueMoveResult
            {
                Status = LeagueMoveStatus.MoveFailed,
                LeagueId = leagueId,
                Message = ex.Message,
            };
        }
    }

    /// <summary>
    /// Bulk variant — runs MoveLeagueAsync per league and returns the
    /// per-league result list. Each league moves in its own transaction so
    /// a failure on one doesn't cancel the others; the caller surfaces the
    /// per-id result so the UI can flag the failures.
    /// </summary>
    public async Task<List<LeagueMoveResult>> MoveLeaguesAsync(IEnumerable<int> leagueIds, int newRootFolderId, bool moveFiles)
    {
        var results = new List<LeagueMoveResult>();
        foreach (var id in leagueIds.Distinct())
        {
            results.Add(await MoveLeagueAsync(id, newRootFolderId, moveFiles));
        }
        return results;
    }

    /// <summary>
    /// Same-filesystem fast path via Directory.Move; on cross-FS failure
    /// (IOException) we walk the tree and copy + delete. We deliberately
    /// don't use Move's overwrite flag — collisions were rejected upstream.
    /// </summary>
    private static void MoveDirectoryAcrossFilesystems(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
            return;
        }
        catch (IOException)
        {
            // fall through to the cross-FS path
        }

        CopyDirectoryRecursive(source, destination);
        Directory.Delete(source, recursive: true);
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var dest = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: false);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var dest = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, dest);
        }
    }

    private static bool IsUnderRoot(string filePath, string rootPath)
    {
        var normalizedFile = NormalizePath(filePath);
        var normalizedRoot = NormalizePath(rootPath);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            normalizedRoot += Path.DirectorySeparatorChar;
        return normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderPath(string filePath, string prefix)
    {
        var normalizedFile = NormalizePath(filePath);
        var normalizedPrefix = NormalizePath(prefix);
        if (!normalizedPrefix.EndsWith(Path.DirectorySeparatorChar))
            normalizedPrefix += Path.DirectorySeparatorChar;
        return normalizedFile.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedFile, NormalizePath(prefix), StringComparison.OrdinalIgnoreCase);
    }

    private static string RelativeUnder(string filePath, string rootPath)
    {
        var normalizedFile = NormalizePath(filePath);
        var normalizedRoot = NormalizePath(rootPath);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            normalizedRoot += Path.DirectorySeparatorChar;
        return normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedFile.Substring(normalizedRoot.Length)
            : normalizedFile;
    }

    private static string? FirstSegmentUnderRoot(string filePath, string rootPath)
    {
        var normalizedFile = NormalizePath(filePath);
        var normalizedRoot = NormalizePath(rootPath);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
            normalizedRoot += Path.DirectorySeparatorChar;
        if (!normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = normalizedFile.Substring(normalizedRoot.Length);
        var sepIndex = rest.IndexOf(Path.DirectorySeparatorChar);
        if (sepIndex < 0)
            return null; // file sits directly under root with no league subfolder
        return rest.Substring(0, sepIndex);
    }

    private static string RewritePath(string filePath, string oldPrefix, string newPrefix)
    {
        var normalizedFile = NormalizePath(filePath);
        var normalizedOld = NormalizePath(oldPrefix);
        if (!normalizedOld.EndsWith(Path.DirectorySeparatorChar))
            normalizedOld += Path.DirectorySeparatorChar;
        var normalizedNew = NormalizePath(newPrefix);
        if (!normalizedNew.EndsWith(Path.DirectorySeparatorChar))
            normalizedNew += Path.DirectorySeparatorChar;
        if (normalizedFile.StartsWith(normalizedOld, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedNew + normalizedFile.Substring(normalizedOld.Length);
        }
        return filePath;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', Path.DirectorySeparatorChar)
                   .Replace('/', Path.DirectorySeparatorChar);
    }
}
