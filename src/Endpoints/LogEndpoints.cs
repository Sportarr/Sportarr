using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Sportarr.Api.Endpoints;

public static class LogEndpoints
{
    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app, string logsPath)
    {
        // API: Get log files list
        app.MapGet("/api/log/file", (ILogger<LogEndpoints> logger) =>
        {
            try
            {
                var logFiles = Directory.GetFiles(logsPath, "*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(f => new
                    {
                        filename = Path.GetFileName(f.FullName),
                        lastWriteTime = f.LastWriteTime,
                        size = f.Length
                    })
                    .ToList();

                logger.LogDebug("[LOG FILES] Listing {Count} log files", logFiles.Count);
                return Results.Ok(logFiles);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[LOG FILES] Error listing log files");
                return Results.Problem("Error listing log files");
            }
        });

        // API: Get specific log file content
        // Uses query parameter to avoid ASP.NET routing issues with dots in filenames
        app.MapGet("/api/log/file/content", (string filename, ILogger<LogEndpoints> logger) =>
        {
            try
            {
                if (string.IsNullOrEmpty(filename))
                {
                    return Results.BadRequest(new { message = "Filename is required" });
                }

                filename = Path.GetFileName(filename);
                var logFilePath = Path.Combine(logsPath, filename);

                if (!File.Exists(logFilePath))
                {
                    logger.LogDebug("[LOG FILES] File not found: {Filename}", filename);
                    return Results.NotFound(new { message = "Log file not found" });
                }

                logger.LogDebug("[LOG FILES] Reading log file: {Filename}", filename);

                string content;
                using (var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fileStream))
                {
                    content = reader.ReadToEnd();
                }

                return Results.Ok(new
                {
                    filename = filename,
                    content = content,
                    lastWriteTime = File.GetLastWriteTime(logFilePath),
                    size = new FileInfo(logFilePath).Length
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[LOG FILES] Error reading log file: {Filename}", filename);
                return Results.Problem("Error reading log file");
            }
        });

        // API: Download log file
        app.MapGet("/api/log/file/download", (string filename, ILogger<LogEndpoints> logger) =>
        {
            try
            {
                if (string.IsNullOrEmpty(filename))
                {
                    return Results.BadRequest(new { message = "Filename is required" });
                }

                filename = Path.GetFileName(filename);
                var logFilePath = Path.Combine(logsPath, filename);

                if (!File.Exists(logFilePath))
                {
                    logger.LogDebug("[LOG FILES] File not found for download: {Filename}", filename);
                    return Results.NotFound(new { message = "Log file not found" });
                }

                logger.LogDebug("[LOG FILES] Downloading log file: {Filename}", filename);

                byte[] fileBytes;
                using (var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var memoryStream = new MemoryStream())
                {
                    fileStream.CopyTo(memoryStream);
                    fileBytes = memoryStream.ToArray();
                }

                return Results.File(fileBytes, "text/plain", filename);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[LOG FILES] Error downloading log file: {Filename}", filename);
                return Results.Problem("Error downloading log file");
            }
        });

        return app;
    }
}
