using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Startup;

public static class QualityProfileOrderNormalizer
{
    public static async Task<int> NormalizeAsync(SportarrDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            var columns = (await db.Database.SqlQueryRaw<string>(
                "SELECT name AS \"Value\" FROM pragma_table_info('QualityProfiles')")
                .ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var mappedColumns = db.Model.FindEntityType(typeof(QualityProfile))!
                .GetProperties().Select(property => property.Name);
            if (!mappedColumns.All(columns.Contains))
            {
                return 0;
            }
        }

        var profiles = await db.QualityProfiles
            .Where(profile => profile.IsSynced && profile.TrashId != null)
            .ToListAsync();
        var normalized = 0;
        foreach (var profile in profiles)
        {
            if (!QualityProfileRanker.UsesAscendingImportedOrder(profile))
            {
                continue;
            }

            profile.Items = profile.Items.AsEnumerable().Reverse().ToList();
            db.Entry(profile).Property(p => p.Items).IsModified = true;
            normalized++;
        }

        if (normalized > 0)
        {
            await db.SaveChangesAsync();
        }

        return normalized;
    }
}
