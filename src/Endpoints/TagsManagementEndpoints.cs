using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class TagsManagementEndpoints
{
    public static IEndpointRouteBuilder MapTagsManagementEndpoints(this IEndpointRouteBuilder app)
    {
// API: Tags Management
app.MapPost("/api/tag", async (Tag tag, SportarrDbContext db) =>
{
    db.Tags.Add(tag);
    await db.SaveChangesAsync();
    return Results.Created($"/api/tag/{tag.Id}", tag);
});

app.MapPut("/api/tag/{id:int}", async (int id, Tag updatedTag, SportarrDbContext db) =>
{
    var tag = await db.Tags.FindAsync(id);
    if (tag is null) return Results.NotFound();

    tag.Label = updatedTag.Label;
    tag.Color = updatedTag.Color;
    await db.SaveChangesAsync();
    return Results.Ok(tag);
});

app.MapDelete("/api/tag/{id:int}", async (int id, SportarrDbContext db) =>
{
    var tag = await db.Tags.FindAsync(id);
    if (tag is null) return Results.NotFound();

    db.Tags.Remove(tag);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Tag Detail (with linked entity counts, like Sonarr's /api/v3/tag/detail)
app.MapGet("/api/tag/detail", async (SportarrDbContext db) =>
{
    var tags = await db.Tags.ToListAsync();
    var leagues = await db.Leagues.ToListAsync();
    var delayProfiles = await db.DelayProfiles.ToListAsync();
    var releaseProfiles = await db.ReleaseProfiles.ToListAsync();
    var indexers = await db.Indexers.ToListAsync();
    var downloadClients = await db.DownloadClients.ToListAsync();
    var notifications = await db.Notifications.ToListAsync();
    var importLists = await db.ImportLists.ToListAsync();

    var result = tags.Select(tag => new
    {
        id = tag.Id,
        label = tag.Label,
        color = tag.Color,
        leagueIds = leagues.Where(l => l.Tags.Contains(tag.Id)).Select(l => l.Id).ToList(),
        delayProfileIds = delayProfiles.Where(d => d.Tags.Contains(tag.Id)).Select(d => d.Id).ToList(),
        releaseProfileIds = releaseProfiles.Where(r => r.Tags.Contains(tag.Id)).Select(r => r.Id).ToList(),
        indexerIds = indexers.Where(i => i.Tags.Contains(tag.Id)).Select(i => i.Id).ToList(),
        downloadClientIds = downloadClients.Where(dc => dc.Tags.Contains(tag.Id)).Select(dc => dc.Id).ToList(),
        notificationIds = notifications.Where(n => n.Tags.Contains(tag.Id)).Select(n => n.Id).ToList(),
        importListIds = importLists.Where(il => il.Tags.Contains(tag.Id)).Select(il => il.Id).ToList()
    });

    return Results.Ok(result);
});

        return app;
    }
}
