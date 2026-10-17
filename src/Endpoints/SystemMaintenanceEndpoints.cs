using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sportarr.Api.Services;

namespace Sportarr.Api.Endpoints;

public static class SystemMaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapSystemMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/system/disk-scan", (DiskScanService diskScanService) =>
        {
            diskScanService.TriggerScanNow();
            return Results.Ok(new { message = "Disk scan triggered successfully" });
        });

        return app;
    }
}
