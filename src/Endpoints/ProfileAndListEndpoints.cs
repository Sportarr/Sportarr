using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class ProfileAndListEndpoints
{
    public static IEndpointRouteBuilder MapProfileAndListEndpoints(this IEndpointRouteBuilder app)
    {
app.MapGet("/api/delayprofile", async (SportarrDbContext db) =>
{
    var profiles = await db.DelayProfiles.OrderBy(d => d.Order).ToListAsync();
    return Results.Ok(profiles);
});

// API: Get single delay profile
app.MapGet("/api/delayprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.DelayProfiles.FindAsync(id);
    return profile == null ? Results.NotFound() : Results.Ok(profile);
});

// API: Create delay profile
app.MapPost("/api/delayprofile", async (DelayProfile profile, SportarrDbContext db) =>
{
    profile.Created = DateTime.UtcNow;
    db.DelayProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

// API: Update delay profile
app.MapPut("/api/delayprofile/{id}", async (int id, DelayProfile profile, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        var existing = await db.DelayProfiles.FindAsync(id);
        if (existing == null) return Results.NotFound();

        existing.Order = profile.Order;
        existing.PreferredProtocol = profile.PreferredProtocol;
        existing.UsenetDelay = profile.UsenetDelay;
        existing.TorrentDelay = profile.TorrentDelay;
        existing.BypassIfHighestQuality = profile.BypassIfHighestQuality;
        existing.BypassIfAboveCustomFormatScore = profile.BypassIfAboveCustomFormatScore;
        existing.MinimumCustomFormatScore = profile.MinimumCustomFormatScore;
        existing.Tags = profile.Tags;
        existing.LastModified = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(existing);
    }
    catch (DbUpdateConcurrencyException ex)
    {
        logger.LogError(ex, "[DELAY PROFILE] Concurrency error updating profile {Id}", id);
        return Results.Conflict(new { error = "Resource was modified by another client. Please refresh and try again." });
    }
});

// API: Delete delay profile
app.MapDelete("/api/delayprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.DelayProfiles.FindAsync(id);
    if (profile == null) return Results.NotFound();

    db.DelayProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Reorder delay profiles
app.MapPut("/api/delayprofile/reorder", async (List<int> profileIds, SportarrDbContext db) =>
{
    for (int i = 0; i < profileIds.Count; i++)
    {
        var profile = await db.DelayProfiles.FindAsync(profileIds[i]);
        if (profile != null)
        {
            profile.Order = i + 1;
        }
    }
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Release Profiles Management
app.MapGet("/api/releaseprofile", async (SportarrDbContext db) =>
{
    var profiles = await db.ReleaseProfiles.OrderBy(p => p.Name).ToListAsync();
    return Results.Ok(profiles);
});

app.MapGet("/api/releaseprofile/{id:int}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.ReleaseProfiles.FindAsync(id);
    return profile is not null ? Results.Ok(profile) : Results.NotFound();
});

app.MapPost("/api/releaseprofile", async (ReleaseProfile profile, SportarrDbContext db) =>
{
    profile.Created = DateTime.UtcNow;
    db.ReleaseProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Created($"/api/releaseprofile/{profile.Id}", profile);
});

app.MapPut("/api/releaseprofile/{id:int}", async (int id, ReleaseProfile updatedProfile, SportarrDbContext db) =>
{
    var profile = await db.ReleaseProfiles.FindAsync(id);
    if (profile is null) return Results.NotFound();

    profile.Name = updatedProfile.Name;
    profile.Enabled = updatedProfile.Enabled;
    profile.Required = updatedProfile.Required;
    profile.Ignored = updatedProfile.Ignored;
    profile.Preferred = updatedProfile.Preferred;
    profile.IncludePreferredWhenRenaming = updatedProfile.IncludePreferredWhenRenaming;
    profile.Tags = updatedProfile.Tags;
    profile.IndexerId = updatedProfile.IndexerId;
    profile.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

app.MapDelete("/api/releaseprofile/{id:int}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.ReleaseProfiles.FindAsync(id);
    if (profile is null) return Results.NotFound();

    db.ReleaseProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Quality Definitions Management
app.MapGet("/api/qualitydefinition", async (SportarrDbContext db) =>
{
    var definitions = await db.QualityDefinitions.OrderBy(q => q.Quality).ToListAsync();
    return Results.Ok(definitions);
});

app.MapGet("/api/qualitydefinition/{id:int}", async (int id, SportarrDbContext db) =>
{
    var definition = await db.QualityDefinitions.FindAsync(id);
    return definition is not null ? Results.Ok(definition) : Results.NotFound();
});

app.MapPut("/api/qualitydefinition/{id:int}", async (int id, QualityDefinition updatedDef, SportarrDbContext db) =>
{
    var definition = await db.QualityDefinitions.FindAsync(id);
    if (definition is null) return Results.NotFound();

    definition.MinSize = updatedDef.MinSize;
    definition.MaxSize = updatedDef.MaxSize;
    definition.PreferredSize = updatedDef.PreferredSize;
    definition.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(definition);
});

app.MapPut("/api/qualitydefinition/bulk", async (List<QualityDefinition> definitions, SportarrDbContext db, TrashGuideSyncService trashSync) =>
{
    foreach (var updatedDef in definitions)
    {
        var definition = await db.QualityDefinitions.FindAsync(updatedDef.Id);
        if (definition is not null)
        {
            definition.MinSize = updatedDef.MinSize;
            definition.MaxSize = updatedDef.MaxSize;
            definition.PreferredSize = updatedDef.PreferredSize;
            definition.LastModified = DateTime.UtcNow;
        }
    }
    await db.SaveChangesAsync();

    // Disable TRaSH quality size auto-sync when user manually saves custom values
    // This prevents auto-sync from overwriting user customizations
    var syncSettings = await trashSync.GetSyncSettingsAsync();
    if (syncSettings.EnableQualitySizeSync)
    {
        syncSettings.EnableQualitySizeSync = false;
        await trashSync.SaveSyncSettingsAsync(syncSettings);
        Log.Information("[Quality] Disabled TRaSH quality size auto-sync due to manual save");
    }

    return Results.Ok();
});

// API: Quality Definition TRaSH Import
app.MapPost("/api/qualitydefinition/trash/import", async (TrashGuideSyncService trashSync) =>
{
    // Enable auto-sync when user manually imports - this ensures future syncs keep quality sizes up-to-date
    var result = await trashSync.SyncQualitySizesFromTrashAsync(enableAutoSync: true);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

// API: Import Lists Management
app.MapGet("/api/importlist", async (SportarrDbContext db) =>
{
    var lists = await db.ImportLists.OrderBy(l => l.Name).ToListAsync();
    return Results.Ok(lists);
});

app.MapGet("/api/importlist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var list = await db.ImportLists.FindAsync(id);
    return list is not null ? Results.Ok(list) : Results.NotFound();
});

app.MapPost("/api/importlist", async (ImportList list, SportarrDbContext db) =>
{
    list.Created = DateTime.UtcNow;
    db.ImportLists.Add(list);
    await db.SaveChangesAsync();
    return Results.Created($"/api/importlist/{list.Id}", list);
});

app.MapPut("/api/importlist/{id:int}", async (int id, ImportList updatedList, SportarrDbContext db) =>
{
    var list = await db.ImportLists.FindAsync(id);
    if (list is null) return Results.NotFound();

    list.Name = updatedList.Name;
    list.Enabled = updatedList.Enabled;
    list.ListType = updatedList.ListType;
    list.Url = updatedList.Url;
    list.ApiKey = updatedList.ApiKey;
    list.QualityProfileId = updatedList.QualityProfileId;
    list.RootFolderPath = updatedList.RootFolderPath;
    list.MonitorEvents = updatedList.MonitorEvents;
    list.SearchOnAdd = updatedList.SearchOnAdd;
    list.Tags = updatedList.Tags;
    list.MinimumDaysBeforeEvent = updatedList.MinimumDaysBeforeEvent;
    list.LeagueFilter = updatedList.LeagueFilter;
    list.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(list);
});

app.MapDelete("/api/importlist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var list = await db.ImportLists.FindAsync(id);
    if (list is null) return Results.NotFound();

    db.ImportLists.Remove(list);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPost("/api/importlist/{id:int}/sync", async (int id, ImportListService importListService) =>
{
    var (success, message, eventsFound) = await importListService.SyncImportListAsync(id);

    if (success)
    {
        return Results.Ok(new
        {
            success = true,
            message,
            eventsFound,
            listId = id
        });
    }
    else
    {
        return Results.BadRequest(new
        {
            success = false,
            message,
            eventsFound = 0,
            listId = id
        });
    }
});

// API: Metadata Providers Management
app.MapGet("/api/metadata", async (SportarrDbContext db) =>
{
    var providers = await db.MetadataProviders.OrderBy(m => m.Name).ToListAsync();
    return Results.Ok(providers);
});

app.MapGet("/api/metadata/{id:int}", async (int id, SportarrDbContext db) =>
{
    var provider = await db.MetadataProviders.FindAsync(id);
    return provider is not null ? Results.Ok(provider) : Results.NotFound();
});

app.MapPost("/api/metadata", async (MetadataProvider provider, SportarrDbContext db) =>
{
    provider.Created = DateTime.UtcNow;
    db.MetadataProviders.Add(provider);
    await db.SaveChangesAsync();
    return Results.Created($"/api/metadata/{provider.Id}", provider);
});

app.MapPut("/api/metadata/{id:int}", async (int id, MetadataProvider provider, SportarrDbContext db) =>
{
    var existing = await db.MetadataProviders.FindAsync(id);
    if (existing is null) return Results.NotFound();

    existing.Name = provider.Name;
    existing.Type = provider.Type;
    existing.Enabled = provider.Enabled;
    existing.EventNfo = provider.EventNfo;
    existing.EventCardNfo = provider.EventCardNfo;
    existing.EventImages = provider.EventImages;
    existing.PlayerImages = provider.PlayerImages;
    existing.LeagueLogos = provider.LeagueLogos;
    existing.EventNfoFilename = provider.EventNfoFilename;
    existing.EventPosterFilename = provider.EventPosterFilename;
    existing.EventFanartFilename = provider.EventFanartFilename;
    existing.UseEventFolder = provider.UseEventFolder;
    existing.ImageQuality = provider.ImageQuality;
    existing.Tags = provider.Tags;
    existing.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(existing);
});

app.MapDelete("/api/metadata/{id:int}", async (int id, SportarrDbContext db) =>
{
    var provider = await db.MetadataProviders.FindAsync(id);
    if (provider is null) return Results.NotFound();

    db.MetadataProviders.Remove(provider);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

        return app;
    }
}
