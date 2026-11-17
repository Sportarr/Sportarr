using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class UpdateEndpoints
{
    public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        // The running version, the latest release, and whether the built-in
        // update can install it. "refresh=true" asks GitHub again.
        app.MapGet("/api/update/status", async (bool? refresh, InstallUpdateService installer, CancellationToken ct) =>
            Results.Ok(await installer.GetStatusAsync(refresh == true, ct)));

        // Installs the latest release with the built-in update. Sportarr
        // stops after the answer and the update helper starts it again.
        // The request token is not used. A closed browser tab must not stop
        // a download half way.
        app.MapPost("/api/update/install", async (InstallUpdateService installer) =>
        {
            var result = await installer.InstallAsync(CancellationToken.None);
            return result.Outcome switch
            {
                UpdateInstallOutcome.Started => Results.Accepted(value: new { message = result.Message }),
                UpdateInstallOutcome.Refused => Results.Conflict(new { error = result.Message }),
                _ => Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status500InternalServerError)
            };
        });

        return app;
    }
}
