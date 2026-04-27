using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Sportarr.Api.Endpoints;

public static class SystemAgentEndpoints
{
    public static IEndpointRouteBuilder MapSystemAgentEndpoints(this IEndpointRouteBuilder app)
    {
        // API: Download Media Server Agents
        app.MapGet("/api/system/agents", () =>
        {
            var agents = new List<object>
            {
                new
                {
                    name = "Plex",
                    type = "plex",
                    available = true,
                    downloadUrl = "/api/system/agents/plex/download"
                },
                new
                {
                    name = "Jellyfin",
                    type = "jellyfin",
                    available = true,
                    downloadUrl = "/api/system/agents/jellyfin/download",
                    repositoryUrl = "https://raw.githubusercontent.com/sportarr/Sportarr/main/agents/jellyfin/manifest.json"
                },
                new
                {
                    name = "Emby",
                    type = "emby",
                    available = true,
                    downloadUrl = "/api/system/agents/emby/download"
                }
            };

            return Results.Ok(new { agents });
        });

        app.MapGet("/api/system/agents/plex/download", async (HttpContext context, ILogger<SystemAgentEndpoints> logger) =>
        {
            var downloadUrl = await Sportarr.Api.Helpers.PluginDownloadHelper.GetPluginDownloadUrlAsync("plex-legacy", logger);
            if (downloadUrl != null)
            {
                context.Response.Redirect(downloadUrl, permanent: false);
                return;
            }

            logger.LogWarning("Could not find Plex legacy bundle asset in GitHub releases, redirecting to releases page");
            context.Response.Redirect("https://github.com/Sportarr/Sportarr/releases/latest", permanent: false);
        });

        app.MapGet("/api/system/agents/jellyfin/download", async (HttpContext context, ILogger<SystemAgentEndpoints> logger) =>
        {
            var downloadUrl = await Sportarr.Api.Helpers.PluginDownloadHelper.GetPluginDownloadUrlAsync("jellyfin", logger);
            if (downloadUrl != null)
            {
                context.Response.Redirect(downloadUrl, permanent: false);
                return;
            }

            logger.LogWarning("Could not find Jellyfin plugin asset in GitHub releases, redirecting to releases page");
            context.Response.Redirect("https://github.com/Sportarr/Sportarr/releases/latest", permanent: false);
        });

        app.MapGet("/api/system/agents/emby/download", async (HttpContext context, ILogger<SystemAgentEndpoints> logger) =>
        {
            var downloadUrl = await Sportarr.Api.Helpers.PluginDownloadHelper.GetPluginDownloadUrlAsync("emby", logger);
            if (downloadUrl != null)
            {
                context.Response.Redirect(downloadUrl, permanent: false);
                return;
            }

            logger.LogWarning("Could not find Emby plugin asset in GitHub releases, redirecting to releases page");
            context.Response.Redirect("https://github.com/Sportarr/Sportarr/releases/latest", permanent: false);
        });

        return app;
    }
}
