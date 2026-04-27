using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using System.Text.Json;

namespace Sportarr.Api.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
app.MapGet("/api/config", async (ConfigService configService) =>
{
    var config = await configService.GetConfigAsync();
    return Results.Ok(new { enableMultiPartEpisodes = config.EnableMultiPartEpisodes });
});

// API: Settings Management (using config.xml)
app.MapGet("/api/settings", async (ConfigService configService, SportarrDbContext db, ILogger<Program> logger) =>
{
    var config = await configService.GetConfigAsync();
    var dbMediaSettings = await db.MediaManagementSettings.FirstOrDefaultAsync();

    // Debug logging to diagnose folder settings load issue
    if (dbMediaSettings != null)
    {
        logger.LogInformation("[CONFIG] GET /api/settings - Database folder settings: CreateLeagueFolders={League}, CreateSeasonFolders={Season}, CreateEventFolders={Event}, ReorganizeFolders={Reorg}",
            dbMediaSettings.CreateLeagueFolders,
            dbMediaSettings.CreateSeasonFolders,
            dbMediaSettings.CreateEventFolders,
            dbMediaSettings.ReorganizeFolders);
    }
    else
    {
        logger.LogWarning("[CONFIG] GET /api/settings - No MediaManagementSettings row found in database, using defaults");
    }

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    // Build MediaManagementSettings JSON with debug logging
    var mediaSettingsObj = new MediaManagementSettings
    {
        RenameEvents = config.RenameEvents,
        ReplaceIllegalCharacters = config.ReplaceIllegalCharacters,
        EnableMultiPartEpisodes = config.EnableMultiPartEpisodes,
        StandardFileFormat = dbMediaSettings?.StandardFileFormat ?? "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
        // Granular folder format settings
        LeagueFolderFormat = dbMediaSettings?.LeagueFolderFormat ?? "{Series}",
        SeasonFolderFormat = dbMediaSettings?.SeasonFolderFormat ?? "Season {Season}",
        EventFolderFormat = dbMediaSettings?.EventFolderFormat ?? "{Event Title} ({Year}-{Month}-{Day}) E{Episode}",
        RenameFiles = dbMediaSettings?.RenameFiles ?? true,
        // Granular folder creation settings
        CreateLeagueFolders = dbMediaSettings?.CreateLeagueFolders ?? true,
        CreateSeasonFolders = dbMediaSettings?.CreateSeasonFolders ?? true,
        CreateEventFolders = dbMediaSettings?.CreateEventFolders ?? false,
        ReorganizeFolders = dbMediaSettings?.ReorganizeFolders ?? false,
        CopyFiles = dbMediaSettings?.CopyFiles ?? false,
        // Note: RemoveCompletedDownloads is now a per-client setting in Download Client options
        DeleteEmptyFolders = dbMediaSettings?.DeleteEmptyFolders ?? false,
        SkipFreeSpaceCheck = config.SkipFreeSpaceCheck,
        MinimumFreeSpace = config.MinimumFreeSpace,
        UseHardlinks = config.UseHardlinks,
        ImportExtraFiles = config.ImportExtraFiles,
        ExtraFileExtensions = config.ExtraFileExtensions,
        ChangeFileDate = config.ChangeFileDate,
        RecycleBin = config.RecycleBin,
        RecycleBinCleanup = config.RecycleBinCleanup,
        SetPermissions = config.SetPermissions,
        ChmodFolder = config.ChmodFolder,
        ChownGroup = config.ChownGroup,
        // Preserve timestamps from database
        Created = dbMediaSettings?.Created ?? DateTime.UtcNow,
        LastModified = dbMediaSettings?.LastModified
    };
    var mediaSettingsJson = System.Text.Json.JsonSerializer.Serialize(mediaSettingsObj, jsonOptions);
    logger.LogInformation("[CONFIG] GET /api/settings - MediaManagementSettings JSON: {Json}", mediaSettingsJson);

    // Convert Config to AppSettings format for frontend compatibility
    var settings = new AppSettings
    {
        HostSettings = System.Text.Json.JsonSerializer.Serialize(new HostSettings
        {
            BindAddress = config.BindAddress,
            Port = config.Port,
            UrlBase = config.UrlBase,
            InstanceName = config.InstanceName,
            EnableSsl = config.EnableSsl,
            SslPort = config.SslPort,
            SslCertPath = config.SslCertPath,
            SslCertPassword = config.SslCertPassword
        }, jsonOptions),

        SecuritySettings = System.Text.Json.JsonSerializer.Serialize(new SecuritySettings
        {
            AuthenticationMethod = config.AuthenticationMethod.ToLower(),
            AuthenticationRequired = config.AuthenticationRequired.ToLower(),
            Username = config.Username,
            Password = "",
            ApiKey = config.ApiKey,
            CertificateValidation = config.CertificateValidation.ToLower(),
            PasswordHash = config.PasswordHash,
            PasswordSalt = config.PasswordSalt,
            PasswordIterations = config.PasswordIterations
        }, jsonOptions),

        ProxySettings = System.Text.Json.JsonSerializer.Serialize(new ProxySettings
        {
            UseProxy = config.UseProxy,
            ProxyType = config.ProxyType.ToLower(),
            ProxyHostname = config.ProxyHostname,
            ProxyPort = config.ProxyPort,
            ProxyUsername = config.ProxyUsername,
            ProxyPassword = config.ProxyPassword,
            ProxyBypassFilter = config.ProxyBypassFilter,
            ProxyBypassLocalAddresses = config.ProxyBypassLocalAddresses
        }, jsonOptions),

        LoggingSettings = System.Text.Json.JsonSerializer.Serialize(new LoggingSettings
        {
            LogLevel = config.LogLevel.ToLower()
        }, jsonOptions),

        AnalyticsSettings = System.Text.Json.JsonSerializer.Serialize(new AnalyticsSettings
        {
            SendAnonymousUsageData = config.SendAnonymousUsageData
        }, jsonOptions),

        BackupSettings = System.Text.Json.JsonSerializer.Serialize(new BackupSettings
        {
            BackupFolder = config.BackupFolder,
            BackupInterval = config.BackupInterval,
            BackupRetention = config.BackupRetention
        }, jsonOptions),

        UpdateSettings = System.Text.Json.JsonSerializer.Serialize(new UpdateSettings
        {
            Branch = config.Branch.ToLower(),
            Automatic = config.UpdateAutomatically, // Use Sonarr field name
            Mechanism = config.UpdateMechanism.ToLower(), // Use Sonarr field name
            ScriptPath = config.UpdateScriptPath // Use Sonarr field name
        }, jsonOptions),

        UISettings = System.Text.Json.JsonSerializer.Serialize(new UISettings
        {
            FirstDayOfWeek = config.FirstDayOfWeek.ToLower(),
            CalendarWeekColumnHeader = config.CalendarWeekColumnHeader,
            ShortDateFormat = config.ShortDateFormat,
            LongDateFormat = config.LongDateFormat,
            TimeFormat = config.TimeFormat,
            ShowRelativeDates = config.ShowRelativeDates,
            Theme = config.Theme.ToLower(),
            EnableColorImpairedMode = config.EnableColorImpairedMode,
            UILanguage = config.UILanguage,
            EventViewMode = config.EventViewMode,
            ShowUnknownLeagueItems = config.ShowUnknownLeagueItems,
            ShowEventPath = config.ShowEventPath,
            TimeZone = config.TimeZone
        }, jsonOptions),

        MediaManagementSettings = mediaSettingsJson,

        // Download handling settings (flat properties for frontend compatibility)
        EnableCompletedDownloadHandling = config.EnableCompletedDownloadHandling,
        // Note: RemoveCompletedDownloads and RemoveFailedDownloads are now per-client settings
        CheckForFinishedDownloadInterval = config.CheckForFinishedDownloadInterval,
        RedownloadFailedDownloads = config.RedownloadFailedDownloads,
        RedownloadFailedFromInteractiveSearch = config.RedownloadFailedFromInteractiveSearch,

        // Search Queue Management (Huntarr-style)
        MaxDownloadQueueSize = config.MaxDownloadQueueSize,
        SearchSleepDuration = config.SearchSleepDuration,

        // Indexer Options (advanced settings)
        IndexerRetention = config.IndexerRetention,
        RssSyncInterval = config.RssSyncInterval,
        PreferIndexerFlags = config.PreferIndexerFlags,
        SearchCacheDuration = config.SearchCacheDuration,
        IndexerMinimumAgeMinutes = config.IndexerMinimumAgeMinutes,

        // Development Settings (hidden)
        DevelopmentSettings = System.Text.Json.JsonSerializer.Serialize(new DevelopmentSettings
        {
            CustomMetadataApiUrl = config.CustomMetadataApiUrl
        }, jsonOptions),

        LastModified = DateTime.UtcNow
    };

    return Results.Ok(settings);
});

