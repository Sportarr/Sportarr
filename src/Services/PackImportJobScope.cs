using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

internal static class PackImportJobScope
{
    private readonly record struct JobKey(string Database, int? ClientId, string DownloadId);
    private sealed class Entry
    {
        internal SemaphoreSlim Lock { get; } = new(1, 1);
        internal int References;
    }
    private static readonly object Sync = new();
    private static readonly Dictionary<JobKey, Entry> Entries = new();

    internal static async Task<IDisposable> EnterAsync(SportarrDbContext db, DownloadQueueItem item)
    {
        // Hash connection identity without retaining credentials in dictionary keys.
        var databaseIdentity = db.Database.IsRelational()
            ? db.Database.ProviderName + "|" + db.Database.GetConnectionString()
            : db.ContextId.InstanceId.ToString();
        var key = new JobKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(databaseIdentity))),
            item.DownloadClientId, item.DownloadId ?? "");
        Entry entry;
        lock (Sync)
        {
            if (!Entries.TryGetValue(key, out entry!)) Entries.Add(key, entry = new Entry());
            entry.References++;
        }
        await entry.Lock.WaitAsync();
        return new Scope(key, entry);
    }

    private sealed class Scope(JobKey key, Entry entry) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            entry.Lock.Release();
            lock (Sync)
            {
                if (--entry.References == 0)
                {
                    Entries.Remove(key);
                    entry.Lock.Dispose();
                }
            }
        }
    }

    internal static async Task<bool> HasCompleteCoverageAsync(SportarrDbContext db, DownloadQueueItem item, Config config)
    {
        if (!item.IsPack) return true;
        var owners = await PackImportBoundary.ReadOwnersAsync(db, item);
        if (PackImportBoundary.OwnershipError(item, owners) != null || owners.Any(x => x.Status != DownloadStatus.Imported))
            return false;
        foreach (var owner in owners)
        {
            var records = await db.EventFiles.AsNoTracking().Where(x => x.EventId == owner.EventId && x.Exists && !x.IsParked).ToListAsync();
            var present = records.Where(x => x.Size > 0 && File.Exists(x.FilePath) &&
                FileImportService.GetFileSizeResolvingSymlinks(x.FilePath) == x.Size).ToList();
            var imports = await db.ImportHistories.AsNoTracking()
                .Where(x => x.EventId == owner.EventId && x.DownloadQueueItemId == owner.Id).ToListAsync();
            if (!present.Any(file => imports.Any(import => import.DestinationPath == file.FilePath && import.Size == file.Size))) return false;
            if (!EventPartDetector.AreAllMonitoredPartsPresent(owner.Event.Sport, owner.Event.Title, owner.Event.League?.Name,
                owner.Event.MonitoredParts, owner.Event.League?.MonitoredParts, present.Select(x => x.PartNumber).ToList(),
                config.EnableMultiPartEpisodes)) return false;
        }
        return true;
    }
}
