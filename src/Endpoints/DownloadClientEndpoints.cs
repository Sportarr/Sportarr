using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Services;
using Sportarr.Api.Models;

namespace Sportarr.Api.Endpoints;

public static class DownloadClientEndpoints
{
    public static IEndpointRouteBuilder MapDownloadClientEndpoints(this IEndpointRouteBuilder app)
    {
// API: Download Clients Management
app.MapGet("/api/downloadclient", async (SportarrDbContext db, ILogger<DownloadClientEndpoints> logger) =>
{
    var clients = await db.DownloadClients.OrderBy(dc => dc.Priority).ToListAsync();
    logger.LogDebug("[Download Client] Returning {Count} download clients", clients.Count);
    foreach (var client in clients)
    {
        logger.LogDebug("[Download Client] Client {Name}: UrlBase = '{UrlBase}'", client.Name, client.UrlBase);
    }
    return Results.Ok(clients);
});

app.MapGet("/api/downloadclient/{id:int}", async (int id, SportarrDbContext db) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    return client is null ? Results.NotFound() : Results.Ok(client);
});

app.MapPost("/api/downloadclient", async (DownloadClient client, SportarrDbContext db, ILogger<DownloadClientEndpoints> logger) =>
{
    logger.LogInformation("[Download Client] Creating new client {Name} - UrlBase: '{UrlBase}'", client.Name, client.UrlBase);
    // Sanitize host: strip protocol prefix and trailing slashes (users often paste full URLs)
    // If they included https://, also enable UseSsl so the secure intent is preserved
    if (!string.IsNullOrEmpty(client.Host))
    {
        if (client.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) client.UseSsl = true;
        else if (client.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) client.UseSsl = false;
        client.Host = System.Text.RegularExpressions.Regex.Replace(client.Host, @"^https?://", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).TrimEnd('/');
    }
    client.Created = DateTime.UtcNow;
    db.DownloadClients.Add(client);
    await db.SaveChangesAsync();
    logger.LogInformation("[Download Client] Created client {Name} with ID {Id}", client.Name, client.Id);
    return Results.Created($"/api/downloadclient/{client.Id}", client);
});

app.MapPut("/api/downloadclient/{id:int}", async (int id, DownloadClient updatedClient, SportarrDbContext db, ILogger<DownloadClientEndpoints> logger) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    if (client is null)
    {
        logger.LogWarning("[Download Client] Client ID {Id} not found for update", id);
        return Results.NotFound(new { error = $"Download client with ID {id} not found" });
    }

    // Sanitize host: strip protocol prefix and trailing slashes (users often paste full URLs)
    // If they included https://, also enable UseSsl so the secure intent is preserved
    if (!string.IsNullOrEmpty(updatedClient.Host))
    {
        if (updatedClient.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) updatedClient.UseSsl = true;
        else if (updatedClient.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) updatedClient.UseSsl = false;
        updatedClient.Host = System.Text.RegularExpressions.Regex.Replace(updatedClient.Host, @"^https?://", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).TrimEnd('/');
    }
    logger.LogInformation("[Download Client] Updating client {Name} (ID: {Id}) - UrlBase: '{UrlBase}'", updatedClient.Name, id, updatedClient.UrlBase);

    client.Name = updatedClient.Name;
    client.Type = updatedClient.Type;
    client.Host = updatedClient.Host;
    client.Port = updatedClient.Port;
    client.Username = updatedClient.Username;
    // Only update password if a new one is provided (preserve existing if empty)
    if (!string.IsNullOrEmpty(updatedClient.Password))
    {
        client.Password = updatedClient.Password;
    }
    // Only update API key if a new one is provided (preserve existing if empty)
    if (!string.IsNullOrEmpty(updatedClient.ApiKey))
    {
        client.ApiKey = updatedClient.ApiKey;
    }
    client.UrlBase = updatedClient.UrlBase;
    client.Category = updatedClient.Category;
    client.PostImportCategory = updatedClient.PostImportCategory;
    client.Directory = updatedClient.Directory;
    client.UseSsl = updatedClient.UseSsl;
    client.DisableSslCertificateValidation = updatedClient.DisableSslCertificateValidation;
    client.Enabled = updatedClient.Enabled;
    client.Priority = updatedClient.Priority;
    client.SequentialDownload = updatedClient.SequentialDownload;
    client.FirstAndLastFirst = updatedClient.FirstAndLastFirst;
    client.InitialState = updatedClient.InitialState;
    client.RemoveCompletedDownloads = updatedClient.RemoveCompletedDownloads;
    client.RemoveFailedDownloads = updatedClient.RemoveFailedDownloads;
    client.Tags = updatedClient.Tags;
    client.LastModified = DateTime.UtcNow;

    try
    {
        await db.SaveChangesAsync();
        logger.LogInformation("[Download Client] Updated client {Name} (ID: {Id})", client.Name, id);
        return Results.Ok(client);
    }
    catch (DbUpdateConcurrencyException ex)
    {
        logger.LogError(ex, "[Download Client] Concurrency error updating client {Id}: Record may have been deleted", id);
        return Results.Conflict(new { error = "This download client was modified or deleted. Please refresh and try again." });
    }
});

app.MapDelete("/api/downloadclient/{id:int}", async (int id, SportarrDbContext db) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    if (client is null) return Results.NotFound();

    db.DownloadClients.Remove(client);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Test download client connection - supports all client types
app.MapPost("/api/downloadclient/test", async (DownloadClient client, DownloadClientService downloadClientService) =>
{
    var (success, message) = await downloadClientService.TestConnectionAsync(client);

    if (success)
    {
        return Results.Ok(new { success = true, message });
    }

    return Results.BadRequest(new { success = false, message });
});

// API: Remote Path Mappings (for download client path translation)
app.MapGet("/api/remotepathmapping", async (SportarrDbContext db) =>
{
    var mappings = await db.RemotePathMappings.OrderBy(m => m.Host).ToListAsync();
    return Results.Ok(mappings);
});

app.MapGet("/api/remotepathmapping/{id:int}", async (int id, SportarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    return mapping is null ? Results.NotFound() : Results.Ok(mapping);
});

app.MapPost("/api/remotepathmapping", async (RemotePathMapping mapping, SportarrDbContext db) =>
{
    db.RemotePathMappings.Add(mapping);
    await db.SaveChangesAsync();
    return Results.Created($"/api/remotepathmapping/{mapping.Id}", mapping);
});

app.MapPut("/api/remotepathmapping/{id:int}", async (int id, RemotePathMapping updatedMapping, SportarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    if (mapping is null) return Results.NotFound();

    mapping.Host = updatedMapping.Host;
    mapping.RemotePath = updatedMapping.RemotePath;
    mapping.LocalPath = updatedMapping.LocalPath;

    await db.SaveChangesAsync();
    return Results.Ok(mapping);
});

app.MapDelete("/api/remotepathmapping/{id:int}", async (int id, SportarrDbContext db) =>
{
    var mapping = await db.RemotePathMappings.FindAsync(id);
    if (mapping is null) return Results.NotFound();

    db.RemotePathMappings.Remove(mapping);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

        return app;
    }
}