app.MapPut("/api/settings", async (AppSettings updatedSettings, ConfigService configService, SimpleAuthService simpleAuthService, SportarrDbContext db, FileFormatManager fileFormatManager, ILogger<Program> logger) =>
{
    logger.LogInformation("[CONFIG] Settings update requested");
    try
    {

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    // Parse all settings from frontend format
    var hostSettings = System.Text.Json.JsonSerializer.Deserialize<HostSettings>(updatedSettings.HostSettings, jsonOptions);
    var securitySettings = System.Text.Json.JsonSerializer.Deserialize<SecuritySettings>(updatedSettings.SecuritySettings, jsonOptions);
    var proxySettings = System.Text.Json.JsonSerializer.Deserialize<ProxySettings>(updatedSettings.ProxySettings, jsonOptions);
    var loggingSettings = System.Text.Json.JsonSerializer.Deserialize<LoggingSettings>(updatedSettings.LoggingSettings, jsonOptions);
    var analyticsSettings = System.Text.Json.JsonSerializer.Deserialize<AnalyticsSettings>(updatedSettings.AnalyticsSettings, jsonOptions);
    var backupSettings = System.Text.Json.JsonSerializer.Deserialize<BackupSettings>(updatedSettings.BackupSettings, jsonOptions);
    var updateSettingsObj = System.Text.Json.JsonSerializer.Deserialize<UpdateSettings>(updatedSettings.UpdateSettings, jsonOptions);
    var uiSettings = System.Text.Json.JsonSerializer.Deserialize<UISettings>(updatedSettings.UISettings, jsonOptions);
    var mediaManagementSettings = System.Text.Json.JsonSerializer.Deserialize<MediaManagementSettings>(updatedSettings.MediaManagementSettings, jsonOptions);
    var developmentSettings = !string.IsNullOrEmpty(updatedSettings.DevelopmentSettings)
        ? System.Text.Json.JsonSerializer.Deserialize<DevelopmentSettings>(updatedSettings.DevelopmentSettings, jsonOptions)
        : null;

    // Get previous EnableMultiPartEpisodes value to detect changes
    var config = await configService.GetConfigAsync();
    var previousEnableMultiPart = config.EnableMultiPartEpisodes;

    // CRITICAL: Validate credentials when enabling Forms or Basic authentication
    // This prevents users from locking themselves out
    if (securitySettings != null)
    {
        var authMethod = securitySettings.AuthenticationMethod?.ToLower() ?? "none";
        if (authMethod == "forms" || authMethod == "basic")
        {
            // Check if credentials already exist in config
            var hasExistingCredentials = !string.IsNullOrWhiteSpace(config.Username) &&
                                         !string.IsNullOrWhiteSpace(config.PasswordHash);

            // Check if user is providing new credentials
            var hasNewUsername = !string.IsNullOrWhiteSpace(securitySettings.Username);
            var hasNewPassword = !string.IsNullOrWhiteSpace(securitySettings.Password);

            if (!hasExistingCredentials && !hasNewUsername)
            {
                logger.LogWarning("[CONFIG] Rejected: Cannot enable {AuthMethod} authentication without username", authMethod);
                return Results.BadRequest(new { error = "Username is required when enabling authentication." });
            }

            if (!hasExistingCredentials && !hasNewPassword)
            {
                logger.LogWarning("[CONFIG] Rejected: Cannot enable {AuthMethod} authentication without password", authMethod);
                return Results.BadRequest(new { error = "Password is required when enabling authentication for the first time." });
            }

            if (hasNewPassword && securitySettings.Password!.Length < 6)
            {
                logger.LogWarning("[CONFIG] Rejected: Password too short");
                return Results.BadRequest(new { error = "Password must be at least 6 characters." });
            }
        }
    }

    // Handle password hashing if needed
    // Only call SetCredentialsAsync when BOTH username AND password are provided (user is setting new credentials)
    // If only username is provided (no password), it's just a settings save - credentials are managed via config.xml
    if (securitySettings != null &&
        !string.IsNullOrWhiteSpace(securitySettings.Username) &&
        !string.IsNullOrWhiteSpace(securitySettings.Password))
    {
        logger.LogInformation("[AUTH] Setting new credentials for user: {Username}", securitySettings.Username);
        await simpleAuthService.SetCredentialsAsync(securitySettings.Username, securitySettings.Password);
        logger.LogInformation("[AUTH] Credentials set successfully");
    }

    // Log incoming security settings for debugging
    logger.LogWarning("[CONFIG] *** SETTINGS SAVE REQUESTED ***");
    if (securitySettings != null)
    {
        logger.LogWarning("[CONFIG] Incoming SecuritySettings: AuthMethod={Method}, AuthRequired={Required}, Username={Username}",
            securitySettings.AuthenticationMethod ?? "(null)",
            securitySettings.AuthenticationRequired ?? "(null)",
            securitySettings.Username ?? "(null)");
    }
    else
    {
        logger.LogWarning("[CONFIG] SecuritySettings is NULL after deserialization!");
        logger.LogWarning("[CONFIG] Raw SecuritySettings JSON: {Json}", updatedSettings.SecuritySettings ?? "(null)");
    }

    // Update config.xml with all settings
    await configService.UpdateConfigAsync(config =>
    {
        logger.LogInformation("[CONFIG] Before update: AuthMethod={Method}, AuthRequired={Required}",
            config.AuthenticationMethod, config.AuthenticationRequired);

        if (hostSettings != null)
        {
            config.BindAddress = hostSettings.BindAddress;
            config.Port = hostSettings.Port;
            config.UrlBase = hostSettings.UrlBase;
            config.InstanceName = hostSettings.InstanceName;
            config.EnableSsl = hostSettings.EnableSsl;
            config.SslPort = hostSettings.SslPort;
            config.SslCertPath = hostSettings.SslCertPath;
            config.SslCertPassword = hostSettings.SslCertPassword;
        }

        if (securitySettings != null)
        {
            // Always update these core authentication settings from frontend
            config.AuthenticationMethod = securitySettings.AuthenticationMethod ?? config.AuthenticationMethod;
            config.AuthenticationRequired = securitySettings.AuthenticationRequired ?? config.AuthenticationRequired;
            config.Username = securitySettings.Username ?? config.Username;
            config.CertificateValidation = securitySettings.CertificateValidation ?? config.CertificateValidation;

            // Don't overwrite API key from frontend (it's read-only, managed by regenerate endpoint)

            // Password hash/salt are managed by SimpleAuthService.SetCredentialsAsync
            // Only update if explicitly provided (non-empty), otherwise keep existing values
            // Frontend doesn't send these fields, so they'll be null/empty
            if (!string.IsNullOrEmpty(securitySettings.PasswordHash))
            {
                config.PasswordHash = securitySettings.PasswordHash;
            }
            if (!string.IsNullOrEmpty(securitySettings.PasswordSalt))
            {
                config.PasswordSalt = securitySettings.PasswordSalt;
            }
            if (securitySettings.PasswordIterations > 0)
            {
                config.PasswordIterations = securitySettings.PasswordIterations;
            }

            logger.LogInformation("[CONFIG] After update: AuthMethod={Method}, AuthRequired={Required}",
                config.AuthenticationMethod, config.AuthenticationRequired);
        }

        if (proxySettings != null)
        {
            config.UseProxy = proxySettings.UseProxy;
            config.ProxyType = proxySettings.ProxyType;
            config.ProxyHostname = proxySettings.ProxyHostname;
            config.ProxyPort = proxySettings.ProxyPort;
            config.ProxyUsername = proxySettings.ProxyUsername;
            config.ProxyPassword = proxySettings.ProxyPassword;
            config.ProxyBypassFilter = proxySettings.ProxyBypassFilter;
            config.ProxyBypassLocalAddresses = proxySettings.ProxyBypassLocalAddresses;
        }

        if (loggingSettings != null)
        {
            config.LogLevel = loggingSettings.LogLevel;
        }

        if (analyticsSettings != null)
        {
            config.SendAnonymousUsageData = analyticsSettings.SendAnonymousUsageData;
        }

        if (backupSettings != null)
        {
            config.BackupFolder = backupSettings.BackupFolder;
            config.BackupInterval = backupSettings.BackupInterval;
            config.BackupRetention = backupSettings.BackupRetention;
        }

        if (updateSettingsObj != null)
        {
            config.Branch = updateSettingsObj.Branch;
            config.UpdateAutomatically = updateSettingsObj.Automatic; // Use Sonarr field name
            config.UpdateMechanism = updateSettingsObj.Mechanism; // Use Sonarr field name
            config.UpdateScriptPath = updateSettingsObj.ScriptPath; // Use Sonarr field name
        }

        if (uiSettings != null)
        {
            config.FirstDayOfWeek = uiSettings.FirstDayOfWeek;
            config.CalendarWeekColumnHeader = uiSettings.CalendarWeekColumnHeader;
            config.ShortDateFormat = uiSettings.ShortDateFormat;
            config.LongDateFormat = uiSettings.LongDateFormat;
            config.TimeFormat = uiSettings.TimeFormat;
            config.ShowRelativeDates = uiSettings.ShowRelativeDates;
            config.Theme = uiSettings.Theme;
            config.EnableColorImpairedMode = uiSettings.EnableColorImpairedMode;
            config.UILanguage = uiSettings.UILanguage;
            config.EventViewMode = uiSettings.EventViewMode;
            config.ShowUnknownLeagueItems = uiSettings.ShowUnknownLeagueItems;
            config.ShowEventPath = uiSettings.ShowEventPath;
            config.TimeZone = uiSettings.TimeZone;
        }

        if (mediaManagementSettings != null)
        {
            config.RenameEvents = mediaManagementSettings.RenameEvents;
            config.ReplaceIllegalCharacters = mediaManagementSettings.ReplaceIllegalCharacters;
            config.EnableMultiPartEpisodes = mediaManagementSettings.EnableMultiPartEpisodes;
            config.CreateEventFolders = mediaManagementSettings.CreateEventFolders;
            config.DeleteEmptyFolders = mediaManagementSettings.DeleteEmptyFolders;
            config.SkipFreeSpaceCheck = mediaManagementSettings.SkipFreeSpaceCheck;
            config.MinimumFreeSpace = (int)mediaManagementSettings.MinimumFreeSpace;
            config.UseHardlinks = mediaManagementSettings.UseHardlinks;
            config.ImportExtraFiles = mediaManagementSettings.ImportExtraFiles;
            config.ExtraFileExtensions = mediaManagementSettings.ExtraFileExtensions;
            config.ChangeFileDate = mediaManagementSettings.ChangeFileDate;
            config.RecycleBin = mediaManagementSettings.RecycleBin;
            config.RecycleBinCleanup = mediaManagementSettings.RecycleBinCleanup;
            config.SetPermissions = mediaManagementSettings.SetPermissions;
            config.ChmodFolder = mediaManagementSettings.ChmodFolder;
            config.ChownGroup = mediaManagementSettings.ChownGroup;
        }

        // Download handling settings (flat properties from frontend)
        config.EnableCompletedDownloadHandling = updatedSettings.EnableCompletedDownloadHandling;
        // Note: RemoveCompletedDownloads and RemoveFailedDownloads are now per-client settings
        config.CheckForFinishedDownloadInterval = updatedSettings.CheckForFinishedDownloadInterval;
        config.RedownloadFailedDownloads = updatedSettings.RedownloadFailedDownloads;
        config.RedownloadFailedFromInteractiveSearch = updatedSettings.RedownloadFailedFromInteractiveSearch;

        // Search Queue Management (Huntarr-style)
        config.MaxDownloadQueueSize = updatedSettings.MaxDownloadQueueSize;
        config.SearchSleepDuration = updatedSettings.SearchSleepDuration;

        // Indexer Options (advanced settings)
        config.IndexerRetention = updatedSettings.IndexerRetention;
        config.RssSyncInterval = Math.Max(10, updatedSettings.RssSyncInterval); // Enforce minimum of 10 minutes
        config.PreferIndexerFlags = updatedSettings.PreferIndexerFlags;
        config.SearchCacheDuration = Math.Max(10, updatedSettings.SearchCacheDuration); // Enforce minimum of 10 seconds
        config.IndexerMinimumAgeMinutes = Math.Max(0, updatedSettings.IndexerMinimumAgeMinutes); // Sonarr-style: clamp at 0 (no negative delays)

        // Development Settings (hidden)
        if (developmentSettings != null)
        {
            config.CustomMetadataApiUrl = developmentSettings.CustomMetadataApiUrl ?? "";
            logger.LogInformation("[CONFIG] Development settings updated: CustomMetadataApiUrl={Url}",
                string.IsNullOrEmpty(config.CustomMetadataApiUrl) ? "(default)" : config.CustomMetadataApiUrl);
        }
    });

    // Update MediaManagementSettings in database
    if (mediaManagementSettings != null)
    {
        // Debug logging to diagnose folder settings save issue
        logger.LogInformation("[CONFIG] Folder settings from request - CreateLeagueFolders={League}, CreateSeasonFolders={Season}, CreateEventFolders={Event}, ReorganizeFolders={Reorg}",
            mediaManagementSettings.CreateLeagueFolders,
            mediaManagementSettings.CreateSeasonFolders,
            mediaManagementSettings.CreateEventFolders,
            mediaManagementSettings.ReorganizeFolders);

        var dbSettings = await db.MediaManagementSettings.FirstOrDefaultAsync();
        if (dbSettings == null)
        {
            // Create new settings row if it doesn't exist
            dbSettings = new MediaManagementSettings
            {
                RenameFiles = mediaManagementSettings.RenameFiles,
                StandardFileFormat = mediaManagementSettings.StandardFileFormat ?? "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
                // Granular folder format settings
                LeagueFolderFormat = mediaManagementSettings.LeagueFolderFormat ?? "{Series}",
                SeasonFolderFormat = mediaManagementSettings.SeasonFolderFormat ?? "Season {Season}",
                EventFolderFormat = mediaManagementSettings.EventFolderFormat ?? "{Event Title} ({Year}-{Month}-{Day}) E{Episode}",
                RenameEvents = mediaManagementSettings.RenameEvents,
                ReplaceIllegalCharacters = mediaManagementSettings.ReplaceIllegalCharacters,
                // Granular folder creation settings
                CreateLeagueFolders = mediaManagementSettings.CreateLeagueFolders,
                CreateSeasonFolders = mediaManagementSettings.CreateSeasonFolders,
                CreateEventFolders = mediaManagementSettings.CreateEventFolders,
                ReorganizeFolders = mediaManagementSettings.ReorganizeFolders,
                DeleteEmptyFolders = mediaManagementSettings.DeleteEmptyFolders,
                SkipFreeSpaceCheck = mediaManagementSettings.SkipFreeSpaceCheck,
                MinimumFreeSpace = mediaManagementSettings.MinimumFreeSpace,
                UseHardlinks = mediaManagementSettings.UseHardlinks,
                ImportExtraFiles = mediaManagementSettings.ImportExtraFiles,
                ExtraFileExtensions = mediaManagementSettings.ExtraFileExtensions ?? "srt,nfo",
                ChangeFileDate = mediaManagementSettings.ChangeFileDate ?? "None",
                RecycleBin = mediaManagementSettings.RecycleBin ?? "",
                RecycleBinCleanup = mediaManagementSettings.RecycleBinCleanup,
                SetPermissions = mediaManagementSettings.SetPermissions,
                FileChmod = mediaManagementSettings.FileChmod ?? "644",
                ChmodFolder = mediaManagementSettings.ChmodFolder ?? "755",
                ChownUser = mediaManagementSettings.ChownUser ?? "",
                ChownGroup = mediaManagementSettings.ChownGroup ?? "",
                CopyFiles = mediaManagementSettings.CopyFiles,
                // Note: RemoveCompletedDownloads and RemoveFailedDownloads are now per-client settings
                LastModified = DateTime.UtcNow
            };
            db.MediaManagementSettings.Add(dbSettings);
            logger.LogInformation("[CONFIG] MediaManagementSettings created in database");
        }
        else
        {
            // Update existing settings
            dbSettings.RenameFiles = mediaManagementSettings.RenameFiles;
            dbSettings.StandardFileFormat = mediaManagementSettings.StandardFileFormat;
            // Granular folder format settings
            dbSettings.LeagueFolderFormat = mediaManagementSettings.LeagueFolderFormat;
            dbSettings.SeasonFolderFormat = mediaManagementSettings.SeasonFolderFormat;
            dbSettings.EventFolderFormat = mediaManagementSettings.EventFolderFormat;
            dbSettings.RenameEvents = mediaManagementSettings.RenameEvents;
            dbSettings.ReplaceIllegalCharacters = mediaManagementSettings.ReplaceIllegalCharacters;
            // Granular folder creation settings
            dbSettings.CreateLeagueFolders = mediaManagementSettings.CreateLeagueFolders;
            dbSettings.CreateSeasonFolders = mediaManagementSettings.CreateSeasonFolders;
            dbSettings.CreateEventFolders = mediaManagementSettings.CreateEventFolders;
            dbSettings.ReorganizeFolders = mediaManagementSettings.ReorganizeFolders;
            dbSettings.DeleteEmptyFolders = mediaManagementSettings.DeleteEmptyFolders;
            dbSettings.SkipFreeSpaceCheck = mediaManagementSettings.SkipFreeSpaceCheck;
            dbSettings.MinimumFreeSpace = mediaManagementSettings.MinimumFreeSpace;
            dbSettings.UseHardlinks = mediaManagementSettings.UseHardlinks;
            dbSettings.ImportExtraFiles = mediaManagementSettings.ImportExtraFiles;
            dbSettings.ExtraFileExtensions = mediaManagementSettings.ExtraFileExtensions;
            dbSettings.ChangeFileDate = mediaManagementSettings.ChangeFileDate;
            dbSettings.RecycleBin = mediaManagementSettings.RecycleBin;
            dbSettings.RecycleBinCleanup = mediaManagementSettings.RecycleBinCleanup;
            dbSettings.SetPermissions = mediaManagementSettings.SetPermissions;
            dbSettings.FileChmod = mediaManagementSettings.FileChmod;
            dbSettings.ChmodFolder = mediaManagementSettings.ChmodFolder;
            dbSettings.ChownUser = mediaManagementSettings.ChownUser;
            dbSettings.ChownGroup = mediaManagementSettings.ChownGroup;
            dbSettings.CopyFiles = mediaManagementSettings.CopyFiles;
            // Note: RemoveCompletedDownloads and RemoveFailedDownloads are now per-client settings
            dbSettings.LastModified = DateTime.UtcNow;
            logger.LogInformation("[CONFIG] MediaManagementSettings updated in database");
        }

        await db.SaveChangesAsync();

        // Verify the save by re-reading from database
        var verifySettings = await db.MediaManagementSettings.FirstOrDefaultAsync();
        if (verifySettings != null)
        {
            logger.LogInformation("[CONFIG] Verified folder settings after save - CreateLeagueFolders={League}, CreateSeasonFolders={Season}, CreateEventFolders={Event}, ReorganizeFolders={Reorg}",
                verifySettings.CreateLeagueFolders,
                verifySettings.CreateSeasonFolders,
                verifySettings.CreateEventFolders,
                verifySettings.ReorganizeFolders);
        }
    }

    // Auto-manage {Part} token when EnableMultiPartEpisodes changes
    var updatedConfig = await configService.GetConfigAsync();
    if (updatedConfig.EnableMultiPartEpisodes != previousEnableMultiPart)
    {
        logger.LogInformation("[CONFIG] EnableMultiPartEpisodes changed from {Old} to {New} - updating file format",
            previousEnableMultiPart, updatedConfig.EnableMultiPartEpisodes);
        await fileFormatManager.UpdateFileFormatForMultiPartSetting(updatedConfig.EnableMultiPartEpisodes);
    }

    // CRITICAL: Sync SecuritySettings to database (used by DynamicAuthenticationMiddleware)
    // The middleware reads from db.AppSettings.SecuritySettings, not config.xml
    if (securitySettings != null)
    {
        logger.LogInformation("[CONFIG] Syncing SecuritySettings to database for authentication middleware");

        var appSettings = await db.AppSettings.FirstOrDefaultAsync();
        if (appSettings == null)
        {
            appSettings = new AppSettings { Id = 1 };
            db.AppSettings.Add(appSettings);
        }

        // Get fresh config to ensure we have updated password hash from SimpleAuthService
        var freshConfig = await configService.GetConfigAsync();

        // Create SecuritySettings JSON for database (using database-format property names)
        var dbSecuritySettings = new SecuritySettings
        {
            AuthenticationMethod = freshConfig.AuthenticationMethod?.ToLower() ?? "none",
            AuthenticationRequired = freshConfig.AuthenticationRequired?.ToLower() ?? "disabledforlocaladdresses",
            Username = freshConfig.Username ?? "",
            Password = "", // Never store plaintext
            ApiKey = freshConfig.ApiKey ?? "",
            CertificateValidation = freshConfig.CertificateValidation?.ToLower() ?? "enabled",
            PasswordHash = freshConfig.PasswordHash ?? "",
            PasswordSalt = freshConfig.PasswordSalt ?? "",
            PasswordIterations = freshConfig.PasswordIterations > 0 ? freshConfig.PasswordIterations : 10000
        };

        appSettings.SecuritySettings = System.Text.Json.JsonSerializer.Serialize(dbSecuritySettings);
        await db.SaveChangesAsync();

        logger.LogInformation("[CONFIG] SecuritySettings synced to database: AuthMethod={Method}, AuthRequired={Required}",
            dbSecuritySettings.AuthenticationMethod, dbSecuritySettings.AuthenticationRequired);
    }

    logger.LogInformation("[CONFIG] Settings saved to config.xml successfully");
    return Results.Ok(updatedSettings);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[CONFIG] Failed to save settings");
        return Results.Problem($"Failed to save settings: {ex.Message}");
    }
});

// API: Regenerate API Key (Sonarr pattern - no restart required)
app.MapPost("/api/settings/apikey/regenerate", async (ConfigService configService, ILogger<Program> logger) =>
{
    logger.LogWarning("[API KEY] API key regeneration requested");
    var newApiKey = await configService.RegenerateApiKeyAsync();
    logger.LogWarning("[API KEY] API key regenerated and saved to config.xml - all connected applications must be updated!");
    return Results.Ok(new { apiKey = newApiKey, message = "API key regenerated successfully. Update all connected applications (Prowlarr, download clients, etc.) with the new key." });
});

        return app;
    }
}
