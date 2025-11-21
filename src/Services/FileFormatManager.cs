using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Manages file naming format based on multi-part episode settings
/// Automatically adds/removes {Part} token when EnableMultiPartEpisodes is toggled
/// </summary>
public class FileFormatManager
{
    private readonly SportarrDbContext _db;
    private readonly ILogger<FileFormatManager> _logger;

    // Standard format templates
    private const string FORMAT_WITH_PART = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}";
    private const string FORMAT_WITHOUT_PART = "{Series} - {Season}{Episode} - {Event Title} - {Quality Full}";

    public FileFormatManager(SportarrDbContext db, ILogger<FileFormatManager> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Update file format based on multi-part episode setting
    /// Call this whenever EnableMultiPartEpisodes changes
    /// </summary>
    public async Task UpdateFileFormatForMultiPartSetting(bool enableMultiPart)
    {
        var settings = await _db.MediaManagementSettings.FirstOrDefaultAsync();

        if (settings == null)
        {
            _logger.LogWarning("MediaManagementSettings not found - cannot update file format");
            return;
        }

        var currentFormat = settings.StandardFileFormat;
        var currentEventFormat = settings.StandardEventFormat;
        var newFormat = enableMultiPart ? FORMAT_WITH_PART : FORMAT_WITHOUT_PART;

        // Only update if user hasn't customized the format beyond our templates
        if (IsStandardFormat(currentFormat))
        {
            settings.StandardFileFormat = newFormat;
            settings.StandardEventFormat = newFormat; // Also update StandardEventFormat (shown in UI)
            await _db.SaveChangesAsync();

            _logger.LogInformation("Updated file format: EnableMultiPart={EnableMultiPart}, Format={Format}",
                enableMultiPart, newFormat);
        }
        else
        {
            // User has a custom format - intelligently add/remove {Part} token
            if (enableMultiPart && !currentFormat.Contains("{Part}", StringComparison.OrdinalIgnoreCase))
            {
                // Add {Part} after {Episode} if it exists
                if (currentFormat.Contains("{Episode}", StringComparison.OrdinalIgnoreCase))
                {
                    settings.StandardFileFormat = currentFormat.Replace("{Episode}", "{Episode}{Part}", StringComparison.OrdinalIgnoreCase);
                    settings.StandardEventFormat = settings.StandardFileFormat; // Keep both in sync
                    await _db.SaveChangesAsync();
                    _logger.LogInformation("Added {{Part}} token to custom format: {Format}", settings.StandardFileFormat);
                }
                else
                {
                    _logger.LogWarning("Custom format doesn't contain {{Episode}} - cannot auto-add {{Part}} token. User must add manually.");
                }
            }
            else if (!enableMultiPart && currentFormat.Contains("{Part}", StringComparison.OrdinalIgnoreCase))
            {
                // Remove {Part} token
                settings.StandardFileFormat = currentFormat.Replace("{Part}", "", StringComparison.OrdinalIgnoreCase);
                settings.StandardEventFormat = settings.StandardFileFormat; // Keep both in sync
                await _db.SaveChangesAsync();
                _logger.LogInformation("Removed {{Part}} token from custom format: {Format}", settings.StandardFileFormat);
            }
        }
    }

    /// <summary>
    /// Check if current format is one of our standard templates
    /// </summary>
    private bool IsStandardFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return true;

        // Check if it matches any of our known formats
        var standardFormats = new[]
        {
            FORMAT_WITH_PART,
            FORMAT_WITHOUT_PART,
            // Legacy formats
            "{Event Title} - {Air Date} - {Quality Full}",
            "{Series} - {Season}{Episode} - {Event Title} - {Quality Full}",
            "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}"
        };

        return standardFormats.Any(f => f.Equals(format, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get the correct default format based on multi-part setting
    /// </summary>
    public string GetDefaultFormat(bool enableMultiPart)
    {
        return enableMultiPart ? FORMAT_WITH_PART : FORMAT_WITHOUT_PART;
    }

    /// <summary>
    /// Ensure file format matches current multi-part setting
    /// Call this on startup or when settings are loaded
    /// </summary>
    public async Task EnsureFileFormatMatchesMultiPartSetting(bool enableMultiPart)
    {
        var settings = await _db.MediaManagementSettings.FirstOrDefaultAsync();

        if (settings == null)
        {
            _logger.LogDebug("MediaManagementSettings not found - will be created with defaults");
            return;
        }

        var currentFormat = settings.StandardFileFormat;
        var shouldHavePart = enableMultiPart;
        var hasPart = currentFormat?.Contains("{Part}", StringComparison.OrdinalIgnoreCase) ?? false;

        if (shouldHavePart != hasPart)
        {
            _logger.LogInformation("File format doesn't match EnableMultiPartEpisodes setting - auto-correcting");
            await UpdateFileFormatForMultiPartSetting(enableMultiPart);
        }
    }
}
