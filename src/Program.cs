using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Models.Metadata;
using Sportarr.Api.Services;
using Sportarr.Api.Middleware;
using Sportarr.Api.Helpers;
using Serilog;
using Serilog.Events;
using System.Text.Json;
using Polly;
using Polly.Extensions.Http;

// Pre-configure builder to read configuration before setting up Serilog
var preBuilder = WebApplication.CreateBuilder(args);

// Configuration - get data path first so logs go in the right place
var apiKey = preBuilder.Configuration["Sportarr:ApiKey"] ?? Guid.NewGuid().ToString("N");
var dataPath = preBuilder.Configuration["Sportarr:DataPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");

try
{
    Directory.CreateDirectory(dataPath);
    Console.WriteLine($"[Sportarr] Data directory: {dataPath}");
}
catch (Exception ex)
{
    Console.WriteLine($"[Sportarr] ERROR: Failed to create data directory: {ex.Message}");
    throw;
}

// Configure Serilog with logs inside the data directory (like Sonarr)
// This ensures logs are accessible in Docker when user maps their config volume
var logsPath = Path.Combine(dataPath, "logs");
Directory.CreateDirectory(logsPath);
Console.WriteLine($"[Sportarr] Logs directory: {logsPath}");

// Output template for logs (shared between console and file)
var outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";

// Create sanitizing formatter to protect sensitive data
var sanitizingFormatter = new SanitizingTextFormatter(outputTemplate);

// Configure Serilog like Sonarr:
// - Main log file: sportarr.txt with rolling by size (1MB like Sonarr) and day
// - Retained file count: 31 files (like Sonarr default)
// - When file reaches size limit, rolls to sportarr_001.txt, sportarr_002.txt, etc.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatter: sanitizingFormatter)
    .WriteTo.File(
        formatter: sanitizingFormatter,
        path: Path.Combine(logsPath, "sportarr.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31,           // Keep 31 files like Sonarr
        fileSizeLimitBytes: 1048576,          // 1MB per file like Sonarr
        rollOnFileSizeLimit: true,            // Roll when size limit reached
        shared: true,                         // Allow multiple processes to write
        flushToDiskInterval: TimeSpan.FromSeconds(1))
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for all logging
builder.Host.UseSerilog();

builder.Configuration["Sportarr:ApiKey"] = apiKey;

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient(); // For calling Sportarr-API

// Configure HttpClient for TRaSH Guides GitHub API with proper User-Agent
builder.Services.AddHttpClient("TrashGuides")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0 (https://github.com/Sportarr/Sportarr)");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    });

// Register rate limiting service (Sonarr-style HTTP-level rate limiting)
builder.Services.AddSingleton<IRateLimitService, RateLimitService>();

// Register RateLimitHandler as transient (one per HttpClient)
builder.Services.AddTransient<RateLimitHandler>();

// Configure HttpClient for indexer searches with rate limiting and Polly retry policy
// Rate limiting is now handled at the HTTP layer via RateLimitHandler, matching Sonarr/Radarr
builder.Services.AddHttpClient("IndexerClient")
    .AddHttpMessageHandler<RateLimitHandler>()  // Rate limiting at HTTP layer
    .AddTransientHttpErrorPolicy(policyBuilder =>
        policyBuilder.WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                Console.WriteLine($"[Indexer] Retry {retryCount} after {timespan.TotalSeconds}s due to {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
            }))
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

builder.Services.AddControllers(); // Add MVC controllers for AuthenticationController
// Configure minimal API JSON options - serialize enums as integers for frontend compatibility
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Use camelCase for JSON property names to match frontend expectations
    // Frontend sends: { externalId: "...", name: "..." }
    // Backend has: { ExternalId, Name } with PascalCase properties
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

    // Enable case-insensitive property name matching for JSON deserialization
    // This allows TheSportsDB API responses (idLeague, strLeague) to map to our League model
    options.SerializerOptions.PropertyNameCaseInsensitive = true;

    // Handle circular references (e.g., Event -> League -> Events -> Event)
    // This prevents serialization errors when navigation properties create cycles
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;

    // DO NOT add JsonStringEnumConverter - we need numeric enum values for frontend
    // The frontend expects type: 5 (number), not type: "Sabnzbd" (string)
});
builder.Services.AddSingleton<Sportarr.Api.Services.ConfigService>();
builder.Services.AddScoped<Sportarr.Api.Services.UserService>();
builder.Services.AddScoped<Sportarr.Api.Services.AuthenticationService>();
builder.Services.AddScoped<Sportarr.Api.Services.SimpleAuthService>();
builder.Services.AddScoped<Sportarr.Api.Services.SessionService>();
builder.Services.AddScoped<Sportarr.Api.Services.DownloadClientService>();
builder.Services.AddScoped<Sportarr.Api.Services.IndexerStatusService>(); // Sonarr-style indexer health tracking and backoff
builder.Services.AddScoped<Sportarr.Api.Services.IndexerSearchService>();
builder.Services.AddScoped<Sportarr.Api.Services.ReleaseMatchingService>(); // Sonarr-style release validation to prevent downloading wrong content
builder.Services.AddSingleton<Sportarr.Api.Services.SearchCacheService>(); // Caches search results to prevent duplicate API calls
builder.Services.AddSingleton<Sportarr.Api.Services.SearchQueueService>(); // Queue for parallel search execution
builder.Services.AddScoped<Sportarr.Api.Services.AutomaticSearchService>();
builder.Services.AddScoped<Sportarr.Api.Services.DelayProfileService>();
builder.Services.AddScoped<Sportarr.Api.Services.QualityDetectionService>();
builder.Services.AddScoped<Sportarr.Api.Services.ReleaseEvaluator>();
builder.Services.AddScoped<Sportarr.Api.Services.MediaFileParser>();
builder.Services.AddScoped<Sportarr.Api.Services.SportsFileNameParser>(); // Sports-specific filename parsing (UFC, WWE, NFL, etc.)
builder.Services.AddScoped<Sportarr.Api.Services.FileNamingService>();
builder.Services.AddScoped<Sportarr.Api.Services.FileRenameService>(); // Auto-renames files when event metadata changes
builder.Services.AddScoped<Sportarr.Api.Services.EventPartDetector>(); // Multi-part episode detection for Fighting sports
builder.Services.AddScoped<Sportarr.Api.Services.FileFormatManager>(); // Auto-manages {Part} token in file format
builder.Services.AddScoped<Sportarr.Api.Services.FileImportService>();
builder.Services.AddScoped<Sportarr.Api.Services.ImportMatchingService>(); // Matches external downloads to events
builder.Services.AddScoped<Sportarr.Api.Services.ExternalDownloadScanner>(); // Scans download clients for external downloads
builder.Services.AddScoped<Sportarr.Api.Services.CustomFormatService>();
builder.Services.AddScoped<Sportarr.Api.Services.TrashGuideSyncService>(); // TRaSH Guides sync for custom formats and scores
builder.Services.AddHostedService<Sportarr.Api.Services.TrashSyncBackgroundService>(); // TRaSH Guides auto-sync background service
builder.Services.AddScoped<Sportarr.Api.Services.HealthCheckService>();
builder.Services.AddScoped<Sportarr.Api.Services.BackupService>();
builder.Services.AddScoped<Sportarr.Api.Services.LibraryImportService>();
builder.Services.AddScoped<Sportarr.Api.Services.ImportListService>();
builder.Services.AddScoped<Sportarr.Api.Services.ImportService>(); // Handles completed download imports
builder.Services.AddScoped<Sportarr.Api.Services.ProvideImportItemService>(); // Provides import items with path translation
builder.Services.AddScoped<Sportarr.Api.Services.EventQueryService>(); // Universal: Sport-aware query builder for all sports
builder.Services.AddScoped<Sportarr.Api.Services.LeagueEventSyncService>(); // Syncs events from TheSportsDB to populate leagues
builder.Services.AddHostedService<Sportarr.Api.Services.LeagueEventAutoSyncService>(); // Background service for automatic periodic event sync

// TheSportsDB client for universal sports metadata (via Sportarr-API)
builder.Services.AddHttpClient<Sportarr.Api.Services.TheSportsDBClient>()
    .AddTransientHttpErrorPolicy(policyBuilder =>
        policyBuilder.WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Console.WriteLine($"[TheSportsDB] Retry {retryAttempt} after {timespan.TotalSeconds}s delay");
            }
        ));

builder.Services.AddSingleton<Sportarr.Api.Services.TaskService>();
builder.Services.AddHostedService<Sportarr.Api.Services.EnhancedDownloadMonitorService>(); // Unified download monitoring with retry, blocklist, and auto-import
builder.Services.AddHostedService<Sportarr.Api.Services.RssSyncService>(); // Automatic RSS sync for new releases
builder.Services.AddHostedService<Sportarr.Api.Services.TvScheduleSyncService>(); // TV schedule sync for automatic search timing
builder.Services.AddHostedService<Sportarr.Api.Services.DiskScanService>(); // Periodic file existence verification (Sonarr-style disk scan)


// Add ASP.NET Core Authentication (Sonarr/Radarr pattern)
Sportarr.Api.Authentication.AuthenticationBuilderExtensions.AddAppAuthentication(builder.Services);

// Configure database
var dbPath = Path.Combine(dataPath, "sportarr.db");
Console.WriteLine($"[Sportarr] Database path: {dbPath}");
builder.Services.AddDbContext<SportarrDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}")
           .ConfigureWarnings(w => w
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning)
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning)));
// Add DbContextFactory for concurrent database access (used by IndexerStatusService for parallel indexer searches)
builder.Services.AddDbContextFactory<SportarrDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}")
           .ConfigureWarnings(w => w
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning)
               .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning)), ServiceLifetime.Scoped);

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Apply database migrations automatically on startup
try
{
    Console.WriteLine("[Sportarr] Applying database migrations...");
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();

        // Check if database exists and has tables but no migration history
        // This happens when database was created with EnsureCreated() instead of Migrate()
        var canConnect = db.Database.CanConnect();
        var hasMigrationHistory = canConnect && db.Database.GetAppliedMigrations().Any();

        // Check if AppSettings table exists (core table that should always be present)
        bool hasTables = false;
        if (canConnect)
        {
            using var connection = db.Database.GetDbConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AppSettings'";
            hasTables = Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        if (canConnect && hasTables && !hasMigrationHistory)
        {
            // Database was created with EnsureCreated() - we need to seed the migration history
            // to prevent migrations from trying to recreate existing tables
            Console.WriteLine("[Sportarr] Detected database created without migrations. Seeding migration history...");

            // Get all migrations that exist in the codebase
            var allMigrations = db.Database.GetMigrations().ToList();

            // Mark all existing migrations as applied (since tables already exist)
            // We'll use a raw SQL approach since the history table doesn't exist yet
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                    ""MigrationId"" TEXT NOT NULL,
                    ""ProductVersion"" TEXT NOT NULL,
                    CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
                )");

            // Insert all migrations as applied (using parameterized query to prevent SQL injection)
            foreach (var migration in allMigrations)
            {
                try
                {
                    db.Database.ExecuteSqlInterpolated(
                        $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migration}, '8.0.0')");
                    Console.WriteLine($"[Sportarr] Marked migration as applied: {migration}");
                }
                catch
                {
                    // Migration might already be in history, skip
                }
            }

            Console.WriteLine("[Sportarr] Migration history seeded successfully");
        }

        // Now apply any new migrations
        db.Database.Migrate();

        // Ensure MonitoredParts column exists in Leagues table (backwards compatibility fix)
        // This handles cases where migrations were applied but column wasn't created
        try
        {
            var checkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('Leagues') WHERE name='MonitoredParts'";
            var columnExists = db.Database.SqlQueryRaw<int>(checkColumnSql).AsEnumerable().FirstOrDefault();

            if (columnExists == 0)
            {
                Console.WriteLine("[Sportarr] Leagues.MonitoredParts column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE Leagues ADD COLUMN MonitoredParts TEXT");
                Console.WriteLine("[Sportarr] Leagues.MonitoredParts column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify Leagues.MonitoredParts column: {ex.Message}");
        }

        // Ensure MonitoredParts column exists in Events table (backwards compatibility fix)
        try
        {
            var checkEventColumnSql = "SELECT COUNT(*) FROM pragma_table_info('Events') WHERE name='MonitoredParts'";
            var eventColumnExists = db.Database.SqlQueryRaw<int>(checkEventColumnSql).AsEnumerable().FirstOrDefault();

            if (eventColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] Events.MonitoredParts column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE Events ADD COLUMN MonitoredParts TEXT");
                Console.WriteLine("[Sportarr] Events.MonitoredParts column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify Events.MonitoredParts column: {ex.Message}");
        }

        // Ensure DisableSslCertificateValidation column exists in DownloadClients table (backwards compatibility fix)
        try
        {
            var checkSslColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadClients') WHERE name='DisableSslCertificateValidation'";
            var sslColumnExists = db.Database.SqlQueryRaw<int>(checkSslColumnSql).AsEnumerable().FirstOrDefault();

            if (sslColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadClients.DisableSslCertificateValidation column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadClients ADD COLUMN DisableSslCertificateValidation INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] DownloadClients.DisableSslCertificateValidation column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify DownloadClients.DisableSslCertificateValidation column: {ex.Message}");
        }

        // Ensure SequentialDownload and FirstAndLastFirst columns exist in DownloadClients table (debrid service support)
        try
        {
            var checkSeqColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadClients') WHERE name='SequentialDownload'";
            var seqColumnExists = db.Database.SqlQueryRaw<int>(checkSeqColumnSql).AsEnumerable().FirstOrDefault();

            if (seqColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadClients.SequentialDownload column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadClients ADD COLUMN SequentialDownload INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] DownloadClients.SequentialDownload column added successfully");
            }

            var checkFirstLastColumnSql = "SELECT COUNT(*) FROM pragma_table_info('DownloadClients') WHERE name='FirstAndLastFirst'";
            var firstLastColumnExists = db.Database.SqlQueryRaw<int>(checkFirstLastColumnSql).AsEnumerable().FirstOrDefault();

            if (firstLastColumnExists == 0)
            {
                Console.WriteLine("[Sportarr] DownloadClients.FirstAndLastFirst column missing - adding it now...");
                db.Database.ExecuteSqlRaw("ALTER TABLE DownloadClients ADD COLUMN FirstAndLastFirst INTEGER NOT NULL DEFAULT 0");
                Console.WriteLine("[Sportarr] DownloadClients.FirstAndLastFirst column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify DownloadClients sequential download columns: {ex.Message}");
        }

        // Remove deprecated UseSymlinks column from MediaManagementSettings if it exists
        // (Decypharr handles symlinks itself, Sportarr doesn't need this setting)
        try
        {
            var checkSymlinkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('MediaManagementSettings') WHERE name='UseSymlinks'";
            var symlinkColumnExists = db.Database.SqlQueryRaw<int>(checkSymlinkColumnSql).AsEnumerable().FirstOrDefault();

            if (symlinkColumnExists > 0)
            {
                Console.WriteLine("[Sportarr] Removing deprecated UseSymlinks column from MediaManagementSettings...");
                // SQLite doesn't support DROP COLUMN directly before 3.35.0, so we need to recreate the table
                // However, EF Core will simply ignore the extra column, so we can leave it for now
                // The column won't be used and will be cleaned up on next migration
                Console.WriteLine("[Sportarr] UseSymlinks column will be ignored (deprecated setting removed)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not check for deprecated UseSymlinks column: {ex.Message}");
        }

        // Ensure EventFiles table exists (backwards compatibility fix for file tracking)
        // This handles cases where migration history was seeded before EventFiles migration existed
        try
        {
            var checkTableSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='EventFiles'";
            var tableExists = db.Database.SqlQueryRaw<int>(checkTableSql).AsEnumerable().FirstOrDefault();

            if (tableExists == 0)
            {
                Console.WriteLine("[Sportarr] EventFiles table missing - creating it now...");

                // Create EventFiles table with all columns and indexes
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE ""EventFiles"" (
                        ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""EventId"" INTEGER NOT NULL,
                        ""FilePath"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL,
                        ""Quality"" TEXT NULL,
                        ""PartName"" TEXT NULL,
                        ""PartNumber"" INTEGER NULL,
                        ""Added"" TEXT NOT NULL,
                        ""LastVerified"" TEXT NULL,
                        ""Exists"" INTEGER NOT NULL DEFAULT 1,
                        CONSTRAINT ""FK_EventFiles_Events_EventId"" FOREIGN KEY (""EventId"") REFERENCES ""Events"" (""Id"") ON DELETE CASCADE
                    )");

                // Create indexes
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_EventFiles_EventId"" ON ""EventFiles"" (""EventId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_EventFiles_PartNumber"" ON ""EventFiles"" (""PartNumber"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_EventFiles_Exists"" ON ""EventFiles"" (""Exists"")");

                Console.WriteLine("[Sportarr] EventFiles table created successfully");
                Console.WriteLine("[Sportarr] File tracking is now enabled for all sports");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify EventFiles table: {ex.Message}");
        }

        // Ensure PendingImports table exists (for external download detection feature)
        try
        {
            var checkTableSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PendingImports'";
            var tableExists = db.Database.SqlQueryRaw<int>(checkTableSql).AsEnumerable().FirstOrDefault();

            if (tableExists == 0)
            {
                Console.WriteLine("[Sportarr] PendingImports table missing - creating it now...");

                // Create PendingImports table with all columns
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE ""PendingImports"" (
                        ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""DownloadClientId"" INTEGER NOT NULL,
                        ""DownloadId"" TEXT NOT NULL,
                        ""Title"" TEXT NOT NULL,
                        ""FilePath"" TEXT NOT NULL,
                        ""Size"" INTEGER NOT NULL DEFAULT 0,
                        ""Quality"" TEXT NULL,
                        ""QualityScore"" INTEGER NOT NULL DEFAULT 0,
                        ""Status"" INTEGER NOT NULL DEFAULT 0,
                        ""ErrorMessage"" TEXT NULL,
                        ""SuggestedEventId"" INTEGER NULL,
                        ""SuggestedPart"" TEXT NULL,
                        ""SuggestionConfidence"" INTEGER NOT NULL DEFAULT 0,
                        ""Detected"" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        ""ResolvedAt"" TEXT NULL,
                        ""Protocol"" TEXT NULL,
                        ""TorrentInfoHash"" TEXT NULL,
                        CONSTRAINT ""FK_PendingImports_DownloadClients_DownloadClientId"" FOREIGN KEY (""DownloadClientId"") REFERENCES ""DownloadClients"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_PendingImports_Events_SuggestedEventId"" FOREIGN KEY (""SuggestedEventId"") REFERENCES ""Events"" (""Id"") ON DELETE SET NULL
                    )");

                // Create indexes
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_DownloadClientId"" ON ""PendingImports"" (""DownloadClientId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_SuggestedEventId"" ON ""PendingImports"" (""SuggestedEventId"")");
                db.Database.ExecuteSqlRaw(@"CREATE INDEX ""IX_PendingImports_Status"" ON ""PendingImports"" (""Status"")");

                Console.WriteLine("[Sportarr] PendingImports table created successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify PendingImports table: {ex.Message}");
        }

        // Ensure EnableMultiPartEpisodes column exists in MediaManagementSettings (backwards compatibility fix)
        // This handles cases where migration history was seeded before the column was added
        try
        {
            var checkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('MediaManagementSettings') WHERE name='EnableMultiPartEpisodes'";
            var columnExists = db.Database.SqlQueryRaw<int>(checkColumnSql).AsEnumerable().FirstOrDefault();

            if (columnExists == 0)
            {
                Console.WriteLine("[Sportarr] EnableMultiPartEpisodes column missing - adding it now...");
                db.Database.ExecuteSqlRaw(@"ALTER TABLE ""MediaManagementSettings"" ADD COLUMN ""EnableMultiPartEpisodes"" INTEGER NOT NULL DEFAULT 1");
                Console.WriteLine("[Sportarr] EnableMultiPartEpisodes column added successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not verify EnableMultiPartEpisodes column: {ex.Message}");
        }

        // Remove deprecated StandardEventFormat column if it exists (backwards compatibility fix)
        // This column was removed but migration may not have run properly on some databases
        try
        {
            var checkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('MediaManagementSettings') WHERE name='StandardEventFormat'";
            var columnExists = db.Database.SqlQueryRaw<int>(checkColumnSql).AsEnumerable().FirstOrDefault();

            if (columnExists > 0)
            {
                Console.WriteLine("[Sportarr] Removing deprecated StandardEventFormat column from MediaManagementSettings...");
                // SQLite doesn't support DROP COLUMN directly, so we need to recreate the table
                // Note: Using single quotes for SQL string literals (not C# interpolation)
                var createTableSql = @"
                    CREATE TABLE IF NOT EXISTS MediaManagementSettings_new (
                        Id INTEGER PRIMARY KEY,
                        RenameFiles INTEGER NOT NULL DEFAULT 1,
                        StandardFileFormat TEXT NOT NULL DEFAULT '{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}',
                        EventFolderFormat TEXT NOT NULL DEFAULT '{Series}/Season {Season}',
                        CreateEventFolder INTEGER NOT NULL DEFAULT 1,
                        RenameEvents INTEGER NOT NULL DEFAULT 0,
                        ReplaceIllegalCharacters INTEGER NOT NULL DEFAULT 1,
                        CreateEventFolders INTEGER NOT NULL DEFAULT 1,
                        DeleteEmptyFolders INTEGER NOT NULL DEFAULT 0,
                        SkipFreeSpaceCheck INTEGER NOT NULL DEFAULT 0,
                        MinimumFreeSpace INTEGER NOT NULL DEFAULT 100,
                        UseHardlinks INTEGER NOT NULL DEFAULT 1,
                        ImportExtraFiles INTEGER NOT NULL DEFAULT 0,
                        ExtraFileExtensions TEXT NOT NULL DEFAULT 'srt,nfo',
                        ChangeFileDate TEXT NOT NULL DEFAULT 'None',
                        RecycleBin TEXT NOT NULL DEFAULT '',
                        RecycleBinCleanup INTEGER NOT NULL DEFAULT 7,
                        SetPermissions INTEGER NOT NULL DEFAULT 0,
                        FileChmod TEXT NOT NULL DEFAULT '644',
                        ChmodFolder TEXT NOT NULL DEFAULT '755',
                        ChownUser TEXT NOT NULL DEFAULT '',
                        ChownGroup TEXT NOT NULL DEFAULT '',
                        CopyFiles INTEGER NOT NULL DEFAULT 0,
                        RemoveCompletedDownloads INTEGER NOT NULL DEFAULT 1,
                        RemoveFailedDownloads INTEGER NOT NULL DEFAULT 1,
                        Created TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        LastModified TEXT,
                        EnableMultiPartEpisodes INTEGER NOT NULL DEFAULT 1,
                        RootFolders TEXT NOT NULL DEFAULT '[]'
                    )";

                using var connection = db.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    connection.Open();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = createTableSql;
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO MediaManagementSettings_new (
                            Id, RenameFiles, StandardFileFormat, EventFolderFormat, CreateEventFolder,
                            RenameEvents, ReplaceIllegalCharacters, CreateEventFolders, DeleteEmptyFolders,
                            SkipFreeSpaceCheck, MinimumFreeSpace, UseHardlinks, ImportExtraFiles,
                            ExtraFileExtensions, ChangeFileDate, RecycleBin, RecycleBinCleanup,
                            SetPermissions, FileChmod, ChmodFolder, ChownUser, ChownGroup,
                            CopyFiles, RemoveCompletedDownloads, RemoveFailedDownloads, Created, LastModified,
                            EnableMultiPartEpisodes, RootFolders
                        )
                        SELECT
                            Id, RenameFiles, StandardFileFormat, EventFolderFormat, CreateEventFolder,
                            RenameEvents, ReplaceIllegalCharacters, CreateEventFolders, DeleteEmptyFolders,
                            SkipFreeSpaceCheck, MinimumFreeSpace, UseHardlinks, ImportExtraFiles,
                            ExtraFileExtensions, ChangeFileDate, RecycleBin, RecycleBinCleanup,
                            SetPermissions, FileChmod, ChmodFolder, ChownUser, ChownGroup,
                            CopyFiles, RemoveCompletedDownloads, RemoveFailedDownloads, Created, LastModified,
                            COALESCE(EnableMultiPartEpisodes, 1), COALESCE(RootFolders, '[]')
                        FROM MediaManagementSettings";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "DROP TABLE MediaManagementSettings";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "ALTER TABLE MediaManagementSettings_new RENAME TO MediaManagementSettings";
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine("[Sportarr] StandardEventFormat column removed successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not remove StandardEventFormat column: {ex.Message}");
        }

        // Clean up orphaned events (events whose leagues no longer exist)
        try
        {
            var orphanedEvents = await db.Events
                .Where(e => e.LeagueId == null || !db.Leagues.Any(l => l.Id == e.LeagueId))
                .ToListAsync();

            if (orphanedEvents.Count > 0)
            {
                Console.WriteLine($"[Sportarr] Found {orphanedEvents.Count} orphaned events (no league) - cleaning up...");
                db.Events.RemoveRange(orphanedEvents);
                await db.SaveChangesAsync();
                Console.WriteLine($"[Sportarr] Successfully removed {orphanedEvents.Count} orphaned events");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not clean up orphaned events: {ex.Message}");
        }

        // Clean up incomplete tasks on startup (Sonarr-style behavior)
        // Tasks that were Queued or Running when the app shut down should be cleared
        // This prevents old queued searches from unexpectedly executing after restart
        try
        {
            var incompleteTasks = await db.Tasks
                .Where(t => t.Status == Sportarr.Api.Models.TaskStatus.Queued ||
                           t.Status == Sportarr.Api.Models.TaskStatus.Running ||
                           t.Status == Sportarr.Api.Models.TaskStatus.Aborting)
                .ToListAsync();

            if (incompleteTasks.Count > 0)
            {
                Console.WriteLine($"[Sportarr] Found {incompleteTasks.Count} incomplete tasks from previous session - cleaning up...");
                foreach (var task in incompleteTasks)
                {
                    task.Status = Sportarr.Api.Models.TaskStatus.Cancelled;
                    task.Ended = DateTime.UtcNow;
                    task.Message = "Cancelled: Application was restarted";
                }
                await db.SaveChangesAsync();
                Console.WriteLine($"[Sportarr] Marked {incompleteTasks.Count} tasks as cancelled");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not clean up incomplete tasks: {ex.Message}");
        }
    }
    Console.WriteLine("[Sportarr] Database migrations applied successfully");

    // Ensure StandardFileFormat is updated to new default format (backwards compatibility fix)
    // This runs AFTER migrations so EnableMultiPartEpisodes column exists
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
        try
        {
            var mediaSettings = await db.MediaManagementSettings.FirstOrDefaultAsync();
            if (mediaSettings != null)
            {
                const string correctFormat = "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}";
                const string correctFormatNoPart = "{Series} - {Season}{Episode} - {Event Title} - {Quality Full}";

                // Check if StandardFileFormat needs to be updated
                var currentFormat = mediaSettings.StandardFileFormat ?? "";

                // Only update if it's NOT already in the correct format
                if (!currentFormat.Equals(correctFormat, StringComparison.OrdinalIgnoreCase) &&
                    !currentFormat.Equals(correctFormatNoPart, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if this is an old format that should be replaced
                    var oldFormats = new[]
                    {
                        "{Event Title} - {Event Date} - {League}",
                        "{Event Title} - {Air Date} - {Quality Full}",
                        "{League}/{Event Title}",
                        "{Event Title}",
                        ""
                    };

                    if (oldFormats.Any(f => f.Equals(currentFormat, StringComparison.OrdinalIgnoreCase)) ||
                        string.IsNullOrWhiteSpace(currentFormat))
                    {
                        Console.WriteLine($"[Sportarr] Updating StandardFileFormat from '{currentFormat}' to new Plex-style format...");
                        mediaSettings.StandardFileFormat = correctFormat;
                        await db.SaveChangesAsync();
                        Console.WriteLine("[Sportarr] StandardFileFormat updated successfully");
                    }
                    else
                    {
                        // User has a custom format - log but don't update
                        Console.WriteLine($"[Sportarr] StandardFileFormat is custom: '{currentFormat}' - not updating automatically");
                    }
                }
                else
                {
                    Console.WriteLine($"[Sportarr] StandardFileFormat is already correct: '{currentFormat}'");
                }
            }
            else
            {
                Console.WriteLine("[Sportarr] Warning: MediaManagementSettings not found - will be created on first use");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Sportarr] Warning: Could not update StandardFileFormat: {ex.Message}");
        }
    }

    // Ensure file format matches EnableMultiPartEpisodes setting
    using (var scope = app.Services.CreateScope())
    {
        var fileFormatManager = scope.ServiceProvider.GetRequiredService<Sportarr.Api.Services.FileFormatManager>();
        var configService = scope.ServiceProvider.GetRequiredService<Sportarr.Api.Services.ConfigService>();
        var config = await configService.GetConfigAsync();
        await fileFormatManager.EnsureFileFormatMatchesMultiPartSetting(config.EnableMultiPartEpisodes);
        Console.WriteLine($"[Sportarr] File format verified (EnableMultiPartEpisodes={config.EnableMultiPartEpisodes})");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Sportarr] ERROR: Database migration failed: {ex.Message}");
    Console.WriteLine($"[Sportarr] Stack trace: {ex.StackTrace}");
    throw;
}

// Copy media server agents to config directory for easy Docker access
try
{
    var agentsSourcePath = Path.Combine(AppContext.BaseDirectory, "agents");
    var agentsDestPath = Path.Combine(dataPath, "agents");

    Console.WriteLine($"[Sportarr] Looking for agents at: {agentsSourcePath}");

    // Check if source exists in app directory
    if (Directory.Exists(agentsSourcePath))
    {
        Console.WriteLine($"[Sportarr] Found agents source directory");

        var needsCopy = !Directory.Exists(agentsDestPath);

        // Check if we need to update (source is newer)
        if (!needsCopy && Directory.Exists(agentsDestPath))
        {
            var sourceInfo = new DirectoryInfo(agentsSourcePath);
            var destInfo = new DirectoryInfo(agentsDestPath);
            needsCopy = sourceInfo.LastWriteTimeUtc > destInfo.LastWriteTimeUtc;
        }

        if (needsCopy)
        {
            Console.WriteLine($"[Sportarr] Copying media server agents to {agentsDestPath}...");
            CopyDirectory(agentsSourcePath, agentsDestPath);
            Console.WriteLine("[Sportarr] Media server agents copied successfully");
            Console.WriteLine("[Sportarr] Plex agent available at: {0}", Path.Combine(agentsDestPath, "plex", "Sportarr.bundle"));
        }
        else
        {
            Console.WriteLine($"[Sportarr] Media server agents already available at {agentsDestPath}");
        }
    }
    else
    {
        // Agents not in build output - create them dynamically
        Console.WriteLine($"[Sportarr] Agents not found in build output, checking config directory...");

        var plexAgentFile = Path.Combine(agentsDestPath, "plex", "Sportarr.bundle", "Contents", "Code", "__init__.py");
        var needsUpdate = !Directory.Exists(agentsDestPath) || !File.Exists(plexAgentFile);

        // Check if existing agent has the broken code (import os, CRLF line endings)
        if (!needsUpdate && File.Exists(plexAgentFile))
        {
            var existingCode = File.ReadAllText(plexAgentFile);
            // Detect old broken agent: has "import os" or "os.environ" or CRLF line endings
            if (existingCode.Contains("import os") || existingCode.Contains("os.environ") || existingCode.Contains("\r\n"))
            {
                Console.WriteLine("[Sportarr] Detected outdated Plex agent with CRLF or import issues, updating...");
                needsUpdate = true;
            }
        }

        if (needsUpdate)
        {
            Console.WriteLine($"[Sportarr] Creating/updating agents in {agentsDestPath}...");
            CreateDefaultAgents(agentsDestPath);
            Console.WriteLine("[Sportarr] Agents created/updated successfully");
            Console.WriteLine("[Sportarr] Plex agent available at: {0}", Path.Combine(agentsDestPath, "plex", "Sportarr.bundle"));
        }
        else
        {
            Console.WriteLine($"[Sportarr] Media server agents already available at {agentsDestPath}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Sportarr] Warning: Could not setup agents directory: {ex.Message}");
}

// Helper function to recursively copy directories
static void CopyDirectory(string sourceDir, string destDir)
{
    Directory.CreateDirectory(destDir);

    foreach (var file in Directory.GetFiles(sourceDir))
    {
        var destFile = Path.Combine(destDir, Path.GetFileName(file));
        File.Copy(file, destFile, true);
    }

    foreach (var dir in Directory.GetDirectories(sourceDir))
    {
        var dirName = Path.GetFileName(dir);
        // Skip obj and bin directories (build artifacts)
        if (dirName == "obj" || dirName == "bin")
            continue;
        CopyDirectory(dir, Path.Combine(destDir, dirName));
    }
}

// Create default agents when not available in build output
static void CreateDefaultAgents(string agentsDestPath)
{
    // Create Plex agent
    var plexPath = Path.Combine(agentsDestPath, "plex", "Sportarr.bundle", "Contents", "Code");
    Directory.CreateDirectory(plexPath);

    // Fixed Plex agent code - no imports, hardcoded URL, uses LF line endings
    var plexAgentCode = "# -*- coding: utf-8 -*-\n\nSPORTARR_API_URL = 'https://sportarr.net'\n\n\ndef Start():\n    Log.Info(\"[Sportarr] Agent starting...\")\n    Log.Info(\"[Sportarr] API URL: %s\" % SPORTARR_API_URL)\n    HTTP.CacheTime = 3600\n\n\nclass SportarrAgent(Agent.TV_Shows):\n    name = 'Sportarr'\n    languages = ['en']\n    primary_provider = True\n    fallback_agent = False\n    accepts_from = ['com.plexapp.agents.localmedia']\n\n    def search(self, results, media, lang, manual):\n        Log.Info(\"[Sportarr] Searching for: %s\" % media.show)\n\n        try:\n            search_url = \"%s/api/metadata/plex/search?title=%s\" % (\n                SPORTARR_API_URL,\n                String.Quote(media.show, usePlus=True)\n            )\n\n            if media.year:\n                search_url = search_url + \"&year=%s\" % media.year\n\n            Log.Debug(\"[Sportarr] Search URL: %s\" % search_url)\n            response = JSON.ObjectFromURL(search_url)\n\n            if 'results' in response:\n                for idx, series in enumerate(response['results'][:10]):\n                    score = 100 - (idx * 5)\n\n                    if series.get('title', '').lower() == media.show.lower():\n                        score = 100\n\n                    results.Append(MetadataSearchResult(\n                        id=str(series.get('id')),\n                        name=series.get('title'),\n                        year=series.get('year'),\n                        score=score,\n                        lang=lang\n                    ))\n\n                    Log.Info(\"[Sportarr] Found: %s (ID: %s, Score: %d)\" % (\n                        series.get('title'), series.get('id'), score\n                    ))\n\n        except Exception as e:\n            Log.Error(\"[Sportarr] Search error: %s\" % str(e))\n\n    def update(self, metadata, media, lang, force):\n        Log.Info(\"[Sportarr] Updating metadata for ID: %s\" % metadata.id)\n\n        try:\n            series_url = \"%s/api/metadata/plex/series/%s\" % (SPORTARR_API_URL, metadata.id)\n            Log.Debug(\"[Sportarr] Series URL: %s\" % series_url)\n            series = JSON.ObjectFromURL(series_url)\n\n            if series:\n                metadata.title = series.get('title')\n                metadata.summary = series.get('summary')\n                metadata.originally_available_at = None\n\n                if series.get('year'):\n                    try:\n                        metadata.originally_available_at = Datetime.ParseDate(\"%s-01-01\" % series.get('year'))\n                    except:\n                        pass\n\n                metadata.studio = series.get('studio')\n                metadata.content_rating = series.get('content_rating')\n\n                metadata.genres.clear()\n                for genre in series.get('genres', []):\n                    metadata.genres.add(genre)\n\n                if series.get('poster_url'):\n                    try:\n                        metadata.posters[series['poster_url']] = Proxy.Media(\n                            HTTP.Request(series['poster_url']).content\n                        )\n                    except Exception as e:\n                        Log.Warn(\"[Sportarr] Failed to fetch poster: %s\" % e)\n\n                if series.get('banner_url'):\n                    try:\n                        metadata.banners[series['banner_url']] = Proxy.Media(\n                            HTTP.Request(series['banner_url']).content\n                        )\n                    except Exception as e:\n                        Log.Warn(\"[Sportarr] Failed to fetch banner: %s\" % e)\n\n                if series.get('fanart_url'):\n                    try:\n                        metadata.art[series['fanart_url']] = Proxy.Media(\n                            HTTP.Request(series['fanart_url']).content\n                        )\n                    except Exception as e:\n                        Log.Warn(\"[Sportarr] Failed to fetch fanart: %s\" % e)\n\n            seasons_url = \"%s/api/metadata/plex/series/%s/seasons\" % (SPORTARR_API_URL, metadata.id)\n            Log.Debug(\"[Sportarr] Seasons URL: %s\" % seasons_url)\n            seasons_response = JSON.ObjectFromURL(seasons_url)\n\n            if 'seasons' in seasons_response:\n                for season_data in seasons_response['seasons']:\n                    season_num = season_data.get('season_number')\n                    if season_num in media.seasons:\n                        season = metadata.seasons[season_num]\n                        season.title = season_data.get('title', \"Season %s\" % season_num)\n                        season.summary = season_data.get('summary', '')\n\n                        if season_data.get('poster_url'):\n                            try:\n                                season.posters[season_data['poster_url']] = Proxy.Media(\n                                    HTTP.Request(season_data['poster_url']).content\n                                )\n                            except Exception as e:\n                                Log.Warn(\"[Sportarr] Failed to fetch season poster: %s\" % e)\n\n                        self.update_episodes(metadata, media, season_num)\n\n        except Exception as e:\n            Log.Error(\"[Sportarr] Update error: %s\" % str(e))\n\n    def update_episodes(self, metadata, media, season_num):\n        Log.Debug(\"[Sportarr] Updating episodes for season %s\" % season_num)\n\n        try:\n            episodes_url = \"%s/api/metadata/plex/series/%s/season/%s/episodes\" % (\n                SPORTARR_API_URL, metadata.id, season_num\n            )\n            Log.Debug(\"[Sportarr] Episodes URL: %s\" % episodes_url)\n            episodes_response = JSON.ObjectFromURL(episodes_url)\n\n            if 'episodes' in episodes_response:\n                for ep_data in episodes_response['episodes']:\n                    ep_num = ep_data.get('episode_number')\n\n                    if ep_num in media.seasons[season_num].episodes:\n                        episode = metadata.seasons[season_num].episodes[ep_num]\n\n                        title = ep_data.get('title', \"Episode %s\" % ep_num)\n                        if ep_data.get('part_name'):\n                            title = \"%s - %s\" % (title, ep_data['part_name'])\n\n                        episode.title = title\n                        episode.summary = ep_data.get('summary', '')\n\n                        if ep_data.get('air_date'):\n                            try:\n                                episode.originally_available_at = Datetime.ParseDate(ep_data['air_date'])\n                            except:\n                                pass\n\n                        if ep_data.get('duration_minutes'):\n                            episode.duration = ep_data['duration_minutes'] * 60 * 1000\n\n                        if ep_data.get('thumb_url'):\n                            try:\n                                episode.thumbs[ep_data['thumb_url']] = Proxy.Media(\n                                    HTTP.Request(ep_data['thumb_url']).content\n                                )\n                            except Exception as e:\n                                Log.Warn(\"[Sportarr] Failed to fetch episode thumb: %s\" % e)\n\n                        Log.Debug(\"[Sportarr] Updated S%sE%s: %s\" % (season_num, ep_num, title))\n\n        except Exception as e:\n            Log.Error(\"[Sportarr] Episodes update error: %s\" % str(e))\n";

    File.WriteAllText(Path.Combine(plexPath, "__init__.py"), plexAgentCode);

    // Create Info.plist for Plex (using LF line endings)
    var infoPlistPath = Path.Combine(agentsDestPath, "plex", "Sportarr.bundle", "Contents");
    var infoPlist = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n    <key>CFBundleIdentifier</key>\n    <string>com.sportarr.agents.sportarr</string>\n\n    <key>PlexPluginClass</key>\n    <string>Agent</string>\n\n    <key>PlexClientPlatforms</key>\n    <string>*</string>\n\n    <key>PlexClientPlatformExclusions</key>\n    <string></string>\n\n    <key>PlexFrameworkVersion</key>\n    <string>2</string>\n\n    <key>PlexPluginCodePolicy</key>\n    <string>Elevated</string>\n\n    <key>PlexBundleVersion</key>\n    <string>1</string>\n\n    <key>CFBundleVersion</key>\n    <string>1.0.0</string>\n\n    <key>PlexAgentAttributionText</key>\n    <string>Metadata provided by Sportarr (powered by TheSportsDB)</string>\n</dict>\n</plist>\n";
    File.WriteAllText(Path.Combine(infoPlistPath, "Info.plist"), infoPlist);

    // Create Jellyfin agent placeholder
    var jellyfinPath = Path.Combine(agentsDestPath, "jellyfin");
    Directory.CreateDirectory(jellyfinPath);
    var jellyfinReadme = @"# Sportarr Jellyfin Plugin

The Jellyfin plugin needs to be built from source or downloaded from releases.

## Building from Source

```bash
cd agents/jellyfin/Sportarr
dotnet build -c Release
```

## Installation

Copy the built DLL to your Jellyfin plugins directory:
- Docker: /config/plugins/Sportarr/
- Windows: %APPDATA%\Jellyfin\Server\plugins\Sportarr\
- Linux: ~/.local/share/jellyfin/plugins/Sportarr/

Then restart Jellyfin.
";
    File.WriteAllText(Path.Combine(jellyfinPath, "README.md"), jellyfinReadme);

    // Create a README for the agents folder
    var agentsReadme = @"# Sportarr Media Server Agents

This folder contains metadata agents for media servers.

## Plex

The `plex/Sportarr.bundle` folder is a Plex metadata agent.
Copy it to your Plex plugins directory and restart Plex.

## Jellyfin

See `jellyfin/README.md` for Jellyfin plugin instructions.
";
    File.WriteAllText(Path.Combine(agentsDestPath, "README.md"), agentsReadme);
}

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// Global exception handling - must be early in pipeline
app.UseExceptionHandling();

// Add X-Application-Version header to all API responses (required for Prowlarr)
app.UseVersionHeader();

// ASP.NET Core Authentication & Authorization (Sonarr/Radarr pattern)
app.UseAuthentication();
app.UseAuthorization();
app.UseDynamicAuthentication(); // Dynamic scheme selection based on settings

// Map controller routes (for AuthenticationController)
app.MapControllers();

// Configure static files (UI from wwwroot)
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize endpoint (for frontend) - keep for SPA compatibility
app.MapGet("/initialize.json", async (Sportarr.Api.Services.ConfigService configService) =>
{
    // Get API key from config.xml (same source that authentication uses)
    var config = await configService.GetConfigAsync();
    return Results.Json(new
    {
        apiRoot = "", // Empty since all API routes already start with /api
        apiKey = config.ApiKey,
        release = Sportarr.Api.Version.GetFullVersion(),
        version = Sportarr.Api.Version.GetFullVersion(),
        instanceName = "Sportarr",
        theme = "auto",
        branch = "main",
        analytics = false,
        userHash = Guid.NewGuid().ToString("N")[..8],
        urlBase = "",
        isProduction = !app.Environment.IsDevelopment()
    });
});

// Health check
app.MapGet("/ping", () => Results.Ok("pong"));

// Authentication endpoints
app.MapPost("/api/login", async (
    LoginRequest request,
    Sportarr.Api.Services.SimpleAuthService authService,
    Sportarr.Api.Services.SessionService sessionService,
    HttpContext context,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTH LOGIN] Login attempt for user: {Username}", request.Username);

    var isValid = await authService.ValidateCredentialsAsync(request.Username, request.Password);

    if (isValid)
    {
        logger.LogInformation("[AUTH LOGIN] Login successful for user: {Username}", request.Username);

        // Get client IP and User-Agent for session tracking
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers["User-Agent"].ToString();

        // Create secure session with cryptographic session ID
        var sessionId = await sessionService.CreateSessionAsync(request.Username, ipAddress, userAgent, request.RememberMe);

        // Set secure authentication cookie with session ID
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true, // Prevents JavaScript access (XSS protection)
            Secure = false,  // Set to true in production with HTTPS
            SameSite = SameSiteMode.Strict, // CSRF protection
            Expires = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddDays(7),
            Path = "/"
        };
        context.Response.Cookies.Append("SportarrAuth", sessionId, cookieOptions);

        logger.LogInformation("[AUTH LOGIN] Session created from IP: {IP}", ipAddress);

        return Results.Ok(new LoginResponse { Success = true, Token = sessionId, Message = "Login successful" });
    }

    logger.LogWarning("[AUTH LOGIN] Login failed for user: {Username}", request.Username);
    return Results.Unauthorized();
});

app.MapPost("/api/logout", async (
    Sportarr.Api.Services.SessionService sessionService,
    HttpContext context,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTH LOGOUT] Logout requested");

    // Get session ID from cookie
    var sessionId = context.Request.Cookies["SportarrAuth"];
    if (!string.IsNullOrEmpty(sessionId))
    {
        // Delete session from database
        await sessionService.DeleteSessionAsync(sessionId);
    }

    // Delete cookie
    context.Response.Cookies.Delete("SportarrAuth");
    return Results.Ok(new { message = "Logged out successfully" });
});

// NEW SIMPLE FLOW: Check if initial setup is complete
app.MapGet("/api/auth/check", async (
    Sportarr.Api.Services.SimpleAuthService authService,
    Sportarr.Api.Services.SessionService sessionService,
    HttpContext context,
    ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[AUTH CHECK] Starting auth check");

        // Step 1: Check if setup is complete (credentials exist)
        var isSetupComplete = await authService.IsSetupCompleteAsync();
        logger.LogInformation("[AUTH CHECK] IsSetupComplete={IsSetupComplete}", isSetupComplete);

        if (!isSetupComplete)
        {
            // No credentials exist - need initial setup
            logger.LogInformation("[AUTH CHECK] Setup not complete, redirecting to setup");
            return Results.Ok(new { setupComplete = false, authenticated = false });
        }

        // Step 2: Setup is complete, validate session with security checks
        var sessionId = context.Request.Cookies["SportarrAuth"];
        if (string.IsNullOrEmpty(sessionId))
        {
            logger.LogInformation("[AUTH CHECK] No session cookie found");
            return Results.Ok(new { setupComplete = true, authenticated = false });
        }

        // Get client IP and User-Agent for validation
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers["User-Agent"].ToString();

        // Validate session with IP and User-Agent checks
        var (isValid, username) = await sessionService.ValidateSessionAsync(
            sessionId,
            ipAddress,
            userAgent,
            strictIpCheck: true,      // Reject if IP doesn't match
            strictUserAgentCheck: true // Reject if User-Agent doesn't match
        );

        if (isValid)
        {
            logger.LogInformation("[AUTH CHECK] Valid session for user {Username} from IP {IP}", username, ipAddress);
            return Results.Ok(new { setupComplete = true, authenticated = true, username });
        }
        else
        {
            logger.LogWarning("[AUTH CHECK] Invalid session - IP or User-Agent mismatch");
            // Delete invalid cookie
            context.Response.Cookies.Delete("SportarrAuth");
            return Results.Ok(new { setupComplete = true, authenticated = false });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[AUTH CHECK] CRITICAL ERROR: {Message}", ex.Message);
        logger.LogError(ex, "[AUTH CHECK] Stack trace: {StackTrace}", ex.StackTrace);
        // On error, assume setup incomplete to force setup page
        return Results.Ok(new { setupComplete = false, authenticated = false });
    }
});

// NEW: Initial setup endpoint - creates first user credentials
app.MapPost("/api/setup", async (SetupRequest request, Sportarr.Api.Services.SimpleAuthService authService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[SETUP] Initial setup requested for username: {Username}", request.Username);

        // Check if setup is already complete
        var isSetupComplete = await authService.IsSetupCompleteAsync();
        logger.LogInformation("[SETUP] IsSetupComplete check result: {Result}", isSetupComplete);

        if (isSetupComplete)
        {
            logger.LogWarning("[SETUP] Setup already complete, rejecting request");
            return Results.BadRequest(new { error = "Setup is already complete" });
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            logger.LogWarning("[SETUP] Validation failed: Username or password is empty");
            return Results.BadRequest(new { error = "Username and password are required" });
        }

        if (request.Password.Length < 6)
        {
            logger.LogWarning("[SETUP] Validation failed: Password too short ({Length} chars)", request.Password.Length);
            return Results.BadRequest(new { error = "Password must be at least 6 characters" });
        }

        // Create initial credentials
        logger.LogInformation("[SETUP] Creating credentials for user: {Username}", request.Username);
        await authService.SetCredentialsAsync(request.Username, request.Password);
        logger.LogInformation("[SETUP] Initial setup complete for user: {Username}", request.Username);

        return Results.Ok(new { message = "Setup complete. Please login with your credentials." });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[SETUP] CRITICAL ERROR during setup: {Message}", ex.Message);
        logger.LogError(ex, "[SETUP] Stack trace: {StackTrace}", ex.StackTrace);
        return Results.Problem(detail: ex.Message, title: "Setup failed");
    }
});

// API: System Status
app.MapGet("/api/system/status", (HttpContext context) =>
{
    var status = new SystemStatus
    {
        AppName = "Sportarr",
        Version = Sportarr.Api.Version.GetFullVersion(),  // Use full 4-part version (e.g., 4.0.81.140)
        IsDebug = app.Environment.IsDevelopment(),
        IsProduction = app.Environment.IsProduction(),
        IsDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
        DatabaseType = "SQLite",
        Authentication = "apikey",
        AppData = dataPath,
        StartTime = DateTime.UtcNow
    };
    return Results.Ok(status);
});

// API: System Health Checks
app.MapGet("/api/system/health", async (Sportarr.Api.Services.HealthCheckService healthCheckService) =>
{
    var healthResults = await healthCheckService.PerformAllChecksAsync();
    return Results.Ok(healthResults);
});

// API: System Backup Management
app.MapGet("/api/system/backup", async (Sportarr.Api.Services.BackupService backupService) =>
{
    var backups = await backupService.GetBackupsAsync();
    return Results.Ok(backups);
});

app.MapPost("/api/system/backup", async (Sportarr.Api.Services.BackupService backupService, string? note) =>
{
    try
    {
        var backup = await backupService.CreateBackupAsync(note);
        return Results.Ok(backup);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/system/backup/restore/{backupName}", async (string backupName, Sportarr.Api.Services.BackupService backupService) =>
{
    try
    {
        await backupService.RestoreBackupAsync(backupName);
        return Results.Ok(new { message = "Backup restored successfully. Please restart Sportarr for changes to take effect." });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapDelete("/api/system/backup/{backupName}", async (string backupName, Sportarr.Api.Services.BackupService backupService) =>
{
    try
    {
        await backupService.DeleteBackupAsync(backupName);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/system/backup/cleanup", async (Sportarr.Api.Services.BackupService backupService) =>
{
    try
    {
        await backupService.CleanupOldBackupsAsync();
        return Results.Ok(new { message = "Old backups cleaned up successfully" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// API: Download Media Server Agents
app.MapGet("/api/system/agents", () =>
{
    var agentsPath = Path.Combine(dataPath, "agents");
    var agents = new List<object>();

    if (Directory.Exists(agentsPath))
    {
        // Check for Plex agent
        var plexAgentPath = Path.Combine(agentsPath, "plex", "Sportarr.bundle");
        if (Directory.Exists(plexAgentPath))
        {
            agents.Add(new
            {
                name = "Plex",
                type = "plex",
                available = true,
                path = plexAgentPath,
                downloadUrl = "/api/system/agents/plex/download"
            });
        }

        // Check for Jellyfin agent
        var jellyfinAgentPath = Path.Combine(agentsPath, "jellyfin");
        if (Directory.Exists(jellyfinAgentPath))
        {
            agents.Add(new
            {
                name = "Jellyfin",
                type = "jellyfin",
                available = true,
                path = jellyfinAgentPath,
                downloadUrl = "/api/system/agents/jellyfin/download"
            });
        }
    }

    return Results.Ok(new
    {
        agentsPath = agentsPath,
        agents = agents
    });
});

app.MapGet("/api/system/agents/plex/download", async (HttpContext context, ILogger<Program> logger) =>
{
    // Try config directory first, then fall back to app directory
    var plexAgentPath = Path.Combine(dataPath, "agents", "plex", "Sportarr.bundle");
    logger.LogInformation("Checking for Plex agent at: {Path}", plexAgentPath);

    if (!Directory.Exists(plexAgentPath))
    {
        plexAgentPath = Path.Combine(AppContext.BaseDirectory, "agents", "plex", "Sportarr.bundle");
        logger.LogInformation("Not found, checking fallback at: {Path}", plexAgentPath);
    }

    if (!Directory.Exists(plexAgentPath))
    {
        logger.LogWarning("Plex agent not found at either location");
        context.Response.StatusCode = 404;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Plex agent not found. The agents folder may not be included in your build.\"}");
        return;
    }

    try
    {
        logger.LogInformation("Creating zip from: {Path}", plexAgentPath);

        // Create a zip file in memory
        using var memoryStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            await AddDirectoryToZip(archive, plexAgentPath, "Sportarr.bundle");
        }

        memoryStream.Position = 0;
        var bytes = memoryStream.ToArray();

        logger.LogInformation("Zip created successfully, size: {Size} bytes", bytes.Length);

        context.Response.ContentType = "application/zip";
        context.Response.Headers.Append("Content-Disposition", "attachment; filename=\"Sportarr.bundle.zip\"");
        context.Response.Headers.Append("Content-Length", bytes.Length.ToString());
        await context.Response.Body.WriteAsync(bytes);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to create Plex agent zip");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync($"{{\"error\":\"Failed to create zip: {ex.Message}\"}}");
    }
});

app.MapGet("/api/system/agents/jellyfin/download", async (HttpContext context, ILogger<Program> logger) =>
{
    // Try config directory first, then fall back to app directory
    var jellyfinAgentPath = Path.Combine(dataPath, "agents", "jellyfin");
    logger.LogInformation("Checking for Jellyfin agent at: {Path}", jellyfinAgentPath);

    if (!Directory.Exists(jellyfinAgentPath))
    {
        jellyfinAgentPath = Path.Combine(AppContext.BaseDirectory, "agents", "jellyfin");
        logger.LogInformation("Not found, checking fallback at: {Path}", jellyfinAgentPath);
    }

    if (!Directory.Exists(jellyfinAgentPath))
    {
        logger.LogWarning("Jellyfin agent not found at either location");
        context.Response.StatusCode = 404;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Jellyfin agent not found. The agents folder may not be included in your build.\"}");
        return;
    }

    try
    {
        logger.LogInformation("Creating zip from: {Path}", jellyfinAgentPath);

        // Create a zip file in memory
        using var memoryStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            await AddDirectoryToZip(archive, jellyfinAgentPath, "jellyfin");
        }

        memoryStream.Position = 0;
        var bytes = memoryStream.ToArray();

        logger.LogInformation("Zip created successfully, size: {Size} bytes", bytes.Length);

        context.Response.ContentType = "application/zip";
        context.Response.Headers.Append("Content-Disposition", "attachment; filename=\"Sportarr-Jellyfin.zip\"");
        context.Response.Headers.Append("Content-Length", bytes.Length.ToString());
        await context.Response.Body.WriteAsync(bytes);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to create Jellyfin agent zip");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync($"{{\"error\":\"Failed to create zip: {ex.Message}\"}}");
    }
});

// Helper function to add a directory to a zip archive
static async Task AddDirectoryToZip(System.IO.Compression.ZipArchive archive, string sourceDir, string entryPrefix)
{
    foreach (var file in Directory.GetFiles(sourceDir))
    {
        var entryName = Path.Combine(entryPrefix, Path.GetFileName(file)).Replace('\\', '/');
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var fileStream = File.OpenRead(file);
        await fileStream.CopyToAsync(entryStream);
    }

    foreach (var dir in Directory.GetDirectories(sourceDir))
    {
        var dirName = Path.GetFileName(dir);
        // Skip obj and bin directories
        if (dirName == "obj" || dirName == "bin")
            continue;
        await AddDirectoryToZip(archive, dir, Path.Combine(entryPrefix, dirName));
    }
}

// API: System Updates - Check for new versions from GitHub
app.MapGet("/api/system/updates", async (ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[UPDATES] Checking for updates from GitHub");

        // Get current version using the centralized version helper
        var currentVersion = Sportarr.Api.Version.GetFullVersion();

        logger.LogInformation("[UPDATES] Current version: {Version}", currentVersion);

        // Fetch releases from GitHub API
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", $"Sportarr/{currentVersion}");

        var response = await httpClient.GetAsync("https://api.github.com/repos/Sportarr/Sportarr/releases");

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("[UPDATES] Failed to fetch releases from GitHub: {StatusCode}", response.StatusCode);
            return Results.Problem("Failed to fetch updates from GitHub");
        }

        var json = await response.Content.ReadAsStringAsync();
        var releases = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        var releaseList = new List<object>();
        string? latestVersion = null;

        foreach (var release in releases.EnumerateArray())
        {
            var tagName = release.GetProperty("tag_name").GetString() ?? "";
            var version = tagName.TrimStart('v'); // Remove 'v' prefix if present
            var publishedAt = release.GetProperty("published_at").GetString() ?? DateTime.UtcNow.ToString();
            var body = release.GetProperty("body").GetString() ?? "";
            var htmlUrl = release.GetProperty("html_url").GetString() ?? "";
            var isDraft = release.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
            var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseProp) && prereleaseProp.GetBoolean();

            // Skip drafts and prereleases
            if (isDraft || isPrerelease)
            {
                continue;
            }

            // Track latest version
            if (latestVersion == null)
            {
                latestVersion = version;
            }

            // Parse changelog from release body
            var changes = new List<string>();
            if (!string.IsNullOrEmpty(body))
            {
                var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    // Skip headers and empty lines
                    if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                    {
                        continue;
                    }
                    // Add bullet points
                    if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
                    {
                        changes.Add(trimmed.TrimStart('-', '*').Trim());
                    }
                    else if (changes.Count < 10) // Limit to 10 changes
                    {
                        changes.Add(trimmed);
                    }
                }
            }

            // Check if this release is installed (compare 3-part base version)
            var currentParts = currentVersion.Split('.');
            var currentBase = currentParts.Length >= 3 ? $"{currentParts[0]}.{currentParts[1]}.{currentParts[2]}" : currentVersion;
            var isInstalled = version == currentBase || version == currentVersion;

            releaseList.Add(new
            {
                version,
                releaseDate = publishedAt,
                branch = "main",
                changes = changes.Take(10).ToList(), // Limit to 10 changes per release
                downloadUrl = htmlUrl,
                isInstalled,
                isLatest = version == latestVersion
            });

            // Only show last 10 releases
            if (releaseList.Count >= 10)
            {
                break;
            }
        }

        // Compare versions properly - currentVersion is 4-part (4.0.81.140), latestVersion is 3-part (4.0.82)
        var updateAvailable = false;
        if (latestVersion != null)
        {
            // Extract first 3 parts of current version for comparison (4.0.81.140 -> 4.0.81)
            var currentParts = currentVersion.Split('.');
            var currentBase = currentParts.Length >= 3 ? $"{currentParts[0]}.{currentParts[1]}.{currentParts[2]}" : currentVersion;

            // latestVersion is already 3-part from GitHub tags (v4.0.82 -> 4.0.82)
            updateAvailable = latestVersion != currentBase && latestVersion != currentVersion;
        }

        logger.LogInformation("[UPDATES] Current: {Current}, Latest: {Latest}, Available: {Available}",
            currentVersion, latestVersion ?? "unknown", updateAvailable);

        return Results.Ok(new
        {
            updateAvailable,
            currentVersion,
            latestVersion = latestVersion ?? currentVersion,
            releases = releaseList
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[UPDATES] Error checking for updates");
        return Results.Problem("Error checking for updates: " + ex.Message);
    }
});

// API: System Events (Audit Log)
app.MapGet("/api/system/event", async (SportarrDbContext db, int page = 1, int pageSize = 50, string? type = null, string? category = null) =>
{
    // Validate pagination parameters
    if (page < 1) page = 1;
    if (pageSize < 1) pageSize = 1;
    if (pageSize > 500) pageSize = 500; // Prevent excessive data retrieval

    var query = db.SystemEvents.AsQueryable();

    if (!string.IsNullOrEmpty(type) && Enum.TryParse<EventType>(type, true, out var eventType))
    {
        query = query.Where(e => e.Type == eventType);
    }

    if (!string.IsNullOrEmpty(category) && Enum.TryParse<EventCategory>(category, true, out var eventCategory))
    {
        query = query.Where(e => e.Category == eventCategory);
    }

    var totalCount = await query.CountAsync();
    var events = await query
        .OrderByDescending(e => e.Timestamp)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(new
    {
        events,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

app.MapDelete("/api/system/event/{id:int}", async (int id, SportarrDbContext db) =>
{
    var systemEvent = await db.SystemEvents.FindAsync(id);
    if (systemEvent is null) return Results.NotFound();

    db.SystemEvents.Remove(systemEvent);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.MapPost("/api/system/event/cleanup", async (SportarrDbContext db, int days = 30) =>
{
    var cutoffDate = DateTime.UtcNow.AddDays(-days);
    var oldEvents = db.SystemEvents.Where(e => e.Timestamp < cutoffDate);
    db.SystemEvents.RemoveRange(oldEvents);
    var deleted = await db.SaveChangesAsync();
    return Results.Ok(new { message = $"Deleted {deleted} old system events", deletedCount = deleted });
});

// API: Disk Scan - Trigger a manual disk scan to detect missing files
app.MapPost("/api/system/disk-scan", () =>
{
    Sportarr.Api.Services.DiskScanService.TriggerScan();
    return Results.Ok(new { message = "Disk scan triggered successfully" });
});

// API: Library Import - Scan filesystem for existing event files
app.MapPost("/api/library/scan", async (Sportarr.Api.Services.LibraryImportService service, string folderPath, bool includeSubfolders = true) =>
{
    try
    {
        var result = await service.ScanFolderAsync(folderPath, includeSubfolders);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to scan folder: {ex.Message}");
    }
});

app.MapPost("/api/library/import", async (Sportarr.Api.Services.LibraryImportService service, List<Sportarr.Api.Services.FileImportRequest> requests) =>
{
    try
    {
        var result = await service.ImportFilesAsync(requests);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to import files: {ex.Message}");
    }
});

// API: Library Import - Search TheSportsDB for events to match unmatched files
app.MapGet("/api/library/search", async (
    Sportarr.Api.Services.TheSportsDBClient theSportsDB,
    SportarrDbContext db,
    string query,
    string? sport = null,
    string? organization = null) =>
{
    try
    {
        var results = new List<object>();

        // Search TheSportsDB for events
        var apiEvents = await theSportsDB.SearchEventAsync(query);
        if (apiEvents != null)
        {
            foreach (var evt in apiEvents.Take(20)) // Limit to 20 results
            {
                // Check if event already exists in local database
                var existingEvent = await db.Events
                    .FirstOrDefaultAsync(e => e.ExternalId == evt.ExternalId);

                results.Add(new
                {
                    id = existingEvent?.Id,
                    externalId = evt.ExternalId,
                    title = evt.Title,
                    sport = evt.Sport,
                    eventDate = evt.EventDate,
                    venue = evt.Venue,
                    leagueName = evt.League?.Name,
                    homeTeam = evt.HomeTeam?.Name,
                    awayTeam = evt.AwayTeam?.Name,
                    existsInDatabase = existingEvent != null,
                    hasFile = existingEvent?.HasFile ?? false
                });
            }
        }

        // Also search local database for events that might match
        var localQuery = db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Where(e => !e.HasFile) // Only events without files
            .AsQueryable();

        if (!string.IsNullOrEmpty(sport))
        {
            localQuery = localQuery.Where(e => e.Sport == sport);
        }

        var localEvents = await localQuery
            .Where(e => EF.Functions.Like(e.Title, $"%{query}%"))
            .Take(20)
            .ToListAsync();

        foreach (var evt in localEvents)
        {
            // Don't duplicate if already in results from API
            if (!results.Any(r => ((dynamic)r).externalId == evt.ExternalId))
            {
                results.Add(new
                {
                    id = evt.Id,
                    externalId = evt.ExternalId,
                    title = evt.Title,
                    sport = evt.Sport,
                    eventDate = evt.EventDate,
                    venue = evt.Venue,
                    leagueName = evt.League?.Name,
                    homeTeam = evt.HomeTeam?.Name,
                    awayTeam = evt.AwayTeam?.Name,
                    existsInDatabase = true,
                    hasFile = evt.HasFile
                });
            }
        }

        return Results.Ok(new { results });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to search events: {ex.Message}");
    }
});

// API: Library Import - Get seasons for a league (for hierarchical browsing)
app.MapGet("/api/library/leagues/{leagueId:int}/seasons", async (
    int leagueId,
    SportarrDbContext db) =>
{
    try
    {
        var seasons = await db.Events
            .Where(e => e.LeagueId == leagueId && !string.IsNullOrEmpty(e.Season))
            .Select(e => e.Season)
            .Distinct()
            .OrderByDescending(s => s)
            .ToListAsync();

        return Results.Ok(new { seasons });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get seasons: {ex.Message}");
    }
});

// API: Library Import - Get events for a league/season (for hierarchical browsing)
app.MapGet("/api/library/leagues/{leagueId:int}/events", async (
    int leagueId,
    SportarrDbContext db,
    Sportarr.Api.Services.ConfigService configService,
    string? season = null) =>
{
    try
    {
        var query = db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Include(e => e.Files)
            .Where(e => e.LeagueId == leagueId);

        if (!string.IsNullOrEmpty(season))
        {
            query = query.Where(e => e.Season == season);
        }

        var events = await query
            .OrderByDescending(e => e.EventDate)
            .Take(100) // Limit to 100 events
            .ToListAsync();

        var config = await configService.GetConfigAsync();

        var results = events.Select(e => new
        {
            id = e.Id,
            externalId = e.ExternalId,
            title = e.Title,
            sport = e.Sport,
            eventDate = e.EventDate,
            season = e.Season,
            seasonNumber = e.SeasonNumber,
            episodeNumber = e.EpisodeNumber,
            venue = e.Venue,
            leagueName = e.League?.Name,
            homeTeam = e.HomeTeam?.Name ?? e.HomeTeamName,
            awayTeam = e.AwayTeam?.Name ?? e.AwayTeamName,
            hasFile = e.HasFile,
            // Include part info for multi-part sports
            usesMultiPart = config.EnableMultiPartEpisodes &&
                (Sportarr.Api.Services.EventPartDetector.IsFightingSport(e.Sport) ||
                 Sportarr.Api.Services.EventPartDetector.IsMotorsport(e.Sport)),
            files = e.Files.Select(f => new
            {
                id = f.Id,
                partName = f.PartName,
                partNumber = f.PartNumber,
                quality = f.Quality
            }).ToList()
        }).ToList();

        return Results.Ok(new { events = results });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get events: {ex.Message}");
    }
});

// API: Library Import - Get segment/part definitions for a sport
app.MapGet("/api/library/parts/{sport}", (string sport) =>
{
    var segments = Sportarr.Api.Services.EventPartDetector.GetSegmentDefinitions(sport);
    return Results.Ok(new { parts = segments });
});

// API: Get segment/part definitions for a specific event (event-type-aware)
// e.g., UFC Fight Night events don't show "Early Prelims" option
app.MapGet("/api/event/{eventId:int}/parts", async (int eventId, SportarrDbContext db) =>
{
    var evt = await db.Events.FindAsync(eventId);
    if (evt == null)
        return Results.NotFound(new { error = "Event not found" });

    var sport = evt.Sport ?? "Fighting";
    var segments = Sportarr.Api.Services.EventPartDetector.GetSegmentDefinitions(sport, evt.Title);
    var eventType = Sportarr.Api.Services.EventPartDetector.DetectUfcEventType(evt.Title);

    return Results.Ok(new
    {
        parts = segments,
        eventType = eventType.ToString(),
        isFightNightStyle = Sportarr.Api.Services.EventPartDetector.IsFightNightStyleEvent(evt.Title, null)
    });
});

// API: Get log files list
app.MapGet("/api/log/file", (ILogger<Program> logger) =>
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

        logger.LogInformation("[LOG FILES] Listing {Count} log files", logFiles.Count);
        return Results.Ok(logFiles);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LOG FILES] Error listing log files");
        return Results.Problem("Error listing log files");
    }
});

// API: Get specific log file content
app.MapGet("/api/log/file/{filename}", (string filename, ILogger<Program> logger) =>
{
    try
    {
        // Sanitize filename to prevent directory traversal
        filename = Path.GetFileName(filename);
        var logFilePath = Path.Combine(logsPath, filename);

        if (!File.Exists(logFilePath))
        {
            logger.LogWarning("[LOG FILES] File not found: {Filename}", filename);
            return Results.NotFound(new { message = "Log file not found" });
        }

        logger.LogInformation("[LOG FILES] Reading log file: {Filename}", filename);

        // Read with FileShare.ReadWrite to allow reading while Serilog is writing
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
app.MapGet("/api/log/file/{filename}/download", (string filename, ILogger<Program> logger) =>
{
    try
    {
        // Sanitize filename to prevent directory traversal
        filename = Path.GetFileName(filename);
        var logFilePath = Path.Combine(logsPath, filename);

        if (!File.Exists(logFilePath))
        {
            logger.LogWarning("[LOG FILES] File not found for download: {Filename}", filename);
            return Results.NotFound(new { message = "Log file not found" });
        }

        logger.LogInformation("[LOG FILES] Downloading log file: {Filename}", filename);

        // Read with FileShare.ReadWrite to allow reading while Serilog is writing
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

// API: Get all tasks (with optional limit)
app.MapGet("/api/task", async (Sportarr.Api.Services.TaskService taskService, int? pageSize) =>
{
    try
    {
        var tasks = await taskService.GetAllTasksAsync(pageSize);
        return Results.Ok(tasks);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error getting tasks");
        return Results.Problem("Error getting tasks");
    }
});

// API: Get specific task
app.MapGet("/api/task/{id:int}", async (int id, Sportarr.Api.Services.TaskService taskService) =>
{
    try
    {
        var task = await taskService.GetTaskAsync(id);
        return task is null ? Results.NotFound() : Results.Ok(task);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error getting task {TaskId}", id);
        return Results.Problem("Error getting task");
    }
});

// API: Queue a new task (for testing)
app.MapPost("/api/task", async (Sportarr.Api.Services.TaskService taskService, TaskRequest request) =>
{
    try
    {
        var task = await taskService.QueueTaskAsync(
            request.Name,
            request.CommandName,
            request.Priority ?? 0,
            request.Body
        );
        return Results.Created($"/api/task/{task.Id}", task);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error queueing task");
        return Results.Problem("Error queueing task");
    }
});

// API: Cancel a task
app.MapDelete("/api/task/{id:int}", async (int id, Sportarr.Api.Services.TaskService taskService) =>
{
    try
    {
        var success = await taskService.CancelTaskAsync(id);
        if (!success)
        {
            return Results.NotFound(new { message = "Task not found or cannot be cancelled" });
        }
        return Results.Ok(new { message = "Task cancelled successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error cancelling task {TaskId}", id);
        return Results.Problem("Error cancelling task");
    }
});

// API: Clean up old tasks
app.MapPost("/api/task/cleanup", async (Sportarr.Api.Services.TaskService taskService, int? keepCount) =>
{
    try
    {
        await taskService.CleanupOldTasksAsync(keepCount ?? 100);
        return Results.Ok(new { message = "Old tasks cleaned up successfully" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "[TASK API] Error cleaning up tasks");
        return Results.Problem("Error cleaning up tasks");
    }
});

// API: Get all events (universal for all sports)
app.MapGet("/api/events", async (SportarrDbContext db) =>
{
    var events = await db.Events
        .Include(e => e.League)        // Universal (UFC, Premier League, NBA, etc.)
        .Include(e => e.HomeTeam)      // Universal (team sports and combat sports)
        .Include(e => e.AwayTeam)      // Universal (team sports and combat sports)
        .Include(e => e.Files)         // Event files (for multi-part episodes)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Convert to DTOs to avoid JsonPropertyName serialization issues
    var response = events.Select(EventResponse.FromEvent).ToList();
    return Results.Ok(response);
});

// API: Get single event (universal for all sports)
app.MapGet("/api/events/{id:int}", async (int id, SportarrDbContext db) =>
{
    var evt = await db.Events
        .Include(e => e.League)        // Universal (UFC, Premier League, NBA, etc.)
        .Include(e => e.HomeTeam)      // Universal (team sports and combat sports)
        .Include(e => e.AwayTeam)      // Universal (team sports and combat sports)
        .Include(e => e.Files)         // Event files (for multi-part episodes)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Return DTO to avoid JsonPropertyName serialization issues
    return Results.Ok(EventResponse.FromEvent(evt));
});

// API: Create event (universal for all sports)
app.MapPost("/api/events", async (CreateEventRequest request, SportarrDbContext db) =>
{
    var evt = new Event
    {
        ExternalId = request.ExternalId,
        Title = request.Title,
        Sport = request.Sport,           // Universal: Fighting, Soccer, Basketball, etc.
        LeagueId = request.LeagueId,     // Universal: UFC, Premier League, NBA
        HomeTeamId = request.HomeTeamId, // Team sports and combat sports
        AwayTeamId = request.AwayTeamId, // Team sports and combat sports
        Season = request.Season,
        Round = request.Round,
        EventDate = request.EventDate,
        Venue = request.Venue,
        Location = request.Location,
        Broadcast = request.Broadcast,
        Status = request.Status,
        Monitored = request.Monitored,
        QualityProfileId = request.QualityProfileId,
        Images = request.Images ?? new List<string>()
    };

    // Check if event already exists (by ExternalId OR by Title + EventDate)
    var existingEvent = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .FirstOrDefaultAsync(e =>
            (e.ExternalId != null && e.ExternalId == evt.ExternalId) ||
            (e.Title == evt.Title && e.EventDate.Date == evt.EventDate.Date));

    if (existingEvent != null)
    {
        // Event already exists - return it with AlreadyAdded flag
        return Results.Ok(new { Event = existingEvent, AlreadyAdded = true });
    }

    db.Events.Add(evt);
    await db.SaveChangesAsync();

    // Reload event with related entities
    var createdEvent = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .FirstOrDefaultAsync(e => e.Id == evt.Id);

    if (createdEvent is null) return Results.Problem("Failed to create event");

    return Results.Created($"/api/events/{evt.Id}", createdEvent);
});

// API: Update event (universal for all sports)
app.MapPut("/api/events/{id:int}", async (int id, JsonElement body, SportarrDbContext db) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    // Extract fields from request body (only update fields that are present)
    if (body.TryGetProperty("title", out var titleValue))
        evt.Title = titleValue.GetString() ?? evt.Title;

    if (body.TryGetProperty("sport", out var sportValue))
        evt.Sport = sportValue.GetString() ?? evt.Sport;

    if (body.TryGetProperty("leagueId", out var leagueIdValue))
    {
        if (leagueIdValue.ValueKind == JsonValueKind.Null)
            evt.LeagueId = null;
        else if (leagueIdValue.ValueKind == JsonValueKind.Number)
            evt.LeagueId = leagueIdValue.GetInt32();
    }

    if (body.TryGetProperty("eventDate", out var dateValue))
        evt.EventDate = dateValue.GetDateTime();

    if (body.TryGetProperty("venue", out var venueValue))
        evt.Venue = venueValue.GetString();

    if (body.TryGetProperty("location", out var locationValue))
        evt.Location = locationValue.GetString();

    if (body.TryGetProperty("monitored", out var monitoredValue))
        evt.Monitored = monitoredValue.GetBoolean();

    if (body.TryGetProperty("monitoredParts", out var monitoredPartsValue))
    {
        evt.MonitoredParts = monitoredPartsValue.ValueKind == JsonValueKind.Null
            ? null
            : monitoredPartsValue.GetString();
    }

    if (body.TryGetProperty("qualityProfileId", out var qualityProfileIdValue))
    {
        if (qualityProfileIdValue.ValueKind == JsonValueKind.Null)
            evt.QualityProfileId = null;
        else if (qualityProfileIdValue.ValueKind == JsonValueKind.Number)
            evt.QualityProfileId = qualityProfileIdValue.GetInt32();
    }

    evt.LastUpdate = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Reload with related entities
    evt = await db.Events
        .Include(e => e.League)
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.Files)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Return DTO to avoid JsonPropertyName serialization issues
    return Results.Ok(EventResponse.FromEvent(evt));
});

// API: Delete event
app.MapDelete("/api/events/{id:int}", async (int id, SportarrDbContext db) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    db.Events.Remove(evt);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Get all files for an event
app.MapGet("/api/events/{id:int}/files", async (int id, SportarrDbContext db) =>
{
    var evt = await db.Events
        .Include(e => e.Files)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null) return Results.NotFound();

    // Only return files that exist on disk
    return Results.Ok(evt.Files.Where(f => f.Exists).Select(f => new
    {
        f.Id,
        f.EventId,
        f.FilePath,
        f.Size,
        f.Quality,
        f.QualityScore,
        f.CustomFormatScore,
        f.PartName,
        f.PartNumber,
        f.Added,
        f.LastVerified,
        f.Exists,
        FileName = Path.GetFileName(f.FilePath)
    }));
});

// API: Delete a specific event file (removes from disk and database)
// blocklistAction: 'none' | 'blocklistAndSearch' | 'blocklistOnly'
app.MapDelete("/api/events/{eventId:int}/files/{fileId:int}", async (
    int eventId,
    int fileId,
    string? blocklistAction,
    SportarrDbContext db,
    ILogger<Program> logger,
    ConfigService configService,
    AutomaticSearchService searchService) =>
{
    var evt = await db.Events
        .Include(e => e.Files)
        .Include(e => e.League)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (evt is null)
        return Results.NotFound(new { error = "Event not found" });

    var file = evt.Files.FirstOrDefault(f => f.Id == fileId);
    if (file is null)
        return Results.NotFound(new { error = "File not found" });

    logger.LogInformation("[FILES] Deleting file {FileId} for event {EventId}: {FilePath} (blocklistAction={BlocklistAction})",
        fileId, eventId, file.FilePath, blocklistAction ?? "none");

    // Delete from disk if it exists
    bool deletedFromDisk = false;
    if (File.Exists(file.FilePath))
    {
        try
        {
            // Check if recycle bin is configured
            var config = await configService.GetConfigAsync();
            var recycleBinPath = config.RecycleBin;

            if (!string.IsNullOrEmpty(recycleBinPath) && Directory.Exists(recycleBinPath))
            {
                // Move to recycle bin instead of permanent deletion
                var fileName = Path.GetFileName(file.FilePath);
                var recyclePath = Path.Combine(recycleBinPath, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
                File.Move(file.FilePath, recyclePath);
                logger.LogInformation("[FILES] Moved file to recycle bin: {RecyclePath}", recyclePath);
            }
            else
            {
                // Permanent deletion
                File.Delete(file.FilePath);
                logger.LogInformation("[FILES] Permanently deleted file from disk");
            }
            deletedFromDisk = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FILES] Failed to delete file from disk: {FilePath}", file.FilePath);
            return Results.Problem(
                detail: $"Failed to delete file from disk: {ex.Message}",
                statusCode: 500);
        }
    }
    else
    {
        logger.LogWarning("[FILES] File not found on disk (already deleted?): {FilePath}", file.FilePath);
    }

    // Remove from database
    db.Remove(file);

    // Update event's HasFile status
    var remainingFiles = evt.Files.Where(f => f.Id != fileId && f.Exists).ToList();
    if (!remainingFiles.Any())
    {
        evt.HasFile = false;
        evt.FilePath = null;
        evt.FileSize = null;
        evt.Quality = null;
        logger.LogInformation("[FILES] Event {EventId} no longer has any files", eventId);
    }
    else
    {
        // Update to use the first remaining file's info
        var primaryFile = remainingFiles.First();
        evt.FilePath = primaryFile.FilePath;
        evt.FileSize = primaryFile.Size;
        evt.Quality = primaryFile.Quality;
    }

    await db.SaveChangesAsync();

    // Handle blocklist action if specified
    if (blocklistAction == "blocklistAndSearch" || blocklistAction == "blocklistOnly")
    {
        // Add to blocklist using originalTitle if available, otherwise use filename
        var releaseTitle = file.OriginalTitle ?? Path.GetFileNameWithoutExtension(file.FilePath);
        if (!string.IsNullOrEmpty(releaseTitle))
        {
            var blocklistEntry = new BlocklistItem
            {
                EventId = eventId,
                Title = releaseTitle,
                TorrentInfoHash = $"manual-block-{DateTime.UtcNow.Ticks}", // Synthetic hash for non-torrent blocks
                Reason = BlocklistReason.ManualBlock,
                Message = "Deleted from file management",
                BlockedAt = DateTime.UtcNow
            };
            db.Blocklist.Add(blocklistEntry);
            await db.SaveChangesAsync();
            logger.LogInformation("[FILES] Added release to blocklist: {Title}", releaseTitle);
        }

        // Trigger search for replacement if requested
        if (blocklistAction == "blocklistAndSearch" && evt.Monitored)
        {
            // Use event's profile first, then league's, then let AutomaticSearchService handle fallback
            var qualityProfileId = evt.QualityProfileId ?? evt.League?.QualityProfileId;
            var partName = file.PartName;
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("[FILES] Searching for replacement for event {EventId}, part: {Part}", eventId, partName ?? "all");
                    await searchService.SearchAndDownloadEventAsync(eventId, qualityProfileId, partName, isManualSearch: true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[FILES] Failed to search for replacement for event {EventId}", eventId);
                }
            });
        }
    }

    return Results.Ok(new
    {
        success = true,
        message = deletedFromDisk ? "File deleted from disk and database" : "File removed from database (was not found on disk)",
        eventHasFiles = remainingFiles.Any()
    });
});

// API: Delete all files for an event
// blocklistAction: 'none' | 'blocklistAndSearch' | 'blocklistOnly'
app.MapDelete("/api/events/{id:int}/files", async (
    int id,
    string? blocklistAction,
    SportarrDbContext db,
    ILogger<Program> logger,
    ConfigService configService,
    AutomaticSearchService searchService) =>
{
    var evt = await db.Events
        .Include(e => e.Files)
        .Include(e => e.League)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (evt is null)
        return Results.NotFound(new { error = "Event not found" });

    if (!evt.Files.Any())
        return Results.Ok(new { success = true, message = "No files to delete", deletedCount = 0 });

    logger.LogInformation("[FILES] Deleting all {Count} files for event {EventId} (blocklistAction={BlocklistAction})",
        evt.Files.Count, id, blocklistAction ?? "none");

    // Collect original titles for blocklisting before deletion
    var releasesToBlocklist = evt.Files
        .Select(f => f.OriginalTitle ?? Path.GetFileNameWithoutExtension(f.FilePath))
        .Where(t => !string.IsNullOrEmpty(t))
        .Distinct()
        .ToList();

    var config = await configService.GetConfigAsync();
    var recycleBinPath = config.RecycleBin;
    var useRecycleBin = !string.IsNullOrEmpty(recycleBinPath) && Directory.Exists(recycleBinPath);

    int deletedFromDisk = 0;
    int failedToDelete = 0;

    foreach (var file in evt.Files.ToList())
    {
        if (File.Exists(file.FilePath))
        {
            try
            {
                if (useRecycleBin)
                {
                    var fileName = Path.GetFileName(file.FilePath);
                    var recyclePath = Path.Combine(recycleBinPath!, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
                    File.Move(file.FilePath, recyclePath);
                }
                else
                {
                    File.Delete(file.FilePath);
                }
                deletedFromDisk++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[FILES] Failed to delete file: {FilePath}", file.FilePath);
                failedToDelete++;
            }
        }
    }

    // Remove all files from database
    db.RemoveRange(evt.Files);

    // Update event status
    evt.HasFile = false;
    evt.FilePath = null;
    evt.FileSize = null;
    evt.Quality = null;

    await db.SaveChangesAsync();

    // Handle blocklist action if specified
    if (blocklistAction == "blocklistAndSearch" || blocklistAction == "blocklistOnly")
    {
        // Add all releases to blocklist
        foreach (var releaseTitle in releasesToBlocklist)
        {
            var blocklistEntry = new BlocklistItem
            {
                EventId = id,
                Title = releaseTitle!,
                TorrentInfoHash = $"manual-block-{DateTime.UtcNow.Ticks}-{releaseTitle!.GetHashCode()}", // Synthetic hash
                Reason = BlocklistReason.ManualBlock,
                Message = "Deleted from file management (delete all)",
                BlockedAt = DateTime.UtcNow
            };
            db.Blocklist.Add(blocklistEntry);
        }
        await db.SaveChangesAsync();
        logger.LogInformation("[FILES] Added {Count} releases to blocklist", releasesToBlocklist.Count);

        // Trigger search for replacements if requested
        if (blocklistAction == "blocklistAndSearch" && evt.Monitored)
        {
            // Use event's profile first, then league's, then let AutomaticSearchService handle fallback
            var qualityProfileId = evt.QualityProfileId ?? evt.League?.QualityProfileId;
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation("[FILES] Searching for replacement for event {EventId}", id);
                    await searchService.SearchAndDownloadEventAsync(id, qualityProfileId, null, isManualSearch: true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[FILES] Failed to search for replacement for event {EventId}", id);
                }
            });
        }
    }

    var message = failedToDelete > 0
        ? $"Deleted {deletedFromDisk} files, {failedToDelete} failed to delete from disk"
        : $"Deleted {deletedFromDisk} files";

    logger.LogInformation("[FILES] {Message} for event {EventId}", message, id);

    return Results.Ok(new
    {
        success = failedToDelete == 0,
        message,
        deletedCount = deletedFromDisk,
        failedCount = failedToDelete
    });
});

// API: Update event monitored parts (for fighting sports multi-part episodes)
app.MapPut("/api/events/{id:int}/parts", async (int id, JsonElement body, SportarrDbContext db, ILogger<Program> logger) =>
{
    var evt = await db.Events.FindAsync(id);
    if (evt is null) return Results.NotFound();

    if (body.TryGetProperty("monitoredParts", out var partsValue))
    {
        evt.MonitoredParts = partsValue.ValueKind == JsonValueKind.Null
            ? null
            : partsValue.GetString();

        logger.LogInformation("[EVENT] Updated monitored parts for event {EventId} ({EventTitle}) to: {Parts}",
            id, evt.Title, evt.MonitoredParts ?? "null (use league default)");
    }

    evt.LastUpdate = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(evt);
});

// API: Toggle season monitoring (bulk update all events in a season)
app.MapPut("/api/leagues/{leagueId:int}/seasons/{season}/toggle", async (
    int leagueId,
    string season,
    JsonElement body,
    SportarrDbContext db,
    ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(leagueId);
    if (league is null) return Results.NotFound("League not found");

    if (!body.TryGetProperty("monitored", out var monitoredValue))
        return Results.BadRequest("'monitored' field is required");

    bool monitored = monitoredValue.GetBoolean();

    // Get all events for this league and season
    var events = await db.Events
        .Where(e => e.LeagueId == leagueId && e.Season == season)
        .ToListAsync();

    if (events.Count == 0)
        return Results.NotFound($"No events found for season {season}");

    logger.LogInformation("[SEASON TOGGLE] {Action} season {Season} for league {LeagueName} ({EventCount} events)",
        monitored ? "Monitoring" : "Unmonitoring", season, league.Name, events.Count);

    foreach (var evt in events)
    {
        evt.Monitored = monitored;

        if (monitored)
        {
            // When toggling ON: Set to league's default parts (Option A - always use default, forget custom)
            evt.MonitoredParts = league.MonitoredParts;
        }
        else
        {
            // When toggling OFF: Clear parts (unmonitor everything)
            evt.MonitoredParts = null;
        }

        evt.LastUpdate = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();

    logger.LogInformation("[SEASON TOGGLE] Successfully updated {EventCount} events", events.Count);

    return Results.Ok(new
    {
        message = $"Successfully {(monitored ? "monitored" : "unmonitored")} {events.Count} events in season {season}",
        eventsUpdated = events.Count
    });
});

// REMOVED: FightCard endpoints (obsolete - universal approach uses Event.Monitored)
// REMOVED: Organization endpoints (obsolete - replaced with League-based API)
// - /api/organizations (GET) - Replaced with /api/leagues
// - /api/organizations/{name}/events (GET) - Replaced with /api/leagues/{id}/events

// API: Get tags
app.MapGet("/api/tag", async (SportarrDbContext db) =>
{
    var tags = await db.Tags.ToListAsync();
    return Results.Ok(tags);
});

// API: Get quality profiles
app.MapGet("/api/qualityprofile", async (SportarrDbContext db) =>
{
    var profiles = await db.QualityProfiles.ToListAsync();
    return Results.Ok(profiles);
});

// API: Get single quality profile
app.MapGet("/api/qualityprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.QualityProfiles.FindAsync(id);
    return profile == null ? Results.NotFound() : Results.Ok(profile);
});

// API: Create quality profile
app.MapPost("/api/qualityprofile", async (QualityProfile profile, SportarrDbContext db) =>
{
    // Check for duplicate name
    var existingWithName = await db.QualityProfiles
        .FirstOrDefaultAsync(p => p.Name == profile.Name);

    if (existingWithName != null)
    {
        return Results.BadRequest(new { error = "A quality profile with this name already exists" });
    }

    db.QualityProfiles.Add(profile);
    await db.SaveChangesAsync();
    return Results.Ok(profile);
});

// API: Update quality profile
app.MapPut("/api/qualityprofile/{id}", async (int id, QualityProfile profile, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        var existing = await db.QualityProfiles.FindAsync(id);
        if (existing == null) return Results.NotFound();

        // Check for duplicate name (excluding current profile)
        var duplicateName = await db.QualityProfiles
            .FirstOrDefaultAsync(p => p.Name == profile.Name && p.Id != id);

        if (duplicateName != null)
        {
            return Results.BadRequest(new { error = "A quality profile with this name already exists" });
        }

        existing.Name = profile.Name;
        existing.IsDefault = profile.IsDefault;
        existing.UpgradesAllowed = profile.UpgradesAllowed;
        existing.CutoffQuality = profile.CutoffQuality;
        existing.Items = profile.Items;
        existing.FormatItems = profile.FormatItems;
        existing.MinFormatScore = profile.MinFormatScore;
        existing.CutoffFormatScore = profile.CutoffFormatScore;
        existing.FormatScoreIncrement = profile.FormatScoreIncrement;
        existing.MinSize = profile.MinSize;
        existing.MaxSize = profile.MaxSize;

        await db.SaveChangesAsync();
        return Results.Ok(existing);
    }
    catch (DbUpdateConcurrencyException ex)
    {
        logger.LogError(ex, "[QUALITY PROFILE] Concurrency error updating profile {Id}", id);
        return Results.Conflict(new { error = "Resource was modified by another client. Please refresh and try again." });
    }
});

// API: Delete quality profile
app.MapDelete("/api/qualityprofile/{id}", async (int id, SportarrDbContext db) =>
{
    var profile = await db.QualityProfiles.FindAsync(id);
    if (profile == null) return Results.NotFound();

    db.QualityProfiles.Remove(profile);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Get all custom formats
app.MapGet("/api/customformat", async (SportarrDbContext db) =>
{
    var formats = await db.CustomFormats.ToListAsync();
    return Results.Ok(formats);
});

// API: Get single custom format
app.MapGet("/api/customformat/{id}", async (int id, SportarrDbContext db) =>
{
    var format = await db.CustomFormats.FindAsync(id);
    return format == null ? Results.NotFound() : Results.Ok(format);
});

// API: Create custom format
app.MapPost("/api/customformat", async (CustomFormat format, SportarrDbContext db) =>
{
    format.Created = DateTime.UtcNow;
    db.CustomFormats.Add(format);
    await db.SaveChangesAsync();
    return Results.Ok(format);
});

// API: Update custom format
app.MapPut("/api/customformat/{id}", async (int id, CustomFormat format, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        var existing = await db.CustomFormats.FindAsync(id);
        if (existing == null) return Results.NotFound();

        existing.Name = format.Name;
        existing.IncludeCustomFormatWhenRenaming = format.IncludeCustomFormatWhenRenaming;
        existing.Specifications = format.Specifications;
        existing.LastModified = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(existing);
    }
    catch (DbUpdateConcurrencyException ex)
    {
        logger.LogError(ex, "[CUSTOM FORMAT] Concurrency error updating format {Id}", id);
        return Results.Conflict(new { error = "Resource was modified by another client. Please refresh and try again." });
    }
});

// API: Delete custom format
app.MapDelete("/api/customformat/{id}", async (int id, SportarrDbContext db) =>
{
    var format = await db.CustomFormats.FindAsync(id);
    if (format == null) return Results.NotFound();

    db.CustomFormats.Remove(format);
    await db.SaveChangesAsync();
    return Results.Ok();
});

// API: Import custom format from JSON (compatible with Sonarr export format)
// Handles both simple format and extended format with trash_id/trash_scores metadata
app.MapPost("/api/customformat/import", async (JsonElement jsonData, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        // Extract required fields
        if (!jsonData.TryGetProperty("name", out var nameElement))
        {
            return Results.BadRequest(new { error = "JSON must include 'name' field" });
        }

        var name = nameElement.GetString();
        if (string.IsNullOrEmpty(name))
        {
            return Results.BadRequest(new { error = "Name cannot be empty" });
        }

        // Check if format with same name already exists
        var existingFormat = await db.CustomFormats.FirstOrDefaultAsync(cf => cf.Name == name);
        if (existingFormat != null)
        {
            return Results.Conflict(new { error = $"Custom format '{name}' already exists", existingId = existingFormat.Id });
        }

        var format = new CustomFormat
        {
            Name = name,
            Created = DateTime.UtcNow
        };

        // Optional: includeCustomFormatWhenRenaming
        if (jsonData.TryGetProperty("includeCustomFormatWhenRenaming", out var renamingElement))
        {
            format.IncludeCustomFormatWhenRenaming = renamingElement.GetBoolean();
        }

        // Parse specifications
        if (jsonData.TryGetProperty("specifications", out var specsElement) && specsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var specElement in specsElement.EnumerateArray())
            {
                var spec = new FormatSpecification
                {
                    Name = specElement.TryGetProperty("name", out var specName) ? specName.GetString() ?? "" : "",
                    Implementation = specElement.TryGetProperty("implementation", out var impl) ? impl.GetString() ?? "" : "",
                    Negate = specElement.TryGetProperty("negate", out var negate) && negate.GetBoolean(),
                    Required = specElement.TryGetProperty("required", out var required) && required.GetBoolean(),
                    Fields = new Dictionary<string, object>()
                };

                // Parse fields - handle both Sonarr format and simple format
                if (specElement.TryGetProperty("fields", out var fieldsElement))
                {
                    if (fieldsElement.ValueKind == JsonValueKind.Object)
                    {
                        // Simple format: { "value": "pattern" }
                        foreach (var field in fieldsElement.EnumerateObject())
                        {
                            spec.Fields[field.Name] = field.Value.ValueKind switch
                            {
                                JsonValueKind.String => field.Value.GetString() ?? "",
                                JsonValueKind.Number => field.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => field.Value.ToString()
                            };
                        }
                    }
                    else if (fieldsElement.ValueKind == JsonValueKind.Array)
                    {
                        // Sonarr format: [ { "name": "value", "value": "pattern" } ]
                        foreach (var fieldObj in fieldsElement.EnumerateArray())
                        {
                            if (fieldObj.TryGetProperty("name", out var fieldName) &&
                                fieldObj.TryGetProperty("value", out var fieldValue))
                            {
                                var key = fieldName.GetString() ?? "";
                                spec.Fields[key] = fieldValue.ValueKind switch
                                {
                                    JsonValueKind.String => fieldValue.GetString() ?? "",
                                    JsonValueKind.Number => fieldValue.GetDouble(),
                                    JsonValueKind.True => true,
                                    JsonValueKind.False => false,
                                    _ => fieldValue.ToString()
                                };
                            }
                        }
                    }
                }

                format.Specifications.Add(spec);
            }
        }

        // Get default score from trash_scores if present
        int? defaultScore = null;
        if (jsonData.TryGetProperty("trash_scores", out var scoresElement) &&
            scoresElement.TryGetProperty("default", out var defaultScoreElement))
        {
            defaultScore = defaultScoreElement.GetInt32();
        }

        db.CustomFormats.Add(format);
        await db.SaveChangesAsync();

        logger.LogInformation("[CUSTOM FORMAT] Imported format '{Name}' with {SpecCount} specifications (default score: {Score})",
            format.Name, format.Specifications.Count, defaultScore ?? 0);

        return Results.Ok(new
        {
            id = format.Id,
            name = format.Name,
            specifications = format.Specifications.Count,
            defaultScore = defaultScore,
            message = "Custom format imported successfully"
        });
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "[CUSTOM FORMAT] Invalid JSON in import request");
        return Results.BadRequest(new { error = "Invalid JSON format", details = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[CUSTOM FORMAT] Error importing custom format");
        return Results.Problem("Failed to import custom format");
    }
});

// ==================== TRaSH Guides Sync API ====================

// API: Get TRaSH sync status
app.MapGet("/api/trash/status", async (TrashGuideSyncService trashService) =>
{
    try
    {
        var status = await trashService.GetSyncStatusAsync();
        return Results.Ok(status);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get TRaSH sync status: {ex.Message}");
    }
});

// API: Get available TRaSH custom formats (filtered for sports)
app.MapGet("/api/trash/customformats", async (TrashGuideSyncService trashService, bool sportRelevantOnly = true) =>
{
    try
    {
        var formats = await trashService.GetAvailableCustomFormatsAsync(sportRelevantOnly);
        return Results.Ok(formats);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get TRaSH custom formats: {ex.Message}");
    }
});

// API: Sync all sport-relevant custom formats from TRaSH Guides
app.MapPost("/api/trash/sync", async (TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Starting full sport-relevant sync");
        var result = await trashService.SyncAllSportCustomFormatsAsync();

        if (result.Success)
        {
            logger.LogInformation("[TRaSH API] Sync completed: {Created} created, {Updated} updated",
                result.Created, result.Updated);
        }
        else
        {
            logger.LogWarning("[TRaSH API] Sync failed: {Error}", result.Error);
        }

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Sync failed");
        return Results.Problem($"TRaSH sync failed: {ex.Message}");
    }
});

// API: Sync specific custom formats by TRaSH IDs
app.MapPost("/api/trash/sync/selected", async (List<string> trashIds, TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Syncing {Count} selected custom formats", trashIds.Count);
        var result = await trashService.SyncCustomFormatsByIdsAsync(trashIds);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Selected sync failed");
        return Results.Problem($"TRaSH sync failed: {ex.Message}");
    }
});

// API: Apply TRaSH scores to a quality profile
app.MapPost("/api/trash/apply-scores/{profileId}", async (int profileId, TrashGuideSyncService trashService, ILogger<Program> logger, string scoreSet = "default") =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Applying TRaSH scores to profile {ProfileId} using score set '{ScoreSet}'",
            profileId, scoreSet);
        var result = await trashService.ApplyTrashScoresToProfileAsync(profileId, scoreSet);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to apply scores to profile {ProfileId}", profileId);
        return Results.Problem($"Failed to apply TRaSH scores: {ex.Message}");
    }
});

// API: Reset a custom format to TRaSH defaults
app.MapPost("/api/trash/reset/{formatId}", async (int formatId, TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Resetting custom format {FormatId} to TRaSH defaults", formatId);
        var success = await trashService.ResetCustomFormatToTrashDefaultAsync(formatId);

        if (success)
        {
            return Results.Ok(new { message = "Custom format reset to TRaSH defaults" });
        }
        else
        {
            return Results.NotFound(new { error = "Custom format not found or not synced from TRaSH" });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to reset format {FormatId}", formatId);
        return Results.Problem($"Failed to reset custom format: {ex.Message}");
    }
});

// API: Get available score sets
app.MapGet("/api/trash/scoresets", () =>
{
    return Results.Ok(TrashScoreSets.DisplayNames);
});

// API: Preview sync changes before applying
app.MapGet("/api/trash/preview", async (TrashGuideSyncService trashService, bool sportRelevantOnly = true) =>
{
    try
    {
        var preview = await trashService.PreviewSyncAsync(sportRelevantOnly);
        return Results.Ok(preview);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to preview sync: {ex.Message}");
    }
});

// API: Delete all synced custom formats
app.MapDelete("/api/trash/formats", async (TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Deleting all synced custom formats");
        var result = await trashService.DeleteAllSyncedFormatsAsync();
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to delete synced formats");
        return Results.Problem($"Failed to delete formats: {ex.Message}");
    }
});

// API: Delete specific synced custom formats
app.MapDelete("/api/trash/formats/selected", async ([FromBody] List<int> formatIds, TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Deleting {Count} selected synced formats", formatIds.Count);
        var result = await trashService.DeleteSyncedFormatsByIdsAsync(formatIds);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to delete formats");
        return Results.Problem($"Failed to delete formats: {ex.Message}");
    }
});

// API: Get available TRaSH quality profile templates
app.MapGet("/api/trash/profiles", async (TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] GET /api/trash/profiles - Fetching available profile templates");
        var profiles = await trashService.GetAvailableQualityProfilesAsync();
        logger.LogInformation("[TRaSH API] Returning {Count} profile templates", profiles.Count);

        if (profiles.Count == 0)
        {
            logger.LogWarning("[TRaSH API] No profile templates returned - check TRaSH Sync logs for details");
        }
        else
        {
            foreach (var profile in profiles.Take(3))
            {
                logger.LogInformation("[TRaSH API] Profile: {Name} (TrashId: {TrashId})", profile.Name, profile.TrashId);
            }
        }

        return Results.Ok(profiles);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to get profile templates");
        return Results.Problem($"Failed to get profile templates: {ex.Message}");
    }
});

// API: Create quality profile from TRaSH template
app.MapPost("/api/trash/profiles/create", async (TrashGuideSyncService trashService, ILogger<Program> logger, string trashId, string? customName = null) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Creating profile from template {TrashId}", trashId);
        var (success, error, profileId) = await trashService.CreateProfileFromTemplateAsync(trashId, customName);

        if (success)
            return Results.Ok(new { success = true, profileId });
        else
            return Results.BadRequest(new { success = false, error });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to create profile from template");
        return Results.Problem($"Failed to create profile: {ex.Message}");
    }
});

// API: Get TRaSH sync settings
app.MapGet("/api/trash/settings", async (TrashGuideSyncService trashService) =>
{
    try
    {
        var settings = await trashService.GetSyncSettingsAsync();
        return Results.Ok(settings);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get sync settings: {ex.Message}");
    }
});

// API: Save TRaSH sync settings
app.MapPut("/api/trash/settings", async (TrashSyncSettings settings, TrashGuideSyncService trashService, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[TRaSH API] Saving sync settings (AutoSync: {AutoSync}, Interval: {Interval}h)",
            settings.EnableAutoSync, settings.AutoSyncIntervalHours);
        await trashService.SaveSyncSettingsAsync(settings);
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[TRaSH API] Failed to save sync settings");
        return Results.Problem($"Failed to save settings: {ex.Message}");
    }
});

// API: Get naming template presets
app.MapGet("/api/trash/naming-presets", (TrashGuideSyncService trashService, bool enableMultiPartEpisodes = true) =>
{
    try
    {
        var presets = trashService.GetNamingPresets(enableMultiPartEpisodes);
        return Results.Ok(presets);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get naming presets: {ex.Message}");
    }
});

// ==================== End TRaSH Guides API ====================

// API: Get all delay profiles
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

app.MapPut("/api/qualitydefinition/bulk", async (List<QualityDefinition> definitions, SportarrDbContext db) =>
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
    return Results.Ok();
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

// API: Root Folders Management
app.MapGet("/api/rootfolder", async (SportarrDbContext db) =>
{
    var folders = await db.RootFolders.ToListAsync();
    return Results.Ok(folders);
});

app.MapPost("/api/rootfolder", async (RootFolder folder, SportarrDbContext db) =>
{
    // Check if folder path already exists
    if (await db.RootFolders.AnyAsync(f => f.Path == folder.Path))
    {
        return Results.BadRequest(new { error = "Root folder already exists" });
    }

    // Check folder accessibility
    folder.Accessible = Directory.Exists(folder.Path);
    if (folder.Accessible)
    {
        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(folder.Path) ?? folder.Path);
            folder.FreeSpace = driveInfo.AvailableFreeSpace;
        }
        catch
        {
            folder.FreeSpace = 0;
        }
    }
    folder.LastChecked = DateTime.UtcNow;

    db.RootFolders.Add(folder);
    await db.SaveChangesAsync();
    return Results.Created($"/api/rootfolder/{folder.Id}", folder);
});

app.MapDelete("/api/rootfolder/{id:int}", async (int id, SportarrDbContext db) =>
{
    var folder = await db.RootFolders.FindAsync(id);
    if (folder is null) return Results.NotFound();

    db.RootFolders.Remove(folder);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Filesystem Browser (for root folder selection)
app.MapGet("/api/filesystem", (string? path, bool? includeFiles) =>
{
    try
    {
        // Default to root drives if no path provided
        if (string.IsNullOrEmpty(path))
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new
                {
                    type = "drive",
                    name = d.Name,
                    path = d.RootDirectory.FullName,
                    freeSpace = d.AvailableFreeSpace,
                    totalSpace = d.TotalSize
                })
                .ToList();

            return Results.Ok(new
            {
                parent = (string?)null,
                directories = drives
            });
        }

        // Ensure path exists
        if (!Directory.Exists(path))
        {
            return Results.BadRequest(new { error = "Directory does not exist" });
        }

        var directoryInfo = new DirectoryInfo(path);
        var parent = directoryInfo.Parent?.FullName;

        // Get subdirectories
        var directories = directoryInfo.GetDirectories()
            .Where(d => !d.Attributes.HasFlag(FileAttributes.System) && !d.Attributes.HasFlag(FileAttributes.Hidden))
            .Select(d => new
            {
                type = "folder",
                name = d.Name,
                path = d.FullName,
                lastModified = d.LastWriteTimeUtc
            })
            .OrderBy(d => d.name)
            .ToList();

        // Optionally include files
        object? files = null;
        if (includeFiles == true)
        {
            files = directoryInfo.GetFiles()
                .Where(f => !f.Attributes.HasFlag(FileAttributes.System) && !f.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(f => new
                {
                    type = "file",
                    name = f.Name,
                    path = f.FullName,
                    size = f.Length,
                    lastModified = f.LastWriteTimeUtc
                })
                .OrderBy(f => f.name)
                .ToList();
        }

        return Results.Ok(new
        {
            parent,
            directories,
            files
        });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.BadRequest(new { error = "Access denied to this directory" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API: Notifications Management
app.MapGet("/api/notification", async (SportarrDbContext db) =>
{
    var notifications = await db.Notifications.ToListAsync();
    return Results.Ok(notifications);
});

app.MapPost("/api/notification", async (Notification notification, SportarrDbContext db) =>
{
    notification.Created = DateTime.UtcNow;
    notification.LastModified = DateTime.UtcNow;
    db.Notifications.Add(notification);
    await db.SaveChangesAsync();
    return Results.Created($"/api/notification/{notification.Id}", notification);
});

app.MapPut("/api/notification/{id:int}", async (int id, Notification updatedNotification, SportarrDbContext db) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    notification.Name = updatedNotification.Name;
    notification.Implementation = updatedNotification.Implementation;
    notification.Enabled = updatedNotification.Enabled;
    notification.ConfigJson = updatedNotification.ConfigJson;
    notification.LastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(notification);
});

app.MapDelete("/api/notification/{id:int}", async (int id, SportarrDbContext db) =>
{
    var notification = await db.Notifications.FindAsync(id);
    if (notification is null) return Results.NotFound();

    db.Notifications.Remove(notification);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Config (lightweight endpoint for specific config values)
// Note: Does not require authorization as it only returns non-sensitive feature flags
app.MapGet("/api/config", async (Sportarr.Api.Services.ConfigService configService) =>
{
    var config = await configService.GetConfigAsync();
    return Results.Ok(new { enableMultiPartEpisodes = config.EnableMultiPartEpisodes });
});

// API: Settings Management (using config.xml)
app.MapGet("/api/settings", async (Sportarr.Api.Services.ConfigService configService, SportarrDbContext db) =>
{
    var config = await configService.GetConfigAsync();
    var dbMediaSettings = await db.MediaManagementSettings.FirstOrDefaultAsync();

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

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
            ShowUnknownLeagueItems = config.ShowUnknownLeagueItems,
            ShowEventPath = config.ShowEventPath
        }, jsonOptions),

        MediaManagementSettings = System.Text.Json.JsonSerializer.Serialize(new MediaManagementSettings
        {
            RenameEvents = config.RenameEvents,
            ReplaceIllegalCharacters = config.ReplaceIllegalCharacters,
            EnableMultiPartEpisodes = config.EnableMultiPartEpisodes,
            StandardFileFormat = dbMediaSettings?.StandardFileFormat ?? "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
            EventFolderFormat = dbMediaSettings?.EventFolderFormat ?? "{Series}/Season {Season}",
            RenameFiles = dbMediaSettings?.RenameFiles ?? true,
            CreateEventFolder = dbMediaSettings?.CreateEventFolder ?? true,
            CopyFiles = dbMediaSettings?.CopyFiles ?? false,
            RemoveCompletedDownloads = dbMediaSettings?.RemoveCompletedDownloads ?? true,
            CreateEventFolders = config.CreateEventFolders,
            DeleteEmptyFolders = config.DeleteEmptyFolders,
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
            ChownGroup = config.ChownGroup
        }, jsonOptions),

        // Download handling settings (flat properties for frontend compatibility)
        EnableCompletedDownloadHandling = config.EnableCompletedDownloadHandling,
        RemoveCompletedDownloads = config.RemoveCompletedDownloads,
        CheckForFinishedDownloadInterval = config.CheckForFinishedDownloadInterval,
        EnableFailedDownloadHandling = config.EnableFailedDownloadHandling,
        RedownloadFailedDownloads = config.RedownloadFailedDownloads,
        RemoveFailedDownloads = config.RemoveFailedDownloads,

        // Search Queue Management (Huntarr-style)
        MaxDownloadQueueSize = config.MaxDownloadQueueSize,
        SearchSleepDuration = config.SearchSleepDuration,

        LastModified = DateTime.UtcNow
    };

    return Results.Ok(settings);
});

app.MapPut("/api/settings", async (AppSettings updatedSettings, Sportarr.Api.Services.ConfigService configService, Sportarr.Api.Services.SimpleAuthService simpleAuthService, SportarrDbContext db, Sportarr.Api.Services.FileFormatManager fileFormatManager, ILogger<Program> logger) =>
{
    logger.LogInformation("[CONFIG] Settings update requested");

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

    // Get previous EnableMultiPartEpisodes value to detect changes
    var config = await configService.GetConfigAsync();
    var previousEnableMultiPart = config.EnableMultiPartEpisodes;

    // Handle password hashing if needed
    if (securitySettings != null)
    {
        if (!string.IsNullOrWhiteSpace(securitySettings.Username) &&
            !string.IsNullOrWhiteSpace(securitySettings.Password))
        {
            logger.LogInformation("[AUTH] Updating credentials for user: {Username}", securitySettings.Username);
            await simpleAuthService.SetCredentialsAsync(securitySettings.Username, securitySettings.Password);
            logger.LogInformation("[AUTH] Credentials updated successfully");
        }
        else if (!string.IsNullOrWhiteSpace(securitySettings.Username))
        {
            logger.LogInformation("[AUTH] Username-only update requested for: {Username}", securitySettings.Username);
            await simpleAuthService.SetUsernameAsync(securitySettings.Username);
            logger.LogInformation("[AUTH] Username updated successfully");
        }
    }

    // Update config.xml with all settings
    await configService.UpdateConfigAsync(config =>
    {
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
            config.AuthenticationMethod = securitySettings.AuthenticationMethod;
            config.AuthenticationRequired = securitySettings.AuthenticationRequired;
            config.Username = securitySettings.Username;
            // Don't overwrite API key from frontend (it's read-only, managed by regenerate endpoint)
            config.CertificateValidation = securitySettings.CertificateValidation;
            config.PasswordHash = securitySettings.PasswordHash;
            config.PasswordSalt = securitySettings.PasswordSalt;
            config.PasswordIterations = securitySettings.PasswordIterations;
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
            config.ShowUnknownLeagueItems = uiSettings.ShowUnknownLeagueItems;
            config.ShowEventPath = uiSettings.ShowEventPath;
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
        config.RemoveCompletedDownloads = updatedSettings.RemoveCompletedDownloads;
        config.CheckForFinishedDownloadInterval = updatedSettings.CheckForFinishedDownloadInterval;
        config.EnableFailedDownloadHandling = updatedSettings.EnableFailedDownloadHandling;
        config.RedownloadFailedDownloads = updatedSettings.RedownloadFailedDownloads;
        config.RemoveFailedDownloads = updatedSettings.RemoveFailedDownloads;

        // Search Queue Management (Huntarr-style)
        config.MaxDownloadQueueSize = updatedSettings.MaxDownloadQueueSize;
        config.SearchSleepDuration = updatedSettings.SearchSleepDuration;
    });

    // Update MediaManagementSettings in database
    if (mediaManagementSettings != null)
    {
        var dbSettings = await db.MediaManagementSettings.FirstOrDefaultAsync();
        if (dbSettings == null)
        {
            // Create new settings row if it doesn't exist
            dbSettings = new MediaManagementSettings
            {
                RenameFiles = mediaManagementSettings.RenameFiles,
                StandardFileFormat = mediaManagementSettings.StandardFileFormat ?? "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}",
                EventFolderFormat = mediaManagementSettings.EventFolderFormat ?? "{Series}/Season {Season}",
                CreateEventFolder = mediaManagementSettings.CreateEventFolder,
                RenameEvents = mediaManagementSettings.RenameEvents,
                ReplaceIllegalCharacters = mediaManagementSettings.ReplaceIllegalCharacters,
                CreateEventFolders = mediaManagementSettings.CreateEventFolders,
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
                RemoveCompletedDownloads = mediaManagementSettings.RemoveCompletedDownloads,
                RemoveFailedDownloads = mediaManagementSettings.RemoveFailedDownloads,
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
            dbSettings.EventFolderFormat = mediaManagementSettings.EventFolderFormat;
            dbSettings.CreateEventFolder = mediaManagementSettings.CreateEventFolder;
            dbSettings.RenameEvents = mediaManagementSettings.RenameEvents;
            dbSettings.ReplaceIllegalCharacters = mediaManagementSettings.ReplaceIllegalCharacters;
            dbSettings.CreateEventFolders = mediaManagementSettings.CreateEventFolders;
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
            dbSettings.RemoveCompletedDownloads = mediaManagementSettings.RemoveCompletedDownloads;
            dbSettings.RemoveFailedDownloads = mediaManagementSettings.RemoveFailedDownloads;
            dbSettings.LastModified = DateTime.UtcNow;
            logger.LogInformation("[CONFIG] MediaManagementSettings updated in database");
        }

        await db.SaveChangesAsync();
    }

    // Auto-manage {Part} token when EnableMultiPartEpisodes changes
    var updatedConfig = await configService.GetConfigAsync();
    if (updatedConfig.EnableMultiPartEpisodes != previousEnableMultiPart)
    {
        logger.LogInformation("[CONFIG] EnableMultiPartEpisodes changed from {Old} to {New} - updating file format",
            previousEnableMultiPart, updatedConfig.EnableMultiPartEpisodes);
        await fileFormatManager.UpdateFileFormatForMultiPartSetting(updatedConfig.EnableMultiPartEpisodes);
    }

    logger.LogInformation("[CONFIG] Settings saved to config.xml successfully");
    return Results.Ok(updatedSettings);
});

// API: Regenerate API Key (Sonarr pattern - no restart required)
app.MapPost("/api/settings/apikey/regenerate", async (Sportarr.Api.Services.ConfigService configService, ILogger<Program> logger) =>
{
    logger.LogWarning("[API KEY] API key regeneration requested");
    var newApiKey = await configService.RegenerateApiKeyAsync();
    logger.LogWarning("[API KEY] API key regenerated and saved to config.xml - all connected applications must be updated!");
    return Results.Ok(new { apiKey = newApiKey, message = "API key regenerated successfully. Update all connected applications (Prowlarr, download clients, etc.) with the new key." });
});

// API: Download Clients Management
app.MapGet("/api/downloadclient", async (SportarrDbContext db, ILogger<Program> logger) =>
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

app.MapPost("/api/downloadclient", async (DownloadClient client, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[Download Client] Creating new client {Name} - UrlBase: '{UrlBase}'", client.Name, client.UrlBase);
    client.Created = DateTime.UtcNow;
    db.DownloadClients.Add(client);
    await db.SaveChangesAsync();
    logger.LogInformation("[Download Client] Created client {Name} with ID {Id}", client.Name, client.Id);
    return Results.Created($"/api/downloadclient/{client.Id}", client);
});

app.MapPut("/api/downloadclient/{id:int}", async (int id, DownloadClient updatedClient, SportarrDbContext db, ILogger<Program> logger) =>
{
    var client = await db.DownloadClients.FindAsync(id);
    if (client is null)
    {
        logger.LogWarning("[Download Client] Client ID {Id} not found for update", id);
        return Results.NotFound(new { error = $"Download client with ID {id} not found" });
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
    client.UseSsl = updatedClient.UseSsl;
    client.Enabled = updatedClient.Enabled;
    client.Priority = updatedClient.Priority;
    client.SequentialDownload = updatedClient.SequentialDownload;
    client.FirstAndLastFirst = updatedClient.FirstAndLastFirst;
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
app.MapPost("/api/downloadclient/test", async (DownloadClient client, Sportarr.Api.Services.DownloadClientService downloadClientService) =>
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

// API: Download Queue Management
app.MapGet("/api/queue", async (SportarrDbContext db) =>
{
    // Activity/Queue page: Only show items that haven't been imported yet
    // Imported items should only appear in History (Sonarr/Radarr behavior)
    var queue = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .Where(dq => dq.Status != DownloadStatus.Imported)
        .OrderByDescending(dq => dq.Added)
        .ToListAsync();

    // Map to response format with clean Event serialization
    // The Event model has [JsonPropertyName] attributes for TheSportsDB API deserialization
    // which conflict with frontend expectations (strEvent vs title)
    var response = queue.Select(dq => new
    {
        dq.Id,
        dq.EventId,
        // Map Event to clean format without TheSportsDB JsonPropertyName attributes
        Event = dq.Event != null ? new
        {
            dq.Event.Id,
            ExternalId = dq.Event.ExternalId,
            Title = dq.Event.Title,
            Sport = dq.Event.Sport,
            dq.Event.LeagueId,
            dq.Event.Season,
            dq.Event.SeasonNumber,
            dq.Event.EpisodeNumber,
            dq.Event.EventDate,
            dq.Event.Monitored,
            dq.Event.HasFile
        } : null,
        dq.Title,
        dq.DownloadId,
        dq.DownloadClientId,
        DownloadClient = dq.DownloadClient != null ? new
        {
            dq.DownloadClient.Id,
            dq.DownloadClient.Name,
            dq.DownloadClient.PostImportCategory
        } : null,
        dq.Status,
        dq.Quality,
        dq.Size,
        dq.Downloaded,
        dq.Progress,
        dq.TimeRemaining,
        dq.ErrorMessage,
        dq.StatusMessages,
        dq.Added,
        dq.CompletedAt,
        dq.ImportedAt,
        dq.RetryCount,
        dq.Indexer,
        dq.Protocol,
        dq.TorrentInfoHash,
        dq.QualityScore,
        dq.CustomFormatScore
    });

    return Results.Ok(response);
});

app.MapGet("/api/queue/{id:int}", async (int id, SportarrDbContext db) =>
{
    var dq = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (dq is null) return Results.NotFound();

    // Map to response format with clean Event serialization
    var response = new
    {
        dq.Id,
        dq.EventId,
        Event = dq.Event != null ? new
        {
            dq.Event.Id,
            ExternalId = dq.Event.ExternalId,
            Title = dq.Event.Title,
            Sport = dq.Event.Sport,
            dq.Event.LeagueId,
            dq.Event.Season,
            dq.Event.SeasonNumber,
            dq.Event.EpisodeNumber,
            dq.Event.EventDate,
            dq.Event.Monitored,
            dq.Event.HasFile
        } : null,
        dq.Title,
        dq.DownloadId,
        dq.DownloadClientId,
        DownloadClient = dq.DownloadClient != null ? new
        {
            dq.DownloadClient.Id,
            dq.DownloadClient.Name,
            dq.DownloadClient.PostImportCategory
        } : null,
        dq.Status,
        dq.Quality,
        dq.Size,
        dq.Downloaded,
        dq.Progress,
        dq.TimeRemaining,
        dq.ErrorMessage,
        dq.StatusMessages,
        dq.Added,
        dq.CompletedAt,
        dq.ImportedAt,
        dq.RetryCount,
        dq.Indexer,
        dq.Protocol,
        dq.TorrentInfoHash,
        dq.QualityScore,
        dq.CustomFormatScore
    };

    return Results.Ok(response);
});

app.MapDelete("/api/queue/{id:int}", async (
    int id,
    string removalMethod,
    string blocklistAction,
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<Program> logger) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .Include(dq => dq.Event)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();

    // Handle removal method (Sonarr-style)
    if (item.DownloadClient != null)
    {
        switch (removalMethod)
        {
            case "removeFromClient":
                // Remove download and files from download client
                await downloadClientService.RemoveDownloadAsync(item.DownloadClient, item.DownloadId, deleteFiles: true);
                break;

            case "changeCategory":
                // Change to post-import category (only for completed downloads with PostImportCategory set)
                if (!string.IsNullOrEmpty(item.DownloadClient.PostImportCategory))
                {
                    await downloadClientService.ChangeCategoryAsync(
                        item.DownloadClient,
                        item.DownloadId,
                        item.DownloadClient.PostImportCategory);
                }
                break;

            case "ignoreDownload":
                // Just remove from queue, don't touch download client
                break;

            default:
                return Results.BadRequest($"Invalid removal method: {removalMethod}");
        }
    }

    // Handle blocklist action (Sonarr-style)
    switch (blocklistAction)
    {
        case "blocklistAndSearch":
            // Add to blocklist
            if (!string.IsNullOrEmpty(item.TorrentInfoHash))
            {
                var existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.TorrentInfoHash == item.TorrentInfoHash);

                if (existingBlock == null)
                {
                    var blocklistItem = new BlocklistItem
                    {
                        EventId = item.EventId,
                        Title = item.Title,
                        TorrentInfoHash = item.TorrentInfoHash,
                        Indexer = item.Indexer ?? "Unknown",
                        Reason = BlocklistReason.ManualBlock,
                        Message = "Manually removed and blocklisted",
                        BlockedAt = DateTime.UtcNow
                    };
                    db.Blocklist.Add(blocklistItem);
                }
            }
            // Queue automatic search for replacement (uses its own scope)
            _ = searchQueueService.QueueSearchAsync(item.EventId, part: null, isManualSearch: false);
            break;

        case "blocklistOnly":
            // Add to blocklist without searching
            if (!string.IsNullOrEmpty(item.TorrentInfoHash))
            {
                var existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.TorrentInfoHash == item.TorrentInfoHash);

                if (existingBlock == null)
                {
                    var blocklistItem = new BlocklistItem
                    {
                        EventId = item.EventId,
                        Title = item.Title,
                        TorrentInfoHash = item.TorrentInfoHash,
                        Indexer = item.Indexer ?? "Unknown",
                        Reason = BlocklistReason.ManualBlock,
                        Message = "Manually blocklisted",
                        BlockedAt = DateTime.UtcNow
                    };
                    db.Blocklist.Add(blocklistItem);
                }
            }
            break;

        case "none":
            // No blocklist action
            break;

        default:
            return Results.BadRequest($"Invalid blocklist action: {blocklistAction}");
    }

    // Remove from queue
    // First, delete any import history records that reference this queue item (foreign key constraint)
    var importHistories = await db.ImportHistories
        .Where(h => h.DownloadQueueItemId == item.Id)
        .ToListAsync();

    if (importHistories.Any())
    {
        db.ImportHistories.RemoveRange(importHistories);
    }

    db.DownloadQueue.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Queue Operations - Pause Download
app.MapPost("/api/queue/{id:int}/pause", async (int id, SportarrDbContext db, Sportarr.Api.Services.DownloadClientService downloadClientService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();
    if (item.DownloadClient is null) return Results.BadRequest("No download client assigned");

    // Pause in download client
    var success = await downloadClientService.PauseDownloadAsync(item.DownloadClient, item.DownloadId);

    if (success)
    {
        item.Status = DownloadStatus.Paused;
        item.LastUpdate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }

    return Results.StatusCode(500);
});

// API: Queue Operations - Resume Download
app.MapPost("/api/queue/{id:int}/resume", async (int id, SportarrDbContext db, Sportarr.Api.Services.DownloadClientService downloadClientService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();
    if (item.DownloadClient is null) return Results.BadRequest("No download client assigned");

    // Resume in download client
    var success = await downloadClientService.ResumeDownloadAsync(item.DownloadClient, item.DownloadId);

    if (success)
    {
        item.Status = DownloadStatus.Downloading;
        item.LastUpdate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }

    return Results.StatusCode(500);
});

// API: Queue Operations - Force Import
app.MapPost("/api/queue/{id:int}/import", async (int id, SportarrDbContext db, Sportarr.Api.Services.FileImportService fileImportService) =>
{
    var item = await db.DownloadQueue
        .Include(dq => dq.Event)
        .Include(dq => dq.DownloadClient)
        .FirstOrDefaultAsync(dq => dq.Id == id);

    if (item is null) return Results.NotFound();

    try
    {
        item.Status = DownloadStatus.Importing;
        await db.SaveChangesAsync();

        await fileImportService.ImportDownloadAsync(item);

        item.Status = DownloadStatus.Imported;
        item.ImportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(item);
    }
    catch (Exception ex)
    {
        item.Status = DownloadStatus.Failed;
        item.ErrorMessage = $"Import failed: {ex.Message}";
        await db.SaveChangesAsync();
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API: Pending Imports (Manual Import for External Downloads)
app.MapGet("/api/pending-imports", async (SportarrDbContext db) =>
{
    // Get all pending imports (external downloads needing manual mapping)
    var imports = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .Include(pi => pi.SuggestedEvent)
            .ThenInclude(e => e!.League)
        .Where(pi => pi.Status == PendingImportStatus.Pending)
        .OrderByDescending(pi => pi.Detected)
        .ToListAsync();
    return Results.Ok(imports);
});

app.MapGet("/api/pending-imports/{id:int}", async (int id, SportarrDbContext db) =>
{
    var import = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .Include(pi => pi.SuggestedEvent)
            .ThenInclude(e => e!.League)
        .FirstOrDefaultAsync(pi => pi.Id == id);
    return import is null ? Results.NotFound() : Results.Ok(import);
});

app.MapGet("/api/pending-imports/{id:int}/matches", async (
    int id,
    SportarrDbContext db,
    Sportarr.Api.Services.ImportMatchingService matchingService) =>
{
    // Get all possible event matches for user to choose from
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    var matches = await matchingService.GetAllPossibleMatchesAsync(import.Title);
    return Results.Ok(matches);
});

app.MapPut("/api/pending-imports/{id:int}/suggestion", async (
    int id,
    UpdateSuggestionRequest request,
    SportarrDbContext db) =>
{
    // Update the suggested event/part for a pending import
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    import.SuggestedEventId = request.EventId;
    import.SuggestedPart = request.Part;
    // User manually selected = higher confidence
    import.SuggestionConfidence = 100;

    await db.SaveChangesAsync();
    return Results.Ok(import);
});

app.MapPost("/api/pending-imports/{id:int}/accept", async (
    int id,
    SportarrDbContext db,
    Sportarr.Api.Services.FileImportService fileImportService) =>
{
    // Accept a pending import and perform the actual import
    var import = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .Include(pi => pi.SuggestedEvent)
        .FirstOrDefaultAsync(pi => pi.Id == id);

    if (import is null) return Results.NotFound();
    if (import.SuggestedEventId is null)
        return Results.BadRequest(new { error = "No event selected for import" });

    try
    {
        import.Status = PendingImportStatus.Importing;
        await db.SaveChangesAsync();

        // Create a temporary DownloadQueueItem for the import process
        var tempQueueItem = new DownloadQueueItem
        {
            DownloadClientId = import.DownloadClientId,
            DownloadId = import.DownloadId,
            EventId = import.SuggestedEventId.Value,
            Title = import.Title,
            Size = import.Size,
            Downloaded = import.Size,
            Progress = 100,
            Quality = import.Quality ?? "Unknown",
            Indexer = "Manual Import",
            Status = DownloadStatus.Completed,
            Added = import.Detected,
            CompletedAt = DateTime.UtcNow,
            Protocol = import.Protocol ?? "Unknown",
            TorrentInfoHash = import.TorrentInfoHash
        };

        // Import the download using FileImportService
        // Note: FilePath from download client may not be accessible yet,
        // FileImportService will query the download client for the actual path
        await fileImportService.ImportDownloadAsync(tempQueueItem);

        // Mark as completed
        import.Status = PendingImportStatus.Completed;
        import.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(import);
    }
    catch (Exception ex)
    {
        import.Status = PendingImportStatus.Pending;
        import.ErrorMessage = ex.Message;
        await db.SaveChangesAsync();
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/pending-imports/{id:int}/reject", async (int id, SportarrDbContext db) =>
{
    // Reject a pending import (user doesn't want to import it)
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    import.Status = PendingImportStatus.Rejected;
    import.ResolvedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.MapDelete("/api/pending-imports/{id:int}", async (int id, SportarrDbContext db) =>
{
    // Delete a pending import record
    var import = await db.PendingImports.FindAsync(id);
    if (import is null) return Results.NotFound();

    db.PendingImports.Remove(import);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

// Remove pending import AND remove from download client (Sonarr-style)
app.MapPost("/api/pending-imports/{id:int}/remove-from-client", async (
    int id,
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    ILogger<Program> logger) =>
{
    var import = await db.PendingImports
        .Include(pi => pi.DownloadClient)
        .FirstOrDefaultAsync(pi => pi.Id == id);

    if (import is null) return Results.NotFound();

    // Try to remove from download client
    if (import.DownloadClient != null && !string.IsNullOrEmpty(import.DownloadId))
    {
        try
        {
            await downloadClientService.RemoveDownloadAsync(import.DownloadClient, import.DownloadId, deleteFiles: true);
            logger.LogInformation("[Pending Import] Removed download {Title} from client {Client}",
                import.Title, import.DownloadClient.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Pending Import] Failed to remove from download client, continuing with local removal");
        }
    }

    // Remove from database
    db.PendingImports.Remove(import);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.MapPost("/api/pending-imports/scan", async (Sportarr.Api.Services.ExternalDownloadScanner scanner) =>
{
    // Manually trigger a scan for external downloads
    await scanner.ScanForExternalDownloadsAsync();
    return Results.Ok(new { message = "Scan completed" });
});

// API: Import History Management
app.MapGet("/api/history", async (SportarrDbContext db, int page = 1, int pageSize = 50) =>
{
    // Validate pagination parameters
    if (page < 1) page = 1;
    if (pageSize < 1) pageSize = 1;
    if (pageSize > 500) pageSize = 500; // Prevent excessive data retrieval

    var totalCount = await db.ImportHistories.CountAsync();

    // Use explicit projection to avoid circular reference issues with navigation properties
    var history = await db.ImportHistories
        .OrderByDescending(h => h.ImportedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(h => new {
            h.Id,
            h.EventId,
            // Project just the essential event fields to avoid circular refs
            Event = h.Event == null ? null : new {
                h.Event.Id,
                h.Event.Title,
                Organization = h.Event.League != null ? h.Event.League.Name : null, // Use league name as organization
                h.Event.Sport,
                h.Event.EventDate,
                h.Event.Season,
                h.Event.HasFile
            },
            h.DownloadQueueItemId,
            DownloadQueueItem = h.DownloadQueueItem == null ? null : new {
                h.DownloadQueueItem.Id,
                h.DownloadQueueItem.Title,
                h.DownloadQueueItem.Status
            },
            h.SourcePath,
            h.DestinationPath,
            h.Quality,
            h.Size,
            h.Decision,
            h.Warnings,
            h.Errors,
            h.ImportedAt
        })
        .ToListAsync();

    return Results.Ok(new {
        history,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

// API: Get history for a specific event (with optional part filter for multi-part events)
app.MapGet("/api/event/{eventId:int}/history", async (int eventId, string? part, SportarrDbContext db) =>
{
    // Get import history for this event (optionally filtered by part)
    var importQuery = db.ImportHistories
        .Where(h => h.EventId == eventId);

    // Filter by part if specified - only show records matching that exact part
    // When no part filter is provided, show all history for the event
    if (!string.IsNullOrEmpty(part))
    {
        importQuery = importQuery.Where(h => h.Part == part);
    }

    var importHistory = await importQuery
        .OrderByDescending(h => h.ImportedAt)
        .Select(h => new {
            h.Id,
            Type = "import",
            h.SourcePath,
            h.DestinationPath,
            h.Quality,
            h.Size,
            Decision = h.Decision.ToString(),
            h.Warnings,
            h.Errors,
            Date = h.ImportedAt,
            Indexer = h.DownloadQueueItem != null ? h.DownloadQueueItem.Indexer : null,
            TorrentHash = h.DownloadQueueItem != null ? h.DownloadQueueItem.TorrentInfoHash : null,
            h.Part
        })
        .ToListAsync();

    // Get blocklist entries for this event (optionally filtered by part)
    var blocklistQuery = db.Blocklist
        .Where(b => b.EventId == eventId);

    if (!string.IsNullOrEmpty(part))
    {
        blocklistQuery = blocklistQuery.Where(b => b.Part == part);
    }

    var blocklistHistory = await blocklistQuery
        .OrderByDescending(b => b.BlockedAt)
        .Select(b => new {
            b.Id,
            Type = "blocklist",
            SourcePath = b.Title,
            DestinationPath = (string?)null,
            Quality = (string?)null,
            Size = (long?)null,
            Decision = "Blocklisted",
            Warnings = new List<string>(),
            Errors = new List<string> { b.Message ?? "Blocklisted" },
            Date = b.BlockedAt,
            Indexer = b.Indexer,
            TorrentHash = b.TorrentInfoHash,
            Part = b.Part
        })
        .ToListAsync();

    // Get download queue history (grabbed items) - both current and completed (optionally filtered by part)
    var queueQuery = db.DownloadQueue
        .Where(q => q.EventId == eventId);

    if (!string.IsNullOrEmpty(part))
    {
        queueQuery = queueQuery.Where(q => q.Part == part);
    }

    var queueHistory = await queueQuery
        .OrderByDescending(q => q.Added)
        .Select(q => new {
            q.Id,
            Type = q.Status == DownloadStatus.Completed ? "completed" :
                   q.Status == DownloadStatus.Failed ? "failed" :
                   q.Status == DownloadStatus.Warning ? "warning" : "grabbed",
            SourcePath = q.Title,
            DestinationPath = (string?)null,
            Quality = q.Quality,
            Size = (long?)q.Size,
            Decision = q.Status.ToString(),
            Warnings = new List<string>(),
            Errors = !string.IsNullOrEmpty(q.ErrorMessage) ? new List<string> { q.ErrorMessage } : new List<string>(),
            Date = q.Added,
            Indexer = q.Indexer,
            TorrentHash = q.TorrentInfoHash,
            Part = q.Part
        })
        .ToListAsync();

    // Combine and sort by date
    var allHistory = importHistory
        .Cast<object>()
        .Concat(blocklistHistory.Cast<object>())
        .Concat(queueHistory.Cast<object>())
        .OrderByDescending(h => ((dynamic)h).Date)
        .ToList();

    return Results.Ok(allHistory);
});

app.MapGet("/api/history/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.ImportHistories
        .Include(h => h.Event)
        .Include(h => h.DownloadQueueItem)
        .FirstOrDefaultAsync(h => h.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapDelete("/api/history/{id:int}", async (
    int id,
    string blocklistAction,
    SportarrDbContext db,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<Program> logger) =>
{
    var item = await db.ImportHistories
        .Include(h => h.DownloadQueueItem)
        .FirstOrDefaultAsync(h => h.Id == id);
    if (item is null) return Results.NotFound();

    // Handle blocklist action (Sonarr-style)
    switch (blocklistAction)
    {
        case "blocklistAndSearch":
            // Add to blocklist using the torrent info hash from the download queue item
            var torrentHash = item.DownloadQueueItem?.TorrentInfoHash;
            if (!string.IsNullOrEmpty(torrentHash))
            {
                var existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.TorrentInfoHash == torrentHash);

                if (existingBlock == null)
                {
                    var blocklistItem = new BlocklistItem
                    {
                        EventId = item.EventId,
                        Title = item.SourcePath,
                        TorrentInfoHash = torrentHash,
                        Indexer = item.DownloadQueueItem?.Indexer ?? "Unknown",
                        Reason = BlocklistReason.ManualBlock,
                        Message = "Manually removed from history and blocklisted",
                        BlockedAt = DateTime.UtcNow
                    };
                    db.Blocklist.Add(blocklistItem);
                }
            }
            // Queue automatic search for replacement if we have an event ID (uses its own scope)
            if (item.EventId.HasValue)
            {
                _ = searchQueueService.QueueSearchAsync(item.EventId.Value, part: null, isManualSearch: false);
            }
            break;

        case "blocklistOnly":
            // Add to blocklist without searching
            var hash = item.DownloadQueueItem?.TorrentInfoHash;
            if (!string.IsNullOrEmpty(hash))
            {
                var existingBlock = await db.Blocklist
                    .FirstOrDefaultAsync(b => b.TorrentInfoHash == hash);

                if (existingBlock == null)
                {
                    var blocklistItem = new BlocklistItem
                    {
                        EventId = item.EventId,
                        Title = item.SourcePath,
                        TorrentInfoHash = hash,
                        Indexer = item.DownloadQueueItem?.Indexer ?? "Unknown",
                        Reason = BlocklistReason.ManualBlock,
                        Message = "Manually blocklisted from history",
                        BlockedAt = DateTime.UtcNow
                    };
                    db.Blocklist.Add(blocklistItem);
                }
            }
            break;

        case "none":
        default:
            // No blocklist action
            break;
    }

    db.ImportHistories.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Blocklist Management
app.MapGet("/api/blocklist", async (SportarrDbContext db, int page = 1, int pageSize = 50) =>
{
    var totalCount = await db.Blocklist.CountAsync();
    var blocklist = await db.Blocklist
        .Include(b => b.Event)
        .OrderByDescending(b => b.BlockedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    return Results.Ok(new {
        blocklist,
        page,
        pageSize,
        totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        totalRecords = totalCount
    });
});

app.MapGet("/api/blocklist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.Blocklist
        .Include(b => b.Event)
        .FirstOrDefaultAsync(b => b.Id == id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapPost("/api/blocklist", async (BlocklistItem item, SportarrDbContext db) =>
{
    item.BlockedAt = DateTime.UtcNow;
    db.Blocklist.Add(item);
    await db.SaveChangesAsync();
    return Results.Created($"/api/blocklist/{item.Id}", item);
});

app.MapDelete("/api/blocklist/{id:int}", async (int id, SportarrDbContext db) =>
{
    var item = await db.Blocklist.FindAsync(id);
    if (item is null) return Results.NotFound();

    db.Blocklist.Remove(item);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Wanted/Missing Events
app.MapGet("/api/wanted/missing", async (int page, int pageSize, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[Wanted] GET /api/wanted/missing - page: {Page}, pageSize: {PageSize}", page, pageSize);

        var query = db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Include(e => e.Files)
            .Where(e => e.Monitored && !e.HasFile)
            .OrderBy(e => e.EventDate);

        var totalRecords = await query.CountAsync();
        logger.LogInformation("[Wanted] Found {Count} missing events", totalRecords);

        var events = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var eventResponses = events.Select(EventResponse.FromEvent).ToList();

        return Results.Ok(new
        {
            events = eventResponses,
            page,
            pageSize,
            totalRecords
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Wanted] Error fetching missing events");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Failed to fetch missing events"
        );
    }
});

app.MapGet("/api/wanted/cutoff-unmet", async (int page, int pageSize, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("[Wanted] GET /api/wanted/cutoff-unmet - page: {Page}, pageSize: {PageSize}", page, pageSize);

        // For now, return events that have files but could be upgraded
        // TODO: In a full implementation, this would check against quality profile cutoffs
        var query = db.Events
            .Include(e => e.League)
            .Include(e => e.HomeTeam)
            .Include(e => e.AwayTeam)
            .Include(e => e.Files)
            .Where(e => e.Monitored && e.HasFile && e.Quality != null)
            .OrderBy(e => e.EventDate);

        var totalRecords = await query.CountAsync();
        logger.LogInformation("[Wanted] Found {Count} total events with files and quality", totalRecords);

        var events = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Filter events where quality is below cutoff
        // This is a simplified check - full implementation would compare quality scores
        var cutoffUnmetEvents = events.Where(e =>
        {
            // Simple heuristic: if quality contains "WEB" or "HDTV", it's below cutoff
            // A proper implementation would use the quality profile system
            var quality = e.Quality?.ToLower() ?? "";
            return quality.Contains("web") || quality.Contains("hdtv") || quality.Contains("720p");
        }).ToList();

        logger.LogInformation("[Wanted] Filtered to {Count} events below cutoff", cutoffUnmetEvents.Count);

        var eventResponses = cutoffUnmetEvents.Select(EventResponse.FromEvent).ToList();

        return Results.Ok(new
        {
            events = eventResponses,
            page,
            pageSize,
            totalRecords = eventResponses.Count
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[Wanted] Error fetching cutoff unmet events");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Failed to fetch cutoff unmet events"
        );
    }
});

// API: Indexers Management
app.MapGet("/api/indexer", async (SportarrDbContext db) =>
{
    var indexers = await db.Indexers.OrderBy(i => i.Priority).ToListAsync();

    // Transform to frontend-compatible format with implementation field
    var transformedIndexers = indexers.Select(i => new
    {
        id = i.Id,
        name = i.Name,
        implementation = i.Type.ToString(), // Convert enum to string (Torznab, Newznab, Rss, Torrent)
        enable = i.Enabled,
        enableRss = i.EnableRss,
        enableAutomaticSearch = i.EnableAutomaticSearch,
        enableInteractiveSearch = i.EnableInteractiveSearch,
        priority = i.Priority,
        fields = new object[]
        {
            new { name = "baseUrl", value = i.Url },
            new { name = "apiPath", value = i.ApiPath },
            new { name = "apiKey", value = i.ApiKey ?? "" },
            new { name = "categories", value = string.Join(",", i.Categories) },
            new { name = "animeCategories", value = i.AnimeCategories != null ? string.Join(",", i.AnimeCategories) : "" },
            new { name = "minimumSeeders", value = i.MinimumSeeders.ToString() },
            new { name = "seedRatio", value = i.SeedRatio?.ToString() ?? "" },
            new { name = "seedTime", value = i.SeedTime?.ToString() ?? "" },
            new { name = "seasonPackSeedTime", value = i.SeasonPackSeedTime?.ToString() ?? "" },
            new { name = "earlyReleaseLimit", value = i.EarlyReleaseLimit?.ToString() ?? "" },
            new { name = "additionalParameters", value = i.AdditionalParameters ?? "" },
            new { name = "multiLanguages", value = i.MultiLanguages != null ? string.Join(",", i.MultiLanguages) : "" },
            new { name = "rejectBlocklistedTorrentHashes", value = i.RejectBlocklistedTorrentHashes.ToString() },
            new { name = "downloadClientId", value = i.DownloadClientId?.ToString() ?? "" },
            new { name = "tags", value = i.Tags != null ? string.Join(",", i.Tags) : "" }
        }
    }).ToList();

    return Results.Ok(transformedIndexers);
});

app.MapPost("/api/indexer", async (HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        // Read raw JSON to handle Prowlarr API format from frontend
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[INDEXER CREATE] Received payload: {Json}", json);

        // Deserialize as dynamic JSON to extract fields
        var apiIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Convert Prowlarr API format to Indexer model
        var indexer = new Indexer
        {
            Name = apiIndexer.GetProperty("name").GetString() ?? "Unknown",
            Type = apiIndexer.GetProperty("implementation").GetString()?.ToLower() == "newznab"
                ? IndexerType.Newznab
                : IndexerType.Torznab,
            Url = "",
            ApiKey = "",
            Created = DateTime.UtcNow
        };

        // Extract enable/disable flags if present
        if (apiIndexer.TryGetProperty("enable", out var enable))
        {
            indexer.Enabled = enable.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableRss", out var enableRss))
        {
            indexer.EnableRss = enableRss.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableAutomaticSearch", out var enableAuto))
        {
            indexer.EnableAutomaticSearch = enableAuto.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableInteractiveSearch", out var enableInteractive))
        {
            indexer.EnableInteractiveSearch = enableInteractive.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("priority", out var priority))
        {
            indexer.Priority = priority.GetInt32();
        }

        // Extract fields from the fields array
        if (apiIndexer.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                var fieldName = field.GetProperty("name").GetString();
                var fieldValue = field.TryGetProperty("value", out var val) ? val.GetString() : null;

                switch (fieldName)
                {
                    case "baseUrl":
                        indexer.Url = fieldValue?.TrimEnd('/') ?? "";
                        break;
                    case "apiPath":
                        var apiPath = fieldValue ?? "/api";
                        indexer.ApiPath = apiPath.StartsWith('/') ? apiPath : $"/{apiPath}";
                        break;
                    case "apiKey":
                        indexer.ApiKey = fieldValue;
                        break;
                    case "categories":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.Categories = fieldValue.Split(',').Select(c => c.Trim()).ToList();
                        }
                        break;
                    case "minimumSeeders":
                        if (int.TryParse(fieldValue, out var minSeeders))
                        {
                            indexer.MinimumSeeders = minSeeders;
                        }
                        break;
                    case "seedRatio":
                        if (double.TryParse(fieldValue, out var seedRatio))
                        {
                            indexer.SeedRatio = seedRatio;
                        }
                        break;
                    case "seedTime":
                        if (int.TryParse(fieldValue, out var seedTime))
                        {
                            indexer.SeedTime = seedTime;
                        }
                        break;
                }
            }
        }

        logger.LogInformation("[INDEXER CREATE] Creating {Type} indexer: {Name} at {Url}{ApiPath}",
            indexer.Type, indexer.Name, indexer.Url, indexer.ApiPath);

        db.Indexers.Add(indexer);
        await db.SaveChangesAsync();

        return Results.Created($"/api/indexer/{indexer.Id}", indexer);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[INDEXER CREATE] Failed to create indexer");
        return Results.BadRequest(new { success = false, message = $"Failed to create indexer: {ex.Message}" });
    }
});

app.MapPut("/api/indexer/{id:int}", async (int id, HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    try
    {
        var indexer = await db.Indexers.FindAsync(id);
        if (indexer is null) return Results.NotFound();

        // Read raw JSON to handle Prowlarr API format from frontend
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[INDEXER UPDATE] Received payload for ID {Id}: {Json}", id, json);

        // Deserialize as dynamic JSON to extract fields
        var apiIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Update basic fields
        if (apiIndexer.TryGetProperty("name", out var name))
        {
            indexer.Name = name.GetString() ?? indexer.Name;
        }
        if (apiIndexer.TryGetProperty("implementation", out var impl))
        {
            indexer.Type = impl.GetString()?.ToLower() == "newznab" ? IndexerType.Newznab : IndexerType.Torznab;
        }

        // Update enable/disable flags
        if (apiIndexer.TryGetProperty("enable", out var enable))
        {
            indexer.Enabled = enable.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableRss", out var enableRss))
        {
            indexer.EnableRss = enableRss.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableAutomaticSearch", out var enableAuto))
        {
            indexer.EnableAutomaticSearch = enableAuto.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("enableInteractiveSearch", out var enableInteractive))
        {
            indexer.EnableInteractiveSearch = enableInteractive.GetBoolean();
        }
        if (apiIndexer.TryGetProperty("priority", out var priority))
        {
            indexer.Priority = priority.GetInt32();
        }

        // Extract fields from the fields array
        if (apiIndexer.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                var fieldName = field.GetProperty("name").GetString();
                var fieldValue = field.TryGetProperty("value", out var val) ? val.GetString() : null;

                switch (fieldName)
                {
                    case "baseUrl":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.Url = fieldValue.TrimEnd('/');
                        }
                        break;
                    case "apiPath":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            var apiPath = fieldValue;
                            indexer.ApiPath = apiPath.StartsWith('/') ? apiPath : $"/{apiPath}";
                        }
                        break;
                    case "apiKey":
                        // Only update API key if a new value is provided (not empty)
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.ApiKey = fieldValue;
                        }
                        break;
                    case "categories":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.Categories = fieldValue.Split(',').Select(c => c.Trim()).ToList();
                        }
                        break;
                    case "minimumSeeders":
                        if (int.TryParse(fieldValue, out var minSeeders))
                        {
                            indexer.MinimumSeeders = minSeeders;
                        }
                        break;
                    case "seedRatio":
                        if (double.TryParse(fieldValue, out var seedRatio))
                        {
                            indexer.SeedRatio = seedRatio;
                        }
                        break;
                    case "seedTime":
                        if (int.TryParse(fieldValue, out var seedTime))
                        {
                            indexer.SeedTime = seedTime;
                        }
                        break;
                }
            }
        }

        indexer.LastModified = DateTime.UtcNow;

        logger.LogInformation("[INDEXER UPDATE] Updated {Type} indexer: {Name} at {Url}{ApiPath}",
            indexer.Type, indexer.Name, indexer.Url, indexer.ApiPath);

        await db.SaveChangesAsync();
        return Results.Ok(indexer);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[INDEXER UPDATE] Failed to update indexer {Id}", id);
        return Results.BadRequest(new { success = false, message = $"Failed to update indexer: {ex.Message}" });
    }
});

app.MapDelete("/api/indexer/{id:int}", async (int id, SportarrDbContext db) =>
{
    var indexer = await db.Indexers.FindAsync(id);
    if (indexer is null) return Results.NotFound();

    db.Indexers.Remove(indexer);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// API: Clear all indexer rate limits
app.MapPost("/api/indexer/clearratelimits", async (
    Sportarr.Api.Services.IndexerStatusService indexerStatusService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[INDEXER] Clearing all indexer rate limits");
    var clearedCount = await indexerStatusService.ClearAllRateLimitsAsync();
    return Results.Ok(new { success = true, cleared = clearedCount });
});

// API: Release Search (Indexer Integration)
app.MapPost("/api/release/search", async (
    ReleaseSearchRequest request,
    Sportarr.Api.Services.IndexerSearchService indexerSearchService,
    SportarrDbContext db) =>
{
    // Search all enabled indexers
    var results = await indexerSearchService.SearchAllIndexersAsync(request.Query, request.MaxResultsPerIndexer);

    // If quality profile ID is provided, select best release
    if (request.QualityProfileId.HasValue)
    {
        var qualityProfile = await db.QualityProfiles.FindAsync(request.QualityProfileId.Value);
        if (qualityProfile != null)
        {
            var bestRelease = indexerSearchService.SelectBestRelease(results, qualityProfile);
            if (bestRelease != null)
            {
                results = new List<ReleaseSearchResult> { bestRelease };
            }
        }
    }

    return Results.Ok(results);
});

// API: Test indexer connection
app.MapPost("/api/indexer/test", async (
    HttpRequest request,
    Sportarr.Api.Services.IndexerSearchService indexerSearchService,
    ILogger<Program> logger) =>
{
    try
    {
        // Read raw JSON to handle Prowlarr API format from frontend
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[INDEXER TEST] Received payload: {Json}", json);

        // Deserialize as dynamic JSON to extract fields
        var apiIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Convert Prowlarr API format to Indexer model
        var indexer = new Indexer
        {
            Name = apiIndexer.GetProperty("name").GetString() ?? "Test",
            Type = apiIndexer.GetProperty("implementation").GetString()?.ToLower() == "newznab"
                ? IndexerType.Newznab
                : IndexerType.Torznab,
            Url = "",
            ApiKey = ""
        };

        // Extract fields from the fields array
        if (apiIndexer.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                var fieldName = field.GetProperty("name").GetString();
                var fieldValue = field.GetProperty("value").GetString();

                switch (fieldName)
                {
                    case "baseUrl":
                        // Trim trailing slash from baseUrl to avoid double slashes
                        indexer.Url = fieldValue?.TrimEnd('/') ?? "";
                        break;
                    case "apiPath":
                        // Ensure apiPath starts with slash
                        var apiPath = fieldValue ?? "/api";
                        indexer.ApiPath = apiPath.StartsWith('/') ? apiPath : $"/{apiPath}";
                        break;
                    case "apiKey":
                        indexer.ApiKey = fieldValue;
                        break;
                    case "categories":
                        if (!string.IsNullOrEmpty(fieldValue))
                        {
                            indexer.Categories = fieldValue.Split(',').ToList();
                        }
                        break;
                    case "minimumSeeders":
                        if (int.TryParse(fieldValue, out var minSeeders))
                        {
                            indexer.MinimumSeeders = minSeeders;
                        }
                        break;
                }
            }
        }

        logger.LogInformation("[INDEXER TEST] Testing {Type} indexer: {Name} at {Url}{ApiPath}",
            indexer.Type, indexer.Name, indexer.Url, indexer.ApiPath);
        logger.LogInformation("[INDEXER TEST] ApiKey present: {HasApiKey}, Categories: {Categories}",
            !string.IsNullOrEmpty(indexer.ApiKey), string.Join(",", indexer.Categories ?? new List<string>()));

        var success = await indexerSearchService.TestIndexerAsync(indexer);

        if (success)
        {
            logger.LogInformation("[INDEXER TEST] ✓ Test succeeded for {Name}", indexer.Name);
            return Results.Ok(new { success = true, message = "Connection successful" });
        }

        logger.LogWarning("[INDEXER TEST] ✗ Test failed for {Name}", indexer.Name);
        return Results.BadRequest(new { success = false, message = "Connection failed" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[INDEXER TEST] Error testing indexer: {Message}", ex.Message);
        return Results.BadRequest(new { success = false, message = $"Test failed: {ex.Message}" });
    }
});

// API: Manual search for specific event (Universal: supports all sports)
app.MapPost("/api/event/{eventId:int}/search", async (
    int eventId,
    HttpRequest request,
    SportarrDbContext db,
    Sportarr.Api.Services.IndexerSearchService indexerSearchService,
    Sportarr.Api.Services.EventQueryService eventQueryService,
    Sportarr.Api.Services.ConfigService configService,
    ILogger<Program> logger) =>
{
    // Load config for multi-part episode setting
    var config = await configService.GetConfigAsync();

    // Read optional request body for part parameter
    string? part = null;
    if (request.ContentLength > 0)
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        if (!string.IsNullOrEmpty(json))
        {
            var requestData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            if (requestData.TryGetProperty("part", out var partProp))
            {
                part = partProp.GetString();
            }
        }
    }

    logger.LogInformation("[SEARCH] POST /api/event/{EventId}/search - Manual search initiated{Part}",
        eventId, part != null ? $" (Part: {part})" : "");

    var evt = await db.Events
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.League)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (evt == null)
    {
        logger.LogWarning("[SEARCH] Event {EventId} not found", eventId);
        return Results.NotFound();
    }

    // NOTE: Manual search should work regardless of monitored status
    // User clicking "Search" button explicitly wants to find releases for this event
    // Monitored flag only affects automatic background searches
    if (!evt.Monitored)
    {
        logger.LogInformation("[SEARCH] Event {Title} is not monitored - proceeding with manual search anyway", evt.Title);
    }

    logger.LogInformation("[SEARCH] Event: {Title} | Sport: {Sport} | Monitored: {Monitored}", evt.Title, evt.Sport, evt.Monitored);

    // Get quality profile for evaluation - use event's profile, fallback to league's, then default
    QualityProfile? qualityProfile = null;

    // First try: Event's assigned quality profile
    if (evt.QualityProfileId.HasValue)
    {
        qualityProfile = await db.QualityProfiles
            .FirstOrDefaultAsync(p => p.Id == evt.QualityProfileId.Value);
    }

    // Second try: League's quality profile (if event doesn't have one)
    if (qualityProfile == null && evt.League?.QualityProfileId != null)
    {
        qualityProfile = await db.QualityProfiles
            .FirstOrDefaultAsync(p => p.Id == evt.League.QualityProfileId.Value);
    }

    // Final fallback: Default profile (first by ID)
    if (qualityProfile == null)
    {
        qualityProfile = await db.QualityProfiles
            .OrderBy(q => q.Id)
            .FirstOrDefaultAsync();
    }

    var qualityProfileId = qualityProfile?.Id;

    // Log profile status for debugging
    if (qualityProfile != null)
    {
        var customFormatCount = await db.CustomFormats.CountAsync();
        logger.LogInformation("[SEARCH] Using quality profile '{ProfileName}' (ID: {ProfileId}) for event '{EventTitle}'. {FormatItemCount} format items, {CustomFormatCount} custom formats available.",
            qualityProfile.Name, qualityProfile.Id, evt.Title, qualityProfile.FormatItems?.Count ?? 0, customFormatCount);
    }
    else
    {
        logger.LogWarning("[SEARCH] No quality profile found - custom format scoring will not be applied");
    }

    var allResults = new List<ReleaseSearchResult>();
    var seenGuids = new HashSet<string>();

    // NOTE: Cache removed for manual searches - users expect fresh results each time
    // Automatic searches still use cache for rate limiting optimization

    // UNIVERSAL: Build search queries using sport-agnostic approach
    var queries = eventQueryService.BuildEventQueries(evt, part);

    logger.LogInformation("[SEARCH] Built {Count} prioritized query variations{PartNote}",
        queries.Count, part != null ? $" (Part: {part})" : "");

    // OPTIMIZATION: Intelligent fallback search (matches AutomaticSearchService)
    // Try primary query first, only fallback if insufficient results
    int queriesAttempted = 0;
    const int MinimumResults = 10; // Minimum results before stopping (manual search wants more options)

    foreach (var query in queries)
    {
        queriesAttempted++;
        logger.LogInformation("[SEARCH] Trying query {Attempt}/{Total}: '{Query}'",
            queriesAttempted, queries.Count, query);

        // Pass enableMultiPartEpisodes to ensure proper part filtering
        // When disabled for fighting sports, this rejects releases with detected parts (Main Card, Prelims, etc.)
        // Pass event title for Fight Night detection (base name = Main Card for Fight Nights)
        var results = await indexerSearchService.SearchAllIndexersAsync(query, 50, qualityProfileId, part, evt.Sport, config.EnableMultiPartEpisodes, evt.Title);

        // Deduplicate results by GUID
        foreach (var result in results)
        {
            if (!string.IsNullOrEmpty(result.Guid) && !seenGuids.Contains(result.Guid))
            {
                seenGuids.Add(result.Guid);
                allResults.Add(result);
            }
            else if (string.IsNullOrEmpty(result.Guid))
            {
                allResults.Add(result);
            }
        }

        // Success criteria: Found enough results for user to choose from
        if (allResults.Count >= MinimumResults)
        {
            logger.LogInformation("[SEARCH] Found {Count} results - skipping remaining {Remaining} fallback queries (rate limit optimization)",
                allResults.Count, queries.Count - queriesAttempted);
            break;
        }

        // Log progress if we found some results but not enough
        if (allResults.Count > 0 && allResults.Count < MinimumResults)
        {
            logger.LogInformation("[SEARCH] Found {Count} results (below minimum {Min}) - trying next query",
                allResults.Count, MinimumResults);
        }
        else if (allResults.Count == 0)
        {
            logger.LogWarning("[SEARCH] No results for query '{Query}' - trying next fallback", query);
        }

        // Hard limit: Stop at 100 total results
        if (allResults.Count >= 100)
        {
            logger.LogInformation("[SEARCH] Reached 100 results limit");
            break;
        }
    }

    // Check blocklist status for each result (Sonarr-style: show blocked but mark them)
    var blocklistHashes = await db.Blocklist
        .Select(b => new { b.TorrentInfoHash, b.Message })
        .ToListAsync();
    var blocklistLookup = blocklistHashes.ToDictionary(b => b.TorrentInfoHash, b => b.Message);

    foreach (var result in allResults)
    {
        if (!string.IsNullOrEmpty(result.TorrentInfoHash) && blocklistLookup.ContainsKey(result.TorrentInfoHash))
        {
            result.IsBlocklisted = true;
            result.BlocklistReason = blocklistLookup[result.TorrentInfoHash];
            result.Rejections.Add("Release is blocklisted");
        }
    }

    // Sort results: by score (descending), then by part relevance for multi-part episodes
    // Blocklisted items appear at the bottom but are still visible
    var sortedResults = allResults
        .OrderBy(r => r.IsBlocklisted) // Non-blocklisted first
        .ThenByDescending(r => r.Score)
        .ThenByDescending(r => GetPartRelevanceScore(r.Title, part))
        .ToList();

    logger.LogInformation("[SEARCH] Search completed. Returning {Count} unique results ({Blocked} blocklisted, sorted by score + part relevance)",
        sortedResults.Count, sortedResults.Count(r => r.IsBlocklisted));
    return Results.Ok(sortedResults);
});

// API: Get leagues (universal for all sports)
app.MapGet("/api/leagues", async (SportarrDbContext db, string? sport) =>
{
    var query = db.Leagues.AsQueryable();

    // Filter by sport if provided
    if (!string.IsNullOrEmpty(sport))
    {
        query = query.Where(l => l.Sport == sport);
    }

    var leagues = await query
        .OrderBy(l => l.Sport)
        .ThenBy(l => l.Name)
        .ToListAsync();

    var now = DateTime.UtcNow;

    // Calculate stats for each league
    var response = new List<LeagueResponse>();
    foreach (var league in leagues)
    {
        // Get total events for this league
        var eventCount = await db.Events.CountAsync(e => e.LeagueId == league.Id);

        // Get monitored events count
        var monitoredEventCount = await db.Events.CountAsync(e => e.LeagueId == league.Id && e.Monitored);

        // Get downloaded events count (events with files)
        var fileCount = await db.Events.CountAsync(e => e.LeagueId == league.Id && e.HasFile);

        // Get monitored events that have been downloaded (for progress calculation)
        var downloadedMonitoredCount = await db.Events.CountAsync(e => e.LeagueId == league.Id && e.Monitored && e.HasFile);

        // Check if league has future monitored events (for "continuing" status)
        var hasFutureEvents = await db.Events.AnyAsync(e => e.LeagueId == league.Id && e.Monitored && e.EventDate > now);

        response.Add(LeagueResponse.FromLeague(league, eventCount, monitoredEventCount, fileCount, downloadedMonitoredCount, hasFutureEvents));
    }

    return Results.Ok(response);
});

// API: Get league by ID
app.MapGet("/api/leagues/{id:int}", async (int id, SportarrDbContext db) =>
{
    var league = await db.Leagues
        .Include(l => l.MonitoredTeams)
        .ThenInclude(lt => lt.Team)
        .FirstOrDefaultAsync(l => l.Id == id);

    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get event count and stats
    var events = await db.Events
        .Where(e => e.LeagueId == id)
        .ToListAsync();

    return Results.Ok(new
    {
        league.Id,
        league.ExternalId,
        league.Name,
        league.Sport,
        league.Country,
        league.Description,
        league.Monitored,
        league.MonitorType,
        league.QualityProfileId,
        league.SearchForMissingEvents,
        league.SearchForCutoffUnmetEvents,
        league.MonitoredParts,
        league.MonitoredSessionTypes,
        league.LogoUrl,
        league.BannerUrl,
        league.PosterUrl,
        league.Website,
        league.FormedYear,
        league.Added,
        league.LastUpdate,
        // Monitored teams
        MonitoredTeams = league.MonitoredTeams.Select(lt => new
        {
            lt.Id,
            lt.LeagueId,
            lt.TeamId,
            lt.Monitored,
            lt.Added,
            Team = lt.Team != null ? new
            {
                lt.Team.Id,
                lt.Team.ExternalId,
                lt.Team.Name,
                lt.Team.ShortName,
                lt.Team.BadgeUrl
            } : null
        }).ToList(),
        // Stats
        EventCount = events.Count,
        MonitoredEventCount = events.Count(e => e.Monitored),
        FileCount = events.Count(e => e.HasFile)
    });
});

// API: Get all events for a specific league
app.MapGet("/api/leagues/{id:int}/events", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting events for league ID: {LeagueId}", id);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all events for this league
    var events = await db.Events
        .Include(e => e.HomeTeam)
        .Include(e => e.AwayTeam)
        .Include(e => e.Files)
        .Where(e => e.LeagueId == id)
        .OrderByDescending(e => e.EventDate)
        .ToListAsync();

    // Convert to DTOs
    var response = events.Select(EventResponse.FromEvent).ToList();

    logger.LogInformation("[LEAGUES] Found {Count} events for league: {LeagueName}", response.Count, league.Name);
    return Results.Ok(response);
});

// API: Get all files for a league (across all seasons)
app.MapGet("/api/leagues/{id:int}/files", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting all files for league ID: {LeagueId}", id);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all files for events in this league by querying EventFiles directly with join
    var files = await db.EventFiles
        .Where(f => f.Exists && f.Event != null && f.Event.LeagueId == id)
        .Include(f => f.Event)
        .OrderByDescending(f => f.Event!.EventDate)
        .ThenBy(f => f.PartNumber)
        .Select(f => new
        {
            id = f.Id,
            eventId = f.EventId,
            eventTitle = f.Event!.Title,
            eventDate = f.Event.EventDate,
            season = f.Event.Season ?? "Unknown",
            filePath = f.FilePath,
            size = f.Size,
            quality = f.Quality,
            qualityScore = f.QualityScore,
            customFormatScore = f.CustomFormatScore,
            partName = f.PartName,
            partNumber = f.PartNumber,
            added = f.Added,
            exists = f.Exists,
            fileName = Path.GetFileName(f.FilePath)
        })
        .ToListAsync();

    var totalSize = files.Sum(f => f.size);
    logger.LogInformation("[LEAGUES] Found {Count} files for league: {LeagueName}, Total size: {Size} bytes",
        files.Count, league.Name, totalSize);

    return Results.Ok(new
    {
        leagueId = id,
        leagueName = league.Name,
        totalFiles = files.Count,
        totalSize = totalSize,
        files = files
    });
});

// API: Get all files for a specific season in a league
app.MapGet("/api/leagues/{id:int}/seasons/{season}/files", async (int id, string season, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting files for league ID: {LeagueId}, Season: {Season}", id, season);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all files for events in this league and season by querying EventFiles directly
    var files = await db.EventFiles
        .Where(f => f.Exists && f.Event != null && f.Event.LeagueId == id && f.Event.Season == season)
        .Include(f => f.Event)
        .OrderByDescending(f => f.Event!.EventDate)
        .ThenBy(f => f.PartNumber)
        .Select(f => new
        {
            id = f.Id,
            eventId = f.EventId,
            eventTitle = f.Event!.Title,
            eventDate = f.Event.EventDate,
            season = f.Event.Season ?? "Unknown",
            filePath = f.FilePath,
            size = f.Size,
            quality = f.Quality,
            qualityScore = f.QualityScore,
            customFormatScore = f.CustomFormatScore,
            partName = f.PartName,
            partNumber = f.PartNumber,
            added = f.Added,
            exists = f.Exists,
            fileName = Path.GetFileName(f.FilePath)
        })
        .ToListAsync();

    var totalSize = files.Sum(f => f.size);
    logger.LogInformation("[LEAGUES] Found {Count} files for league: {LeagueName}, Season: {Season}, Total size: {Size} bytes",
        files.Count, league.Name, season, totalSize);

    return Results.Ok(new
    {
        leagueId = id,
        leagueName = league.Name,
        season = season,
        totalFiles = files.Count,
        totalSize = totalSize,
        files = files
    });
});

// API: Get teams by external league ID (for Add League modal - before league is added to DB)
app.MapGet("/api/leagues/external/{externalId}/teams", async (string externalId, TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting teams for external league ID: {ExternalId}", externalId);

    // Fetch teams from TheSportsDB
    var teams = await sportsDbClient.GetLeagueTeamsAsync(externalId);
    if (teams == null || !teams.Any())
    {
        logger.LogWarning("[LEAGUES] No teams found for external league ID: {ExternalId}", externalId);
        return Results.Ok(new List<object>()); // Return empty array instead of error
    }

    logger.LogInformation("[LEAGUES] Found {Count} teams for external league ID: {ExternalId}", teams.Count, externalId);
    return Results.Ok(teams);
});

// API: Get motorsport session types for a league (based on league name)
// Used by the Add League modal to show which sessions can be monitored
app.MapGet("/api/motorsport/session-types", (string leagueName) =>
{
    var sessionTypes = EventPartDetector.GetMotorsportSessionTypes(leagueName);
    return Results.Ok(sessionTypes);
});

// API: Get teams for a league (for team selection in Add League modal)
app.MapGet("/api/leagues/{id:int}/teams", async (int id, SportarrDbContext db, TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Getting teams for league ID: {LeagueId}", id);

    // Verify league exists
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        logger.LogWarning("[LEAGUES] League not found: {LeagueId}", id);
        return Results.NotFound(new { error = "League not found" });
    }

    // Check if league has external ID (required for TheSportsDB API)
    if (string.IsNullOrEmpty(league.ExternalId))
    {
        logger.LogWarning("[LEAGUES] League missing external ID: {LeagueName}", league.Name);
        return Results.BadRequest(new { error = "League is missing TheSportsDB external ID" });
    }

    // Fetch teams from TheSportsDB
    var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId);
    if (teams == null || !teams.Any())
    {
        logger.LogWarning("[LEAGUES] No teams found for league: {LeagueName}", league.Name);
        return Results.Ok(new List<object>()); // Return empty array instead of error
    }

    logger.LogInformation("[LEAGUES] Found {Count} teams for league: {LeagueName}", teams.Count, league.Name);
    return Results.Ok(teams);
});

// API: Update league (including monitor toggle)
app.MapPut("/api/leagues/{id:int}", async (int id, JsonElement body, SportarrDbContext db, ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(id);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Log the raw request body for debugging
    logger.LogInformation("[LEAGUES] Updating league: {Name} (ID: {Id}), Request body properties: {Properties}",
        league.Name, id, string.Join(", ", body.EnumerateObject().Select(p => p.Name)));

    // Track what changed for event updates
    bool monitoredChanged = false;
    bool monitorTypeChanged = false;
    bool sessionTypesChanged = false;
    var oldMonitorType = league.MonitorType;

    // Update properties from JSON body
    if (body.TryGetProperty("monitored", out var monitoredProp))
    {
        var newMonitored = monitoredProp.GetBoolean();
        if (league.Monitored != newMonitored)
        {
            logger.LogInformation("[LEAGUES] Monitored changing from {Old} to {New}", league.Monitored, newMonitored);
            league.Monitored = newMonitored;
            monitoredChanged = true;
        }
        else
        {
            logger.LogDebug("[LEAGUES] Monitored unchanged: {Value}", league.Monitored);
        }
    }

    if (body.TryGetProperty("qualityProfileId", out var qualityProp))
    {
        var newQualityProfileId = qualityProp.ValueKind == JsonValueKind.Null ? null : (int?)qualityProp.GetInt32();
        league.QualityProfileId = newQualityProfileId;
        logger.LogInformation("[LEAGUES] Updated quality profile ID to: {QualityProfileId}", league.QualityProfileId?.ToString() ?? "null");

        // Always apply quality profile to ALL events in this league (monitored or not)
        // User can override individual events if needed, but league setting cascades to all
        var eventsToUpdate = await db.Events
            .Where(e => e.LeagueId == id)
            .ToListAsync();

        if (eventsToUpdate.Count > 0)
        {
            logger.LogInformation("[LEAGUES] Cascading quality profile {ProfileId} to {Count} events in league",
                newQualityProfileId?.ToString() ?? "null", eventsToUpdate.Count);

            foreach (var evt in eventsToUpdate)
            {
                evt.QualityProfileId = newQualityProfileId;
                evt.LastUpdate = DateTime.UtcNow;
            }

            logger.LogInformation("[LEAGUES] Successfully updated quality profile for {Count} events", eventsToUpdate.Count);
        }
    }

    if (body.TryGetProperty("monitorType", out var monitorTypeProp))
    {
        var monitorTypeStr = monitorTypeProp.GetString();
        if (Enum.TryParse<MonitorType>(monitorTypeStr, out var monitorType))
        {
            if (league.MonitorType != monitorType)
            {
                logger.LogInformation("[LEAGUES] MonitorType changing from {Old} to {New}", league.MonitorType, monitorType);
                league.MonitorType = monitorType;
                monitorTypeChanged = true;
            }
            else
            {
                logger.LogDebug("[LEAGUES] MonitorType unchanged: {Value}", league.MonitorType);
            }
        }
        else
        {
            logger.LogWarning("[LEAGUES] Failed to parse MonitorType: {Value}", monitorTypeStr);
        }
    }

    if (body.TryGetProperty("searchForMissingEvents", out var searchMissingProp))
    {
        league.SearchForMissingEvents = searchMissingProp.GetBoolean();
        logger.LogInformation("[LEAGUES] Updated search for missing events to: {SearchForMissingEvents}", league.SearchForMissingEvents);
    }

    if (body.TryGetProperty("searchForCutoffUnmetEvents", out var searchCutoffProp))
    {
        league.SearchForCutoffUnmetEvents = searchCutoffProp.GetBoolean();
        logger.LogInformation("[LEAGUES] Updated search for cutoff unmet events to: {SearchForCutoffUnmetEvents}", league.SearchForCutoffUnmetEvents);
    }

    if (body.TryGetProperty("monitoredParts", out var monitoredPartsProp))
    {
        league.MonitoredParts = monitoredPartsProp.ValueKind == JsonValueKind.Null ? null : monitoredPartsProp.GetString();
        logger.LogInformation("[LEAGUES] Updated monitored parts to: {MonitoredParts}", league.MonitoredParts ?? "all parts (default)");

        // Always apply monitored parts to ALL events in this league
        // User can override individual events if needed, but league setting cascades to all
        var eventsToUpdate = await db.Events
            .Where(e => e.LeagueId == id)
            .ToListAsync();

        if (eventsToUpdate.Count > 0)
        {
            logger.LogInformation("[LEAGUES] Cascading monitored parts to {Count} events: {Parts}",
                eventsToUpdate.Count, league.MonitoredParts ?? "all parts");

            foreach (var evt in eventsToUpdate)
            {
                evt.MonitoredParts = league.MonitoredParts;
                evt.LastUpdate = DateTime.UtcNow;
            }

            logger.LogInformation("[LEAGUES] Successfully updated monitored parts for {Count} events", eventsToUpdate.Count);
        }
    }

    // Handle monitored session types for motorsport leagues (currently only F1)
    if (body.TryGetProperty("monitoredSessionTypes", out var sessionTypesProp))
    {
        var newSessionTypes = sessionTypesProp.ValueKind == JsonValueKind.Null ? null : sessionTypesProp.GetString();
        if (league.MonitoredSessionTypes != newSessionTypes)
        {
            logger.LogInformation("[LEAGUES] MonitoredSessionTypes changing from '{Old}' to '{New}'",
                league.MonitoredSessionTypes ?? "(all)", newSessionTypes ?? "(all)");
            league.MonitoredSessionTypes = newSessionTypes;
            sessionTypesChanged = true;
        }
        else
        {
            logger.LogDebug("[LEAGUES] MonitoredSessionTypes unchanged: {Value}", league.MonitoredSessionTypes ?? "(all)");
        }
    }

    // Determine if we need to recalculate event monitoring
    // This happens when: monitored, monitorType, or sessionTypes changes
    bool needsEventUpdate = monitoredChanged || monitorTypeChanged || sessionTypesChanged;
    logger.LogInformation("[LEAGUES] Event update needed: {Needed} (monitoredChanged={MC}, monitorTypeChanged={MTC}, sessionTypesChanged={STC})",
        needsEventUpdate, monitoredChanged, monitorTypeChanged, sessionTypesChanged);

    if (needsEventUpdate)
    {
        var allEvents = await db.Events
            .Where(e => e.LeagueId == id)
            .ToListAsync();

        logger.LogInformation("[LEAGUES] Recalculating monitoring for {Count} events in league {Name}", allEvents.Count, league.Name);

        if (allEvents.Count > 0)
        {
            var currentSeason = DateTime.UtcNow.Year.ToString();
            int monitoredCount = 0;
            int unmonitoredCount = 0;
            int unchangedCount = 0;

            foreach (var evt in allEvents)
            {
                // Base monitoring: is the league monitored?
                bool shouldMonitor = league.Monitored;

                // Apply MonitorType filter (All, Future, CurrentSeason, etc.)
                if (shouldMonitor)
                {
                    shouldMonitor = league.MonitorType switch
                    {
                        MonitorType.All => true,
                        MonitorType.Future => evt.EventDate > DateTime.UtcNow,
                        MonitorType.CurrentSeason => evt.Season == currentSeason,
                        MonitorType.LatestSeason => evt.Season == currentSeason,
                        MonitorType.NextSeason => !string.IsNullOrEmpty(evt.Season) &&
                                                  int.TryParse(evt.Season.Split('-')[0], out var year) &&
                                                  year == DateTime.UtcNow.Year + 1,
                        MonitorType.Recent => evt.EventDate >= DateTime.UtcNow.AddDays(-30),
                        MonitorType.None => false,
                        _ => true
                    };
                }

                // Apply motorsport session type filter (only for F1 currently)
                // Note: null = all sessions, "" = no sessions, "Race,Qualifying" = specific sessions
                if (shouldMonitor && league.Sport == "Motorsport" && league.MonitoredSessionTypes != null)
                {
                    var isSessionMonitored = EventPartDetector.IsMotorsportSessionMonitored(evt.Title, league.Name, league.MonitoredSessionTypes);
                    logger.LogDebug("[LEAGUES] Event '{Title}': session type filter applied, monitored = {IsMonitored} (filter: '{Filter}')",
                        evt.Title, isSessionMonitored, league.MonitoredSessionTypes);
                    shouldMonitor = isSessionMonitored;
                }

                // Update if changed
                if (evt.Monitored != shouldMonitor)
                {
                    logger.LogDebug("[LEAGUES] Event '{Title}' monitoring changing from {Old} to {New}", evt.Title, evt.Monitored, shouldMonitor);
                    evt.Monitored = shouldMonitor;
                    evt.LastUpdate = DateTime.UtcNow;
                    if (shouldMonitor) monitoredCount++;
                    else unmonitoredCount++;
                }
                else
                {
                    unchangedCount++;
                }
            }

            logger.LogInformation("[LEAGUES] Event monitoring updated: {Monitored} now monitored, {Unmonitored} now unmonitored, {Unchanged} unchanged",
                monitoredCount, unmonitoredCount, unchangedCount);
        }
    }
    else
    {
        logger.LogInformation("[LEAGUES] No event update needed - no monitoring-related settings changed");
    }

    league.LastUpdate = DateTime.UtcNow;
    await db.SaveChangesAsync();

    logger.LogInformation("[LEAGUES] Successfully updated league: {Name}", league.Name);
    return Results.Ok(LeagueResponse.FromLeague(league));
});

// API: Get all leagues from TheSportsDB (cached)
app.MapGet("/api/leagues/all", async (Sportarr.Api.Services.TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] Fetching all leagues from cache");

    var results = await sportsDbClient.GetAllLeaguesAsync();

    if (results == null || !results.Any())
    {
        logger.LogWarning("[LEAGUES] No leagues found in cache");
        return Results.Ok(new List<object>());
    }

    // Debug: Log a sample league to see if LogoUrl is populated
    var sampleWithLogo = results.FirstOrDefault(l => !string.IsNullOrEmpty(l.LogoUrl));
    var sampleWithoutLogo = results.FirstOrDefault(l => string.IsNullOrEmpty(l.LogoUrl));
    var leaguesWithLogos = results.Count(l => !string.IsNullOrEmpty(l.LogoUrl));
    logger.LogInformation("[LEAGUES] Found {Count} leagues, {WithLogos} have logos", results.Count, leaguesWithLogos);
    if (sampleWithLogo != null)
        logger.LogInformation("[LEAGUES] Sample with logo: {Name} - LogoUrl: {Logo}", sampleWithLogo.Name, sampleWithLogo.LogoUrl);
    if (sampleWithoutLogo != null)
        logger.LogInformation("[LEAGUES] Sample without logo: {Name} - ExternalId: {Id}", sampleWithoutLogo.Name, sampleWithoutLogo.ExternalId);

    // Convert to DTO to ensure correct field names for frontend (strBadge, strLogo, etc.)
    var dtos = results.Select(TheSportsDBLeagueDto.FromLeague).ToList();
    return Results.Ok(dtos);
});

// API: Search leagues from TheSportsDB
app.MapGet("/api/leagues/search/{query}", async (string query, Sportarr.Api.Services.TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES SEARCH] Searching for: {Query}", query);

    var results = await sportsDbClient.SearchLeagueAsync(query);

    if (results == null || !results.Any())
    {
        logger.LogWarning("[LEAGUES SEARCH] No results found for: {Query}", query);
        return Results.Ok(new List<object>());
    }

    logger.LogInformation("[LEAGUES SEARCH] Found {Count} results", results.Count);
    // Convert to DTO to ensure correct field names for frontend (strBadge, strLogo, etc.)
    var dtos = results.Select(TheSportsDBLeagueDto.FromLeague).ToList();
    return Results.Ok(dtos);
});

// API: Add league to library
app.MapPost("/api/leagues", async (HttpContext context, SportarrDbContext db, IServiceScopeFactory scopeFactory, TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues - Request received");

    // Enable buffering to allow reading the request body multiple times
    context.Request.EnableBuffering();

    // Read and log the raw request body for debugging
    using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
    var requestBody = await reader.ReadToEndAsync();
    logger.LogInformation("[LEAGUES] Request body: {Body}", requestBody);

    // Reset stream position for potential re-reading
    context.Request.Body.Position = 0;

    // Deserialize the AddLeagueRequest DTO from the request body
    // Use DTO to avoid JsonPropertyName conflicts (strLeague vs name)
    AddLeagueRequest? request;
    try
    {
        request = JsonSerializer.Deserialize<AddLeagueRequest>(requestBody, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (request == null)
        {
            logger.LogError("[LEAGUES] Failed to deserialize league request from request body");
            return Results.BadRequest(new { error = "Invalid league data" });
        }

        logger.LogInformation("[LEAGUES] Deserialized request - Name: {Name}, Sport: {Sport}, ExternalId: {ExternalId}",
            request.Name, request.Sport, request.ExternalId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] JSON deserialization error: {Message}", ex.Message);
        return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
    }

    try
    {
        // Convert DTO to League entity
        var league = request.ToLeague();

        // Enrich league with full details (including images) if missing
        // The /all/leagues endpoint doesn't include images, so fetch from lookup
        if (string.IsNullOrEmpty(league.LogoUrl) && !string.IsNullOrEmpty(league.ExternalId))
        {
            logger.LogInformation("[LEAGUES] Fetching full league details to get images for: {Name}", league.Name);
            var fullDetails = await sportsDbClient.LookupLeagueAsync(league.ExternalId);
            if (fullDetails != null)
            {
                league.LogoUrl = fullDetails.LogoUrl;
                league.BannerUrl = fullDetails.BannerUrl;
                league.PosterUrl = fullDetails.PosterUrl;
                league.Description = fullDetails.Description ?? league.Description;
                league.Website = fullDetails.Website ?? league.Website;
                logger.LogInformation("[LEAGUES] Enriched league with images - Logo: {HasLogo}, Banner: {HasBanner}, Poster: {HasPoster}",
                    !string.IsNullOrEmpty(league.LogoUrl), !string.IsNullOrEmpty(league.BannerUrl), !string.IsNullOrEmpty(league.PosterUrl));
            }
            else
            {
                logger.LogWarning("[LEAGUES] Could not fetch full details for league: {ExternalId}", league.ExternalId);
            }
        }

        logger.LogInformation("[LEAGUES] Adding league to database: {Name} ({Sport})", league.Name, league.Sport);

        // Check if league already exists
        var existing = await db.Leagues
            .FirstOrDefaultAsync(l => l.ExternalId == league.ExternalId && !string.IsNullOrEmpty(league.ExternalId));

        if (existing != null)
        {
            logger.LogWarning("[LEAGUES] League already exists: {Name} (ExternalId: {ExternalId})", league.Name, league.ExternalId);
            return Results.BadRequest(new { error = "League already exists in library" });
        }

        // Added timestamp is already set in ToLeague()
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        logger.LogInformation("[LEAGUES] Successfully added league: {Name} with ID {Id}", league.Name, league.Id);

        // Handle monitored teams if specified (for team-based filtering)
        if (request.MonitoredTeamIds != null && request.MonitoredTeamIds.Any())
        {
            logger.LogInformation("[LEAGUES] Processing {Count} monitored teams for league: {Name}",
                request.MonitoredTeamIds.Count, league.Name);

            foreach (var teamExternalId in request.MonitoredTeamIds)
            {
                // Find or create team in database
                var team = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == teamExternalId);

                if (team == null)
                {
                    // Fetch team details from TheSportsDB to populate Team table
                    var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId!);
                    var teamData = teams?.FirstOrDefault(t => t.ExternalId == teamExternalId);

                    if (teamData != null)
                    {
                        team = teamData;
                        team.LeagueId = league.Id;
                        team.Sport = league.Sport; // Populate from league since API doesn't return it
                        db.Teams.Add(team);
                        await db.SaveChangesAsync();
                        logger.LogInformation("[LEAGUES] Added new team: {TeamName} (ExternalId: {ExternalId})",
                            team.Name, team.ExternalId);
                    }
                    else
                    {
                        logger.LogWarning("[LEAGUES] Could not find team with ExternalId: {ExternalId}", teamExternalId);
                        continue;
                    }
                }

                // Create LeagueTeam entry
                var leagueTeam = new LeagueTeam
                {
                    LeagueId = league.Id,
                    TeamId = team.Id,
                    Monitored = true
                };

                db.LeagueTeams.Add(leagueTeam);
                logger.LogInformation("[LEAGUES] Marked team as monitored: {TeamName} for league: {LeagueName}",
                    team.Name, league.Name);
            }

            await db.SaveChangesAsync();
            logger.LogInformation("[LEAGUES] Successfully configured {Count} monitored teams", request.MonitoredTeamIds.Count);
        }
        else
        {
            // Check if this is a motorsport league - motorsports don't use team selection
            var isMotorsport = league.Sport == "Motorsport";

            if (!isMotorsport)
            {
                // Non-motorsport leagues require team selection
                logger.LogInformation("[LEAGUES] No teams selected - league added but not monitored (no events will be synced)");
                league.Monitored = false;

                // For fighting sports: DO NOT clear MonitoredParts when no teams selected
                // The user's part selection preferences should be preserved in the database
                // The backend logic (automatic search) will check for monitored teams before searching
                // This allows users to keep their preferred parts selected in the UI
                if (Sportarr.Api.Services.EventPartDetector.IsFightingSport(league.Sport))
                {
                    logger.LogInformation("[LEAGUES] Fighting sport with no teams - parts preference preserved but no events will be monitored");
                }

                await db.SaveChangesAsync();

                return Results.Ok(new {
                    message = "League added successfully (not monitored - no teams selected)",
                    leagueId = league.Id,
                    monitored = false
                });
            }

            // Motorsport league - proceed with sync (no team selection needed)
            var availableSessionTypes = EventPartDetector.GetMotorsportSessionTypes(league.Name);
            if (availableSessionTypes.Any())
            {
                logger.LogInformation("[LEAGUES] Motorsport league with session type support ({Count} available): {SessionTypes}",
                    availableSessionTypes.Count, request.MonitoredSessionTypes ?? "(all sessions)");
            }
            else
            {
                logger.LogInformation("[LEAGUES] Motorsport league without session type definitions - will sync all events");
            }
        }

        // Automatically sync events for the newly added league
        // This runs in the background to populate all events (past, present, future)
        logger.LogInformation("[LEAGUES] Triggering automatic event sync for league: {Name}", league.Name);
        var leagueId = league.Id;
        var leagueName = league.Name;
        _ = Task.Run(async () =>
        {
            try
            {
                // Create a new scope for the background task to avoid using disposed DbContext
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<Sportarr.Api.Services.LeagueEventSyncService>();

                var syncResult = await syncService.SyncLeagueEventsAsync(leagueId);
                logger.LogInformation("[LEAGUES] Auto-sync completed for {Name}: {Message}",
                    leagueName, syncResult.Message);
            }
            catch (Exception syncEx)
            {
                logger.LogError(syncEx, "[LEAGUES] Auto-sync failed for {Name}: {Message}",
                    leagueName, syncEx.Message);
            }
        });

        // Convert to DTO for frontend response
        var response = LeagueResponse.FromLeague(league);
        return Results.Created($"/api/leagues/{league.Id}", response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error adding league: {Name}. Error: {Message}", request?.Name ?? "Unknown", ex.Message);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error adding league"
        );
    }
});

// API: Update league
// Removed duplicate PUT endpoint - now using JsonElement-based endpoint above for partial updates

// API: Update monitored teams for a league
app.MapPut("/api/leagues/{id:int}/teams", async (int id, UpdateMonitoredTeamsRequest request, SportarrDbContext db, TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    // Use a transaction to ensure all changes succeed or fail together
    using var transaction = await db.Database.BeginTransactionAsync();

    try
    {
        logger.LogInformation("[LEAGUES] Updating monitored teams for league ID: {LeagueId}", id);

        var league = await db.Leagues
            .Include(l => l.MonitoredTeams)
            .ThenInclude(lt => lt.Team)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (league == null)
        {
            return Results.NotFound(new { error = "League not found" });
        }

        // Remove existing monitored teams
        var existingTeams = await db.LeagueTeams.Where(lt => lt.LeagueId == id).ToListAsync();
        db.LeagueTeams.RemoveRange(existingTeams);

        // If no teams provided, set league as not monitored
        if (request.MonitoredTeamIds == null || !request.MonitoredTeamIds.Any())
        {
            logger.LogInformation("[LEAGUES] No teams selected - setting league as not monitored");
            league.Monitored = false;

            // For fighting sports, also set MonitoredParts to empty to indicate no parts are monitored
            // This ensures consistency: no teams = no events = no parts should be monitored
            if (Sportarr.Api.Services.EventPartDetector.IsFightingSport(league.Sport))
            {
                league.MonitoredParts = ""; // Empty string = no parts monitored
                logger.LogInformation("[LEAGUES] Fighting sport with no teams - setting MonitoredParts to empty (no parts monitored)");
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { message = "League updated - no teams monitored", leagueId = league.Id });
        }

        // Add new monitored teams
        logger.LogInformation("[LEAGUES] Adding {Count} monitored teams", request.MonitoredTeamIds.Count);

        foreach (var teamExternalId in request.MonitoredTeamIds)
        {
            // Find or create team in database
            var team = await db.Teams.FirstOrDefaultAsync(t => t.ExternalId == teamExternalId);

            if (team == null)
            {
                // Fetch team details from TheSportsDB
                var teams = await sportsDbClient.GetLeagueTeamsAsync(league.ExternalId!);
                var teamData = teams?.FirstOrDefault(t => t.ExternalId == teamExternalId);

                if (teamData != null)
                {
                    team = teamData;
                    team.LeagueId = league.Id;
                    team.Sport = league.Sport; // Populate from league since API doesn't return it
                    db.Teams.Add(team);
                    // Save immediately to get the team ID before creating LeagueTeam relationship
                    await db.SaveChangesAsync();
                    logger.LogInformation("[LEAGUES] Added new team: {TeamName} (ExternalId: {ExternalId}, Id: {Id})",
                        team.Name, team.ExternalId, team.Id);
                }
                else
                {
                    logger.LogWarning("[LEAGUES] Could not find team with ExternalId: {ExternalId}", teamExternalId);
                    continue;
                }
            }

            // Create LeagueTeam entry - team.Id is now guaranteed to be valid
            var leagueTeam = new LeagueTeam
            {
                LeagueId = league.Id,
                TeamId = team.Id,
                Monitored = true
            };

            db.LeagueTeams.Add(leagueTeam);
            logger.LogInformation("[LEAGUES] Marked team as monitored: {TeamName} for league: {LeagueName}",
                team.Name, league.Name);
        }

        // Set league as monitored
        league.Monitored = true;

        // Save all changes and commit transaction
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation("[LEAGUES] Successfully updated {Count} monitored teams", request.MonitoredTeamIds.Count);
        return Results.Ok(new { message = "Monitored teams updated successfully", leagueId = league.Id, teamCount = request.MonitoredTeamIds.Count });
    }
    catch (Exception ex)
    {
        // Rollback transaction on any error
        await transaction.RollbackAsync();
        logger.LogError(ex, "[LEAGUES] Error updating monitored teams for league ID: {LeagueId}", id);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error updating monitored teams"
        );
    }
});

// API: Delete league
app.MapDelete("/api/leagues/{id:int}", async (int id, bool deleteFiles, SportarrDbContext db, ILogger<Program> logger) =>
{
    var league = await db.Leagues.FindAsync(id);

    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    logger.LogInformation("[LEAGUES] Deleting league: {Name} (deleteFiles: {DeleteFiles})", league.Name, deleteFiles);

    // Delete all events associated with this league (cascade delete, like Sonarr deleting show + episodes)
    var events = await db.Events.Where(e => e.LeagueId == id).ToListAsync();
    var eventIds = events.Select(e => e.Id).ToList();

    // Get all event files before deleting from database
    var eventFiles = eventIds.Any()
        ? await db.EventFiles.Where(ef => eventIds.Contains(ef.EventId)).ToListAsync()
        : new List<EventFile>();

    // Track league folders to delete (collect unique league folders from file paths)
    var leagueFoldersToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (deleteFiles && eventFiles.Any())
    {
        logger.LogInformation("[LEAGUES] Deleting {Count} event files for league: {Name}", eventFiles.Count, league.Name);

        foreach (var eventFile in eventFiles)
        {
            try
            {
                if (File.Exists(eventFile.FilePath))
                {
                    // Extract the league folder path from the file path
                    // File structure: {RootFolder}/{LeagueName}/Season {Year}/{filename}
                    // We want to delete: {RootFolder}/{LeagueName}/
                    var fileDir = Path.GetDirectoryName(eventFile.FilePath);
                    if (!string.IsNullOrEmpty(fileDir))
                    {
                        // Go up one level from "Season {Year}" to get the league folder
                        var seasonDir = fileDir;
                        var leagueDir = Path.GetDirectoryName(seasonDir);
                        if (!string.IsNullOrEmpty(leagueDir))
                        {
                            leagueFoldersToDelete.Add(leagueDir);
                        }
                    }

                    File.Delete(eventFile.FilePath);
                    logger.LogDebug("[LEAGUES] Deleted file: {Path}", eventFile.FilePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[LEAGUES] Failed to delete file: {Path}", eventFile.FilePath);
            }
        }

        // Delete league folders (the {LeagueName} directory that contains all Season folders)
        foreach (var leagueFolder in leagueFoldersToDelete)
        {
            try
            {
                if (Directory.Exists(leagueFolder))
                {
                    Directory.Delete(leagueFolder, recursive: true);
                    logger.LogInformation("[LEAGUES] Deleted league folder: {Path}", leagueFolder);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[LEAGUES] Failed to delete league folder: {Path}", leagueFolder);
            }
        }
    }

    if (events.Any())
    {
        logger.LogInformation("[LEAGUES] Deleting {Count} events for league: {Name}", events.Count, league.Name);
        db.Events.RemoveRange(events);
    }

    db.Leagues.Remove(league);
    await db.SaveChangesAsync();

    var filesDeletedMsg = deleteFiles ? $", {eventFiles.Count} files deleted" : "";
    var foldersDeletedMsg = deleteFiles && leagueFoldersToDelete.Any() ? $", {leagueFoldersToDelete.Count} folder(s) deleted" : "";
    logger.LogInformation("[LEAGUES] Successfully deleted league: {Name} and {EventCount} events{FilesMsg}{FoldersMsg}",
        league.Name, events.Count, filesDeletedMsg, foldersDeletedMsg);
    return Results.Ok(new { success = true, message = $"League deleted successfully ({events.Count} events removed{filesDeletedMsg}{foldersDeletedMsg})" });
});

// API: Refresh events for a league from TheSportsDB
app.MapPost("/api/leagues/{id:int}/refresh-events", async (
    int id,
    SportarrDbContext db,
    Sportarr.Api.Services.LeagueEventSyncService syncService,
    ILogger<Program> logger,
    HttpContext context) =>
{
    logger.LogInformation("[LEAGUES] POST /api/leagues/{Id}/refresh-events - Refreshing events from TheSportsDB", id);

    try
    {
        // Parse request body for optional seasons
        List<string>? seasons = null;
        if (context.Request.ContentLength > 0)
        {
            var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<RefreshEventsRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            seasons = request?.Seasons;
        }

        // Sync events
        var result = await syncService.SyncLeagueEventsAsync(id, seasons);

        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Message });
        }

        logger.LogInformation("[LEAGUES] Successfully synced events: {Message}", result.Message);

        return Results.Ok(new
        {
            success = true,
            message = result.Message,
            newEvents = result.NewCount,
            updatedEvents = result.UpdatedCount,
            skippedEvents = result.SkippedCount,
            failedEvents = result.FailedCount,
            totalEvents = result.TotalCount
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[LEAGUES] Error refreshing events for league {Id}: {Message}", id, ex.Message);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Error refreshing events"
        );
    }
});

// ====================================================================================
// TEAMS API - Universal Sports Support
// ====================================================================================

// API: Get all teams
app.MapGet("/api/teams", async (SportarrDbContext db, int? leagueId, string? sport) =>
{
    var query = db.Teams
        .Include(t => t.League)
        .AsQueryable();

    // Filter by league if provided
    if (leagueId.HasValue)
    {
        query = query.Where(t => t.LeagueId == leagueId.Value);
    }

    // Filter by sport if provided
    if (!string.IsNullOrEmpty(sport))
    {
        query = query.Where(t => t.Sport == sport);
    }

    var teams = await query
        .OrderBy(t => t.Sport)
        .ThenBy(t => t.Name)
        .ToListAsync();

    return Results.Ok(teams);
});

// API: Get team by ID
app.MapGet("/api/teams/{id:int}", async (int id, SportarrDbContext db) =>
{
    var team = await db.Teams
        .Include(t => t.League)
        .FirstOrDefaultAsync(t => t.Id == id);

    if (team == null)
    {
        return Results.NotFound(new { error = "Team not found" });
    }

    // Get event count and stats
    var homeEvents = await db.Events.Where(e => e.HomeTeamId == id).CountAsync();
    var awayEvents = await db.Events.Where(e => e.AwayTeamId == id).CountAsync();

    return Results.Ok(new
    {
        team.Id,
        team.ExternalId,
        team.Name,
        team.ShortName,
        team.AlternateName,
        team.LeagueId,
        League = team.League != null ? new { team.League.Name, team.League.Sport } : null,
        team.Sport,
        team.Country,
        team.Stadium,
        team.StadiumLocation,
        team.StadiumCapacity,
        team.Description,
        team.BadgeUrl,
        team.JerseyUrl,
        team.BannerUrl,
        team.Website,
        team.FormedYear,
        team.PrimaryColor,
        team.SecondaryColor,
        team.Added,
        team.LastUpdate,
        // Stats
        HomeEventCount = homeEvents,
        AwayEventCount = awayEvents,
        TotalEventCount = homeEvents + awayEvents
    });
});

// API: Search teams from TheSportsDB
app.MapGet("/api/teams/search/{query}", async (string query, Sportarr.Api.Services.TheSportsDBClient sportsDbClient, ILogger<Program> logger) =>
{
    logger.LogInformation("[TEAMS SEARCH] Searching for: {Query}", query);

    var results = await sportsDbClient.SearchTeamAsync(query);

    if (results == null || !results.Any())
    {
        logger.LogWarning("[TEAMS SEARCH] No results found for: {Query}", query);
        return Results.Ok(new List<object>());
    }

    logger.LogInformation("[TEAMS SEARCH] Found {Count} results", results.Count);
    return Results.Ok(results);
});

// ========================================
// EVENT SEARCH ENDPOINTS (TheSportsDB)
// ========================================

// GET /api/events/tv-schedule?date=2024-01-15&sport=Soccer
// Get TV schedule for events on a specific date and sport
app.MapGet("/api/events/tv-schedule", async (
    string? date,
    string? sport,
    Sportarr.Api.Services.TheSportsDBClient sportsDbClient,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[EVENTS TV-SCHEDULE] GET /api/events/tv-schedule?date={Date}&sport={Sport}", date, sport);

    if (string.IsNullOrEmpty(date))
    {
        return Results.BadRequest("Date parameter is required (format: YYYY-MM-DD)");
    }

    try
    {
        List<TVSchedule>? results;

        if (!string.IsNullOrEmpty(sport))
        {
            // Get TV schedule for specific sport on specific date
            results = await sportsDbClient.GetTVScheduleBySportDateAsync(sport, date);
        }
        else
        {
            // Get TV schedule for all sports on specific date
            results = await sportsDbClient.GetTVScheduleByDateAsync(date);
        }

        logger.LogInformation("[EVENTS TV-SCHEDULE] Found {Count} events", results?.Count ?? 0);
        return Results.Ok(results ?? new List<TVSchedule>());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EVENTS TV-SCHEDULE] Error fetching TV schedule");
        return Results.Problem("Failed to fetch TV schedule from TheSportsDB");
    }
});

// GET /api/events/livescore?sport=Soccer
// Get live and recent events for a sport
app.MapGet("/api/events/livescore", async (
    string sport,
    Sportarr.Api.Services.TheSportsDBClient sportsDbClient,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[EVENTS LIVESCORE] GET /api/events/livescore?sport={Sport}", sport);

    if (string.IsNullOrEmpty(sport))
    {
        return Results.BadRequest("Sport parameter is required");
    }

    try
    {
        var results = await sportsDbClient.GetLivescoreBySportAsync(sport);
        logger.LogInformation("[EVENTS LIVESCORE] Found {Count} events", results?.Count ?? 0);
        return Results.Ok(results ?? new List<Event>());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[EVENTS LIVESCORE] Error fetching livescore");
        return Results.Problem("Failed to fetch livescore from TheSportsDB");
    }
});

// API: Manual search for fight card
app.MapPost("/api/release/grab", async (
    HttpContext context,
    SportarrDbContext db,
    Sportarr.Api.Services.DownloadClientService downloadClientService,
    ConfigService configService,
    ILogger<Program> logger) =>
{
    // Parse the request body which contains both release and eventId
    var requestBody = await context.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
    if (requestBody == null)
    {
        logger.LogWarning("[GRAB] Invalid request body");
        return Results.BadRequest(new { success = false, message = "Invalid request body" });
    }

    // Extract eventId
    if (!requestBody.TryGetValue("eventId", out var eventIdElement) || !eventIdElement.TryGetInt32(out var eventId))
    {
        logger.LogWarning("[GRAB] Missing or invalid eventId");
        return Results.BadRequest(new { success = false, message = "Event ID is required" });
    }

    // Remove eventId from the dictionary before deserializing as ReleaseSearchResult
    requestBody.Remove("eventId");

    // Deserialize the release object from the remaining properties
    var releaseJson = System.Text.Json.JsonSerializer.Serialize(requestBody);
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var release = System.Text.Json.JsonSerializer.Deserialize<ReleaseSearchResult>(releaseJson, options);
    if (release == null)
    {
        logger.LogWarning("[GRAB] Invalid release data");
        return Results.BadRequest(new { success = false, message = "Invalid release data" });
    }

    logger.LogInformation("[GRAB] Manual grab requested for event {EventId}: {Title}", eventId, release.Title);

    var evt = await db.Events.FindAsync(eventId);
    if (evt == null)
    {
        logger.LogWarning("[GRAB] Event {EventId} not found", eventId);
        return Results.NotFound(new { success = false, message = "Event not found" });
    }

    // Get enabled download client matching the release protocol
    // Torrent releases need torrent clients (qBittorrent, Transmission, etc.)
    // Usenet releases need usenet clients (SABnzbd, NZBGet, etc.)
    var torrentClients = new[] { DownloadClientType.QBittorrent, DownloadClientType.Transmission,
                                 DownloadClientType.Deluge, DownloadClientType.RTorrent,
                                 DownloadClientType.UTorrent };
    var usenetClients = new[] { DownloadClientType.Sabnzbd, DownloadClientType.NzbGet };

    var downloadClient = await db.DownloadClients
        .Where(dc => dc.Enabled)
        .Where(dc => release.Protocol == "Torrent" ? torrentClients.Contains(dc.Type) : usenetClients.Contains(dc.Type))
        .OrderBy(dc => dc.Priority)
        .FirstOrDefaultAsync();

    if (downloadClient == null)
    {
        logger.LogWarning("[GRAB] No enabled {Protocol} download client configured", release.Protocol);
        return Results.BadRequest(new { success = false, message = $"No {release.Protocol} download client configured. This release requires a {(release.Protocol == "Torrent" ? "torrent" : "usenet")} client." });
    }

    logger.LogInformation("[GRAB] Using download client: {ClientName} ({ClientType})",
        downloadClient.Name, downloadClient.Type);

    // NOTE: We do NOT specify download path - download client uses its own configured directory
    // The category is used to track Sportarr downloads and create subdirectories
    // This matches Sonarr/Radarr behavior
    logger.LogInformation("[GRAB] Category: {Category}", downloadClient.Category);
    logger.LogInformation("[GRAB] ========== STARTING DOWNLOAD GRAB ==========");
    logger.LogInformation("[GRAB] Release Title: {Title}", release.Title);
    logger.LogInformation("[GRAB] Release Quality: {Quality}", release.Quality);
    logger.LogInformation("[GRAB] Release Size: {Size} bytes", release.Size);
    logger.LogInformation("[GRAB] Release Indexer: {Indexer}", release.Indexer);
    logger.LogInformation("[GRAB] Download URL: {Url}", release.DownloadUrl);
    logger.LogInformation("[GRAB] Download URL Type: {UrlType}",
        release.DownloadUrl.StartsWith("magnet:") ? "Magnet Link" :
        release.DownloadUrl.EndsWith(".torrent") ? "Torrent File URL" :
        "Unknown/Other");

    // Add download to client (category only, no path)
    AddDownloadResult downloadResult;
    try
    {
        logger.LogInformation("[GRAB] Calling DownloadClientService.AddDownloadWithResultAsync...");
        downloadResult = await downloadClientService.AddDownloadWithResultAsync(
            downloadClient,
            release.DownloadUrl,
            downloadClient.Category,
            release.Title  // Pass release title for better matching
        );
        logger.LogInformation("[GRAB] AddDownloadWithResultAsync returned: Success={Success}, DownloadId={DownloadId}",
            downloadResult.Success, downloadResult.DownloadId ?? "null");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[GRAB] ========== EXCEPTION DURING DOWNLOAD GRAB ==========");
        logger.LogError(ex, "[GRAB] Exception: {Message}", ex.Message);
        return Results.BadRequest(new
        {
            success = false,
            message = $"Failed to add download to {downloadClient.Name}: {ex.Message}"
        });
    }

    if (!downloadResult.Success || downloadResult.DownloadId == null)
    {
        logger.LogError("[GRAB] ========== DOWNLOAD GRAB FAILED ==========");
        logger.LogError("[GRAB] Error: {ErrorMessage}", downloadResult.ErrorMessage);
        logger.LogError("[GRAB] Error Type: {ErrorType}", downloadResult.ErrorType);

        // Return a user-friendly error message based on the error type
        var userMessage = downloadResult.ErrorType switch
        {
            AddDownloadErrorType.LoginFailed => $"Failed to login to {downloadClient.Name}. Check username/password in Settings > Download Clients.",
            AddDownloadErrorType.InvalidTorrent => downloadResult.ErrorMessage ?? "The indexer returned invalid torrent data. The torrent link may have expired.",
            AddDownloadErrorType.TorrentRejected => downloadResult.ErrorMessage ?? $"{downloadClient.Name} rejected the torrent. Check download client logs.",
            AddDownloadErrorType.ConnectionFailed => $"Could not connect to {downloadClient.Name}. Check the host/port in Settings > Download Clients.",
            AddDownloadErrorType.Timeout => $"Request to {downloadClient.Name} timed out. The server may be overloaded or unreachable.",
            _ => downloadResult.ErrorMessage ?? $"Failed to add download to {downloadClient.Name}. Check System > Logs for details."
        };

        return Results.BadRequest(new
        {
            success = false,
            message = userMessage,
            errorType = downloadResult.ErrorType.ToString()
        });
    }

    var downloadId = downloadResult.DownloadId;

    logger.LogInformation("[GRAB] Download added to client successfully!");
    logger.LogInformation("[GRAB] Download ID (Hash): {DownloadId}", downloadId);

    // Track download in database
    logger.LogInformation("[GRAB] Creating download queue item in database...");
    var queueItem = new DownloadQueueItem
    {
        EventId = eventId,
        Title = release.Title,
        DownloadId = downloadId,
        DownloadClientId = downloadClient.Id,
        Status = DownloadStatus.Queued,
        Quality = release.Quality,
        Codec = release.Codec,
        Source = release.Source,
        Size = release.Size,
        Downloaded = 0,
        Progress = 0,
        Indexer = release.Indexer,
        Protocol = release.Protocol,
        TorrentInfoHash = release.TorrentInfoHash,
        RetryCount = 0,
        LastUpdate = DateTime.UtcNow,
        QualityScore = release.QualityScore,
        CustomFormatScore = release.CustomFormatScore
    };

    db.DownloadQueue.Add(queueItem);
    await db.SaveChangesAsync();

    // Immediately check download status (Sonarr/Radarr behavior)
    // This ensures the download appears in the Activity page with real-time status
    logger.LogInformation("[GRAB] Performing immediate status check...");
    try
    {
        // Give SABnzbd a moment to register the download in its queue
        // SABnzbd may need 1-2 seconds after AddNzbAsync returns before the download appears in queue API
        await Task.Delay(2000); // 2 second delay
        logger.LogDebug("[GRAB] Checking status after 2s delay...");

        var status = await downloadClientService.GetDownloadStatusAsync(downloadClient, downloadId);
        if (status != null)
        {
            queueItem.Status = status.Status switch
            {
                "downloading" => DownloadStatus.Downloading,
                "paused" => DownloadStatus.Paused,
                "completed" => DownloadStatus.Completed,
                "queued" or "waiting" => DownloadStatus.Queued,
                _ => DownloadStatus.Queued
            };
            queueItem.Progress = status.Progress;
            queueItem.Downloaded = status.Downloaded;
            queueItem.Size = status.Size > 0 ? status.Size : release.Size;
            queueItem.LastUpdate = DateTime.UtcNow;
            await db.SaveChangesAsync();
            logger.LogInformation("[GRAB] Initial status: {Status}, Progress: {Progress:F1}%",
                queueItem.Status, queueItem.Progress);
        }
        else
        {
            logger.LogDebug("[GRAB] Status not available yet (download still initializing)");
        }
    }
    catch (Exception ex)
    {
        // Don't fail the grab if status check fails
        logger.LogWarning(ex, "[GRAB] Failed to get initial status (download will be tracked by monitor)");
    }

    logger.LogInformation("[GRAB] Download queued in database:");
    logger.LogInformation("[GRAB]   Queue ID: {QueueId}", queueItem.Id);
    logger.LogInformation("[GRAB]   Event ID: {EventId}", queueItem.EventId);
    logger.LogInformation("[GRAB]   Download ID: {DownloadId}", queueItem.DownloadId);
    logger.LogInformation("[GRAB]   Status: {Status}", queueItem.Status);
    logger.LogInformation("[GRAB] ========== DOWNLOAD GRAB COMPLETE ==========");
    logger.LogInformation("[GRAB] The download monitor service will track this download and update its status");

    return Results.Ok(new
    {
        success = true,
        message = "Download started successfully",
        downloadId = downloadId,
        queueId = queueItem.Id
    });
});

// API: Automatic search and download for event
app.MapPost("/api/event/{eventId:int}/automatic-search", async (
    int eventId,
    HttpRequest request,
    int? qualityProfileId,
    Sportarr.Api.Services.TaskService taskService,
    Sportarr.Api.Services.ConfigService configService,
    SportarrDbContext db,
    ILogger<Program> logger) =>
{
    // Read optional request body for part parameter
    string? part = null;
    if (request.ContentLength > 0)
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        if (!string.IsNullOrEmpty(json))
        {
            var requestData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            if (requestData.TryGetProperty("part", out var partProp))
            {
                part = partProp.GetString();
            }
        }
    }

    // Get event details with league
    var evt = await db.Events
        .Include(e => e.League)
        .Include(e => e.Files)
        .FirstOrDefaultAsync(e => e.Id == eventId);
    if (evt == null)
    {
        return Results.NotFound(new { error = "Event not found" });
    }

    var eventTitle = evt.Title ?? $"Event {eventId}";

    // Check if multi-part episodes are enabled and if this is a Fighting sport
    var config = await configService.GetConfigAsync();
    var isFightingSport = new[] { "Fighting", "MMA", "UFC", "Boxing", "Kickboxing", "Wrestling" }
        .Any(s => evt.Sport?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);

    var taskIds = new List<int>();

    // If multi-part is enabled, Fighting sport, and no specific part requested,
    // automatically search for monitored parts
    if (config.EnableMultiPartEpisodes && isFightingSport && part == null)
    {
        // Get monitored parts from event (or fall back to league settings)
        // If null or empty, default to all parts
        var monitoredParts = evt.MonitoredParts ?? evt.League?.MonitoredParts;
        string[] fightCardParts;

        if (!string.IsNullOrEmpty(monitoredParts))
        {
            // Only search for monitored parts
            fightCardParts = monitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            logger.LogInformation("[AUTOMATIC SEARCH] Multi-part enabled for Fighting sport - queuing searches for monitored parts only: {Parts}",
                string.Join(", ", fightCardParts));
        }
        else
        {
            // Default: search all parts
            fightCardParts = new[] { "Early Prelims", "Prelims", "Main Card" };
            logger.LogInformation("[AUTOMATIC SEARCH] Multi-part enabled for Fighting sport - queuing searches for all parts: {EventTitle}", eventTitle);
        }

        foreach (var cardPart in fightCardParts)
        {
            var taskName = $"Search: {eventTitle} ({cardPart})";
            var taskBody = $"{eventId}|{cardPart}";

            var task = await taskService.QueueTaskAsync(
                name: taskName,
                commandName: "EventSearch",
                priority: 10,
                body: taskBody
            );

            taskIds.Add(task.Id);
            logger.LogInformation("[AUTOMATIC SEARCH] Queued search for {Part}: Task ID {TaskId}", cardPart, task.Id);
        }

        var partsMessage = string.Join(", ", fightCardParts);
        return Results.Ok(new {
            success = true,
            message = $"Queued {fightCardParts.Length} automatic searches ({partsMessage})",
            taskIds = taskIds
        });
    }
    else
    {
        // Single search (either non-Fighting sport or specific part requested)
        var taskName = part != null ? $"Search: {eventTitle} ({part})" : $"Search: {eventTitle}";
        var taskBody = part != null ? $"{eventId}|{part}" : eventId.ToString();

        logger.LogInformation("[AUTOMATIC SEARCH] Queuing search for event {EventId}{Part}",
            eventId, part != null ? $" (Part: {part})" : "");

        var task = await taskService.QueueTaskAsync(
            name: taskName,
            commandName: "EventSearch",
            priority: 10,
            body: taskBody
        );

        return Results.Ok(new {
            success = true,
            message = "Search queued",
            taskId = task.Id
        });
    }
});

// API: Get search queue status
app.MapGet("/api/search/queue", (Sportarr.Api.Services.SearchQueueService searchQueueService) =>
{
    var status = searchQueueService.GetQueueStatus();
    return Results.Ok(status);
});

// API: Queue a search for an event (uses new parallel queue system)
app.MapPost("/api/search/queue", async (
    HttpRequest request,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();

    if (string.IsNullOrEmpty(json))
    {
        return Results.BadRequest(new { error = "Request body required" });
    }

    var requestData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

    if (!requestData.TryGetProperty("eventId", out var eventIdProp) || !eventIdProp.TryGetInt32(out int eventId))
    {
        return Results.BadRequest(new { error = "eventId is required" });
    }

    string? part = null;
    if (requestData.TryGetProperty("part", out var partProp))
    {
        part = partProp.GetString();
    }

    logger.LogInformation("[SEARCH QUEUE API] Queueing search for event {EventId}{Part}",
        eventId, part != null ? $" ({part})" : "");

    var queueItem = await searchQueueService.QueueSearchAsync(eventId, part);
    return Results.Ok(queueItem);
});

// API: Queue searches for all events in a league
app.MapPost("/api/search/queue/league/{leagueId:int}", async (
    int leagueId,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[SEARCH QUEUE API] Queueing search for all events in league {LeagueId}", leagueId);

    var queuedItems = await searchQueueService.QueueLeagueSearchAsync(leagueId);
    return Results.Ok(new {
        success = true,
        message = $"Queued {queuedItems.Count} searches",
        count = queuedItems.Count,
        items = queuedItems
    });
});

// API: Get status of a specific queued search
app.MapGet("/api/search/queue/{queueId}", (
    string queueId,
    Sportarr.Api.Services.SearchQueueService searchQueueService) =>
{
    var item = searchQueueService.GetSearchStatus(queueId);
    if (item == null)
    {
        return Results.NotFound(new { error = "Search not found in queue" });
    }
    return Results.Ok(item);
});

// API: Cancel a pending search
app.MapDelete("/api/search/queue/{queueId}", (
    string queueId,
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[SEARCH QUEUE API] Cancelling search {QueueId}", queueId);

    var cancelled = searchQueueService.CancelSearch(queueId);
    if (cancelled)
    {
        return Results.Ok(new { success = true, message = "Search cancelled" });
    }
    return Results.NotFound(new { error = "Search not found or already executing" });
});

// API: Clear all pending searches
app.MapDelete("/api/search/queue", (
    Sportarr.Api.Services.SearchQueueService searchQueueService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[SEARCH QUEUE API] Clearing all pending searches");

    var count = searchQueueService.ClearPendingSearches();
    return Results.Ok(new { success = true, message = $"Cleared {count} pending searches", count });
});

// API: Search all monitored events
app.MapPost("/api/automatic-search/all", async (
    Sportarr.Api.Services.AutomaticSearchService automaticSearchService) =>
{
    var results = await automaticSearchService.SearchAllMonitoredEventsAsync();
    return Results.Ok(results);
});

// API: Search all monitored events in a specific league (Sonarr-style league/show-level search)
app.MapPost("/api/league/{leagueId:int}/automatic-search", async (
    int leagueId,
    SportarrDbContext db,
    Sportarr.Api.Services.AutomaticSearchService automaticSearchService,
    Sportarr.Api.Services.TaskService taskService,
    Sportarr.Api.Services.ConfigService configService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTOMATIC SEARCH] POST /api/league/{LeagueId}/automatic-search - Searching all monitored events in league", leagueId);

    var league = await db.Leagues.FindAsync(leagueId);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all monitored events in this league (searches for missing files and upgrades)
    var events = await db.Events
        .Where(e => e.LeagueId == leagueId && e.Monitored)
        .ToListAsync();

    if (!events.Any())
    {
        return Results.Ok(new
        {
            success = true,
            message = $"No monitored events found in {league.Name}",
            eventsSearched = 0
        });
    }

    logger.LogInformation("[AUTOMATIC SEARCH] Found {Count} monitored events in league: {League}", events.Count, league.Name);

    // Check if multi-part episodes are enabled
    var config = await configService.GetConfigAsync();
    var fightCardParts = new[] { "Early Prelims", "Prelims", "Main Card" };

    // Queue search tasks for all events
    var taskIds = new List<int>();
    int totalSearches = 0;

    foreach (var evt in events)
    {
        // Check if this is a Fighting sport that should use multi-part
        var isFightingSport = new[] { "Fighting", "MMA", "UFC", "Boxing", "Kickboxing", "Wrestling" }
            .Any(s => evt.Sport?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);

        if (config.EnableMultiPartEpisodes && isFightingSport)
        {
            // Queue searches for all parts
            logger.LogInformation("[AUTOMATIC SEARCH] Queuing multi-part searches for Fighting sport event: {EventTitle}", evt.Title);
            foreach (var part in fightCardParts)
            {
                var task = await taskService.QueueTaskAsync(
                    name: $"Search: {evt.Title} ({part})",
                    commandName: "EventSearch",
                    priority: 10,
                    body: $"{evt.Id}|{part}"
                );
                taskIds.Add(task.Id);
                totalSearches++;
            }
        }
        else
        {
            // Single search for non-Fighting sports
            var task = await taskService.QueueTaskAsync(
                name: $"Search: {evt.Title}",
                commandName: "EventSearch",
                priority: 10,
                body: evt.Id.ToString()
            );
            taskIds.Add(task.Id);
            totalSearches++;
        }
    }

    return Results.Ok(new
    {
        success = true,
        message = $"Queued {totalSearches} automatic searches for {league.Name}",
        eventsSearched = events.Count,
        taskIds = taskIds
    });
});

// API: Search all monitored events in a specific season
app.MapPost("/api/leagues/{leagueId:int}/seasons/{season}/automatic-search", async (
    int leagueId,
    string season,
    SportarrDbContext db,
    Sportarr.Api.Services.TaskService taskService,
    Sportarr.Api.Services.ConfigService configService,
    ILogger<Program> logger) =>
{
    logger.LogInformation("[AUTOMATIC SEARCH] POST /api/leagues/{LeagueId}/seasons/{Season}/automatic-search - Searching all monitored events in season", leagueId, season);

    var league = await db.Leagues.FindAsync(leagueId);
    if (league == null)
    {
        return Results.NotFound(new { error = "League not found" });
    }

    // Get all monitored events in this season
    var events = await db.Events
        .Where(e => e.LeagueId == leagueId && e.Season == season && e.Monitored)
        .ToListAsync();

    if (!events.Any())
    {
        return Results.Ok(new
        {
            success = true,
            message = $"No monitored events found in season {season}",
            eventsSearched = 0
        });
    }

    logger.LogInformation("[AUTOMATIC SEARCH] Found {Count} monitored events in season {Season}", events.Count, season);

    // Check if multi-part episodes are enabled
    var config = await configService.GetConfigAsync();

    // Queue search tasks for all events
    var taskIds = new List<int>();
    int totalSearches = 0;

    foreach (var evt in events)
    {
        // Check if this is a Fighting sport that should use multi-part
        var isFightingSport = new[] { "Fighting", "MMA", "UFC", "Boxing", "Kickboxing", "Wrestling" }
            .Any(s => evt.Sport?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false);

        if (config.EnableMultiPartEpisodes && isFightingSport)
        {
            // Get monitored parts from event (or fall back to league settings)
            var monitoredParts = evt.MonitoredParts ?? league?.MonitoredParts;
            string[] partsToSearch;

            if (!string.IsNullOrEmpty(monitoredParts))
            {
                // Only search for monitored parts
                partsToSearch = monitoredParts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                logger.LogInformation("[AUTOMATIC SEARCH] Queuing searches for monitored parts only: {Parts} for event {EventTitle}",
                    string.Join(", ", partsToSearch), evt.Title);
            }
            else
            {
                // Default: search all parts
                partsToSearch = new[] { "Early Prelims", "Prelims", "Main Card" };
                logger.LogInformation("[AUTOMATIC SEARCH] Queuing searches for all parts for event {EventTitle}", evt.Title);
            }

            foreach (var part in partsToSearch)
            {
                var task = await taskService.QueueTaskAsync(
                    name: $"Search: {evt.Title} ({part})",
                    commandName: "EventSearch",
                    priority: 10,
                    body: $"{evt.Id}|{part}"
                );
                taskIds.Add(task.Id);
                totalSearches++;
            }
        }
        else
        {
            // Single search for non-Fighting sports
            var task = await taskService.QueueTaskAsync(
                name: $"Search: {evt.Title}",
                commandName: "EventSearch",
                priority: 10,
                body: evt.Id.ToString()
            );
            taskIds.Add(task.Id);
            totalSearches++;
        }
    }

    return Results.Ok(new
    {
        success = true,
        message = $"Queued {totalSearches} automatic searches for season {season}",
        eventsSearched = events.Count,
        taskIds = taskIds
    });
});

// ========================================
// PROWLARR INTEGRATION - Sonarr/Radarr-Compatible Application API
// ========================================

// Prowlarr uses /api/v1/indexer to sync indexers to applications
// These endpoints allow Prowlarr to automatically push indexers to Sportarr

// GET /api/v1/indexer - List all indexers (Prowlarr uses this to check existing)
app.MapGet("/api/v1/indexer", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v1/indexer - Listing indexers for Prowlarr");
    var indexers = await db.Indexers.OrderBy(i => i.Priority).ToListAsync();

    // Transform to Prowlarr-compatible format
    var prowlarrIndexers = indexers.Select(i => new
    {
        id = i.Id,
        name = i.Name,
        implementation = i.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
        enable = i.Enabled,
        priority = i.Priority,
        fields = new object[]
        {
            new { name = "baseUrl", value = i.Url },
            new { name = "apiKey", value = i.ApiKey ?? "" },
            new { name = "categories", value = string.Join(",", i.Categories) }
        }
    }).ToList();

    return Results.Ok(prowlarrIndexers);
});

// POST /api/v1/indexer - Add new indexer (Prowlarr pushes indexers here)
app.MapPost("/api/v1/indexer", async (
    HttpRequest request,
    SportarrDbContext db,
    ILogger<Program> logger) =>
{
    try
    {
        // Read raw JSON body
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[PROWLARR] POST /api/v1/indexer - Received: {Json}", json);

        // Parse Prowlarr payload
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Extract fields from Prowlarr format
        var name = prowlarrIndexer.GetProperty("name").GetString() ?? "Unknown";
        var implementation = prowlarrIndexer.GetProperty("implementation").GetString() ?? "Torznab";
        var enabled = prowlarrIndexer.TryGetProperty("enable", out var enableProp) ? enableProp.GetBoolean() : true;
        var priority = prowlarrIndexer.TryGetProperty("priority", out var priorityProp) ? priorityProp.GetInt32() : 50;

        // Extract fields array
        var fields = prowlarrIndexer.GetProperty("fields");
        string? baseUrl = null;
        string? apiKey = null;
        string? categories = null;

        foreach (var field in fields.EnumerateArray())
        {
            var fieldName = field.GetProperty("name").GetString();
            var fieldValue = field.TryGetProperty("value", out var val) ? val.GetString() : null;

            if (fieldName == "baseUrl") baseUrl = fieldValue;
            else if (fieldName == "apiKey" || fieldName == "apikey") apiKey = fieldValue;
            else if (fieldName == "categories") categories = fieldValue;
        }

        // Check if indexer with same URL already exists
        var existing = await db.Indexers.FirstOrDefaultAsync(i => i.Url == baseUrl);
        if (existing != null)
        {
            // Update existing
            logger.LogInformation("[PROWLARR] Updating existing indexer: {Name}", name);
            existing.Name = name;
            existing.Type = implementation.ToLower().Contains("newznab") ? IndexerType.Newznab : IndexerType.Torznab;
            existing.ApiKey = apiKey;
            existing.Categories = !string.IsNullOrWhiteSpace(categories)
                ? categories.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>();
            existing.Enabled = enabled;
            existing.Priority = priority;
            existing.LastModified = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(new { id = existing.Id });
        }

        // Create new indexer
        var indexer = new Indexer
        {
            Name = name,
            Type = implementation.ToLower().Contains("newznab") ? IndexerType.Newznab : IndexerType.Torznab,
            Url = baseUrl ?? "",
            ApiKey = apiKey,
            Categories = !string.IsNullOrWhiteSpace(categories)
                ? categories.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>(),
            Enabled = enabled,
            Priority = priority,
            MinimumSeeders = 1,
            Created = DateTime.UtcNow
        };

        db.Indexers.Add(indexer);
        await db.SaveChangesAsync();

        logger.LogInformation("[PROWLARR] Created new indexer: {Name} (ID: {Id})", name, indexer.Id);
        return Results.Created($"/api/v1/indexer/{indexer.Id}", new { id = indexer.Id });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error adding indexer: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// PUT /api/v1/indexer/{id} - Update indexer
app.MapPut("/api/v1/indexer/{id:int}", async (
    int id,
    HttpRequest request,
    SportarrDbContext db,
    ILogger<Program> logger) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync();
        logger.LogInformation("[PROWLARR] PUT /api/v1/indexer/{Id} - Received: {Json}", id, json);

        var indexer = await db.Indexers.FindAsync(id);
        if (indexer is null) return Results.NotFound();

        // Parse and update
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        indexer.Name = prowlarrIndexer.GetProperty("name").GetString() ?? indexer.Name;
        indexer.Enabled = prowlarrIndexer.TryGetProperty("enable", out var enableProp) ? enableProp.GetBoolean() : indexer.Enabled;
        indexer.Priority = prowlarrIndexer.TryGetProperty("priority", out var priorityProp) ? priorityProp.GetInt32() : indexer.Priority;
        indexer.LastModified = DateTime.UtcNow;

        await db.SaveChangesAsync();
        logger.LogInformation("[PROWLARR] Updated indexer: {Name} (ID: {Id})", indexer.Name, id);

        return Results.Ok(new { id = indexer.Id });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error updating indexer: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
});

// DELETE /api/v1/indexer/{id} - Delete indexer
app.MapDelete("/api/v1/indexer/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] DELETE /api/v1/indexer/{Id}", id);
    var indexer = await db.Indexers.FindAsync(id);
    if (indexer is null) return Results.NotFound();

    db.Indexers.Remove(indexer);
    await db.SaveChangesAsync();
    logger.LogInformation("[PROWLARR] Deleted indexer: {Name} (ID: {Id})", indexer.Name, id);

    return Results.Ok();
});

// GET /api/v1/system/status - System info (Prowlarr uses this for connection test)
app.MapGet("/api/v1/system/status", (HttpContext context, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v1/system/status - Connection test from Prowlarr");

    // Log all headers for debugging
    logger.LogInformation("[PROWLARR AUTH] Headers: {Headers}",
        string.Join(", ", context.Request.Headers.Select(h => $"{h.Key}={h.Value}")));

    // Check if API key was provided
    var hasApiKey = context.Request.Headers.ContainsKey("X-Api-Key") ||
                    context.Request.Query.ContainsKey("apikey") ||
                    context.Request.Headers.ContainsKey("Authorization");
    logger.LogInformation("[PROWLARR AUTH] Has API Key: {HasApiKey}", hasApiKey);
    logger.LogInformation("[PROWLARR AUTH] User authenticated: {IsAuthenticated}, User: {User}",
        context.User?.Identity?.IsAuthenticated, context.User?.Identity?.Name);

    return Results.Ok(new
    {
        appName = "Sportarr",
        instanceName = "Sportarr",
        version = Sportarr.Api.Version.AppVersion,
        buildTime = DateTime.UtcNow,
        isDebug = false,
        isProduction = true,
        isAdmin = false,
        isUserInteractive = false,
        startupPath = Directory.GetCurrentDirectory(),
        appData = dataPath,
        osName = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        osVersion = Environment.OSVersion.VersionString,
        isNetCore = true,
        isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux),
        isOsx = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX),
        isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows),
        isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
        mode = "console",
        branch = "main",
        authentication = "forms",
        sqliteVersion = "3.0",
        urlBase = "",
        runtimeVersion = Environment.Version.ToString(),
        runtimeName = ".NET",
        startTime = DateTime.UtcNow
    });
});

// GET /api/v3/system/status - System status (Radarr v3 API for Prowlarr)
app.MapGet("/api/v3/system/status", (HttpContext context, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/system/status - Prowlarr requesting system status (v3 API)");

    // Return same data as v1 endpoint
    return Results.Ok(new
    {
        appName = "Sportarr",
        instanceName = "Sportarr",
        version = Sportarr.Api.Version.AppVersion,
        buildTime = DateTime.UtcNow,
        isDebug = false,
        isProduction = true,
        isAdmin = false,
        isUserInteractive = false,
        startupPath = Directory.GetCurrentDirectory(),
        appData = dataPath,
        osName = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        osVersion = Environment.OSVersion.VersionString,
        isNetCore = true,
        isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux),
        isOsx = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX),
        isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows),
        isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
        mode = "console",
        branch = "main",
        authentication = "forms",
        sqliteVersion = "3.0",
        urlBase = "",
        runtimeVersion = Environment.Version.ToString(),
        runtimeName = ".NET",
        startTime = DateTime.UtcNow
    });
});

// ============================================================================
// DECYPHARR COMPATIBILITY ENDPOINTS
// These endpoints enable Decypharr (debrid download client) to work with Sportarr
// Decypharr uses username/password as Sportarr URL/API key for callback
// ============================================================================

// GET /api/v3/health - Health check endpoint for Decypharr validation
// Decypharr calls this to validate the connection to Sportarr
app.MapGet("/api/v3/health", (HttpContext context, ILogger<Program> logger) =>
{
    logger.LogInformation("[DECYPHARR] GET /api/v3/health - Health check requested");

    // Return empty array (healthy) - Decypharr only checks for 200/404 status
    return Results.Ok(Array.Empty<object>());
});

// GET /api/v3/manualimport - Get files ready for manual import
// Decypharr calls this after a download completes to get files to import
app.MapGet("/api/v3/manualimport", (
    HttpContext context,
    SportarrDbContext db,
    ILogger<Program> logger,
    string? folder,
    string? downloadId,
    int? seriesId,
    int? seasonNumber,
    bool? filterExistingFiles) =>
{
    logger.LogInformation("[DECYPHARR] GET /api/v3/manualimport - folder={Folder}, downloadId={DownloadId}, seriesId={SeriesId}",
        folder, downloadId, seriesId);

    // If no folder specified, return empty array
    if (string.IsNullOrEmpty(folder))
    {
        return Results.Ok(Array.Empty<object>());
    }

    // Look for files in the specified folder
    var importFiles = new List<object>();

    try
    {
        if (Directory.Exists(folder))
        {
            var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => new[] { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v" }
                    .Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            logger.LogInformation("[DECYPHARR] Found {Count} video files in {Folder}", files.Count, folder);

            int id = 1;
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var fileName = Path.GetFileNameWithoutExtension(file);

                // Try to parse the file name to extract event info
                // Format: "League - S2024E01 - Event Title - Quality.ext"
                var eventMatch = System.Text.RegularExpressions.Regex.Match(fileName,
                    @"(.+?)\s*-\s*S(\d{4})E(\d+)\s*-\s*(.+?)(?:\s*-\s*(\d+p|\w+))?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                string leagueName = eventMatch.Success ? eventMatch.Groups[1].Value.Trim() : "Unknown League";
                int season = eventMatch.Success && int.TryParse(eventMatch.Groups[2].Value, out var s) ? s : DateTime.Now.Year;
                int episode = eventMatch.Success && int.TryParse(eventMatch.Groups[3].Value, out var e) ? e : 1;
                string eventTitle = eventMatch.Success ? eventMatch.Groups[4].Value.Trim() : fileName;

                importFiles.Add(new
                {
                    id = id++,
                    path = file,
                    relativePath = Path.GetRelativePath(folder, file),
                    folderName = Path.GetFileName(folder),
                    name = fileName,
                    size = fileInfo.Length,
                    series = new
                    {
                        id = seriesId ?? 1,
                        title = leagueName,
                        sortTitle = leagueName.ToLowerInvariant(),
                        status = "continuing",
                        overview = "",
                        network = "",
                        images = Array.Empty<object>(),
                        seasons = new[] { new { seasonNumber = season, monitored = true } },
                        year = season,
                        path = folder,
                        qualityProfileId = 1,
                        languageProfileId = 1,
                        seasonFolder = true,
                        monitored = true,
                        useSceneNumbering = false,
                        runtime = 0,
                        tvdbId = 0,
                        tvRageId = 0,
                        tvMazeId = 0,
                        firstAired = $"{season}-01-01",
                        seriesType = "standard",
                        cleanTitle = leagueName.ToLowerInvariant().Replace(" ", ""),
                        titleSlug = leagueName.ToLowerInvariant().Replace(" ", "-"),
                        genres = new[] { "Sports" },
                        tags = Array.Empty<int>(),
                        added = DateTime.UtcNow.ToString("o"),
                        ratings = new { votes = 0, value = 0.0 }
                    },
                    seasonNumber = season,
                    episodes = new[]
                    {
                        new
                        {
                            id = id,
                            seriesId = seriesId ?? 1,
                            episodeFileId = 0,
                            seasonNumber = season,
                            episodeNumber = episode,
                            title = eventTitle,
                            airDate = DateTime.Now.ToString("yyyy-MM-dd"),
                            airDateUtc = DateTime.UtcNow.ToString("o"),
                            overview = "",
                            hasFile = false,
                            monitored = true,
                            absoluteEpisodeNumber = episode,
                            unverifiedSceneNumbering = false
                        }
                    },
                    quality = new
                    {
                        quality = new { id = 7, name = "WEBDL-1080p", source = "web", resolution = 1080 },
                        revision = new { version = 1, real = 0, isRepack = false }
                    },
                    languages = new[] { new { id = 1, name = "English" } },
                    releaseGroup = "",
                    customFormats = Array.Empty<object>(),
                    customFormatScore = 0,
                    indexerFlags = 0,
                    rejections = Array.Empty<object>()
                });
            }
        }
        else
        {
            logger.LogWarning("[DECYPHARR] Folder does not exist: {Folder}", folder);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[DECYPHARR] Error scanning folder: {Folder}", folder);
    }

    logger.LogInformation("[DECYPHARR] Returning {Count} files for manual import", importFiles.Count);
    return Results.Ok(importFiles);
});

// POST /api/v3/command - Execute commands (used by Decypharr for ManualImport)
app.MapPost("/api/v3/command", async (HttpContext context, SportarrDbContext db, FileImportService fileImportService, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[DECYPHARR] POST /api/v3/command - {Json}", json);

    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var commandName = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : "";

        if (commandName?.Equals("ManualImport", StringComparison.OrdinalIgnoreCase) == true)
        {
            logger.LogInformation("[DECYPHARR] Processing ManualImport command");

            if (root.TryGetProperty("files", out var filesElement) && filesElement.ValueKind == JsonValueKind.Array)
            {
                var importedCount = 0;

                foreach (var fileElement in filesElement.EnumerateArray())
                {
                    var path = fileElement.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;

                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        logger.LogInformation("[DECYPHARR] Would import file: {Path}", path);
                        // TODO: Trigger actual file import using FileImportService
                        // For now, just log the import request
                        importedCount++;
                    }
                }

                logger.LogInformation("[DECYPHARR] ManualImport processed {Count} files", importedCount);
            }

            // Return command response
            return Results.Ok(new
            {
                id = new Random().Next(1, 10000),
                name = "ManualImport",
                commandName = "ManualImport",
                message = "Completed",
                body = new { },
                priority = "normal",
                status = "completed",
                queued = DateTime.UtcNow.ToString("o"),
                started = DateTime.UtcNow.ToString("o"),
                ended = DateTime.UtcNow.ToString("o"),
                duration = "00:00:00.0000000",
                trigger = "manual",
                stateChangeTime = DateTime.UtcNow.ToString("o"),
                sendUpdatesToClient = true,
                updateScheduledTask = false
            });
        }
        else
        {
            logger.LogInformation("[DECYPHARR] Unknown command: {Command}", commandName);
            return Results.Ok(new
            {
                id = new Random().Next(1, 10000),
                name = commandName,
                status = "completed"
            });
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[DECYPHARR] Error processing command");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// GET /api/v3/series - Get series list (used by Decypharr to identify content)
app.MapGet("/api/v3/series", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[DECYPHARR] GET /api/v3/series - Listing series/leagues");

    // Get distinct league names from events
    var leagueNames = await db.Events
        .Where(e => e.League != null)
        .Select(e => e.League!.Name)
        .Distinct()
        .ToListAsync();

    var series = leagueNames.Select((leagueName, index) => new
    {
        id = index + 1,
        title = leagueName ?? "",
        sortTitle = (leagueName ?? "").ToLowerInvariant(),
        status = "continuing",
        overview = $"Sports events from {leagueName}",
        network = "",
        images = Array.Empty<object>(),
        seasons = new[] { new { seasonNumber = DateTime.Now.Year, monitored = true } },
        year = DateTime.Now.Year,
        path = $"/sports/{(leagueName ?? "").ToLowerInvariant().Replace(" ", "-")}",
        qualityProfileId = 1,
        languageProfileId = 1,
        seasonFolder = true,
        monitored = true,
        useSceneNumbering = false,
        runtime = 0,
        tvdbId = 0,
        tvRageId = 0,
        tvMazeId = 0,
        seriesType = "standard",
        cleanTitle = (leagueName ?? "").ToLowerInvariant().Replace(" ", ""),
        titleSlug = (leagueName ?? "").ToLowerInvariant().Replace(" ", "-"),
        genres = new[] { "Sports" },
        tags = Array.Empty<int>(),
        added = DateTime.UtcNow.ToString("o"),
        ratings = new { votes = 0, value = 0.0 }
    }).ToList();

    logger.LogInformation("[DECYPHARR] Returning {SeriesCount} series", series.Count);
    return Results.Ok(series);
});

// ============================================================================
// END DECYPHARR COMPATIBILITY ENDPOINTS
// ============================================================================

// POST /api/v3/indexer/test - Test indexer connection (Radarr v3 API for Prowlarr)
app.MapPost("/api/v3/indexer/test", async (HttpRequest request, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] POST /api/v3/indexer/test - Prowlarr testing indexer");

    // Read the test indexer payload from Prowlarr
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[PROWLARR] Test indexer payload: {Json}", json);

    // For now, just return success - Prowlarr is testing if we can receive indexer configs
    // In a real implementation, we might test the indexer URL, but for connection testing this is enough
    return Results.Ok(new
    {
        id = 0,
        name = "Test",
        message = "Connection test successful"
    });
});

// GET /api/v3/indexer/schema - Indexer schema (Radarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer/schema", (ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/indexer/schema - Prowlarr requesting indexer schema");

    // Return Torznab/Newznab indexer schema matching Radarr/Sonarr format exactly
    return Results.Ok(new object[]
    {
        new
        {
            id = 0,
            enableRss = true,
            enableAutomaticSearch = true,
            enableInteractiveSearch = true,
            supportsRss = true,
            supportsSearch = true,
            protocol = "torrent",
            priority = 25,
            downloadClientId = 0,
            name = "",
            implementation = "Torznab",
            implementationName = "Torznab",
            configContract = "TorznabSettings",
            infoLink = "https://github.com/Prowlarr/Prowlarr",
            seedCriteria = new
            {
                seedRatio = 1.0,
                seedTime = 1,
                seasonPackSeedTime = 1
            },
            tags = new int[] { },
            presets = new object[] { },
            fields = new object[]
            {
                new
                {
                    order = 0,
                    name = "baseUrl",
                    label = "URL",
                    helpText = "Torznab feed URL",
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 1,
                    name = "apiPath",
                    label = "API Path",
                    helpText = "Path to the api, usually /api",
                    helpLink = (string?)null,
                    value = "/api",
                    type = "textbox",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 2,
                    name = "apiKey",
                    label = "API Key",
                    helpText = (string?)null,
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    privacy = "apiKey",
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 3,
                    name = "categories",
                    label = "Categories",
                    helpText = "Comma separated list of categories",
                    helpLink = (string?)null,
                    value = new int[] { 2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060 },
                    type = "select",
                    selectOptions = new object[] { },
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 4,
                    name = "minimumSeeders",
                    label = "Minimum Seeders",
                    helpText = "Minimum number of seeders required",
                    helpLink = (string?)null,
                    value = 1,
                    type = "number",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 5,
                    name = "seedCriteria.seedRatio",
                    label = "Seed Ratio",
                    helpText = "The ratio a torrent should reach before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = 1.0,
                    type = "number",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 6,
                    name = "seedCriteria.seedTime",
                    label = "Seed Time",
                    helpText = "The time a torrent should be seeded before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = 1,
                    type = "number",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 7,
                    name = "seedCriteria.seasonPackSeedTime",
                    label = "Season Pack Seed Time",
                    helpText = "The time a season pack torrent should be seeded before stopping, empty is download client's default",
                    helpLink = (string?)null,
                    value = (int?)null,
                    type = "number",
                    advanced = true,
                    hidden = false
                }
            }
        },
        new
        {
            id = 0,
            enableRss = true,
            enableAutomaticSearch = true,
            enableInteractiveSearch = true,
            supportsRss = true,
            supportsSearch = true,
            protocol = "usenet",
            priority = 25,
            downloadClientId = 0,
            name = "",
            implementation = "Newznab",
            implementationName = "Newznab",
            configContract = "NewznabSettings",
            infoLink = "https://github.com/Prowlarr/Prowlarr",
            tags = new int[] { },
            presets = new object[] { },
            fields = new object[]
            {
                new
                {
                    order = 0,
                    name = "baseUrl",
                    label = "URL",
                    helpText = "Newznab feed URL",
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 1,
                    name = "apiPath",
                    label = "API Path",
                    helpText = "Path to the api, usually /api",
                    helpLink = (string?)null,
                    value = "/api",
                    type = "textbox",
                    advanced = true,
                    hidden = false
                },
                new
                {
                    order = 2,
                    name = "apiKey",
                    label = "API Key",
                    helpText = (string?)null,
                    helpLink = (string?)null,
                    value = "",
                    type = "textbox",
                    privacy = "apiKey",
                    advanced = false,
                    hidden = false
                },
                new
                {
                    order = 3,
                    name = "categories",
                    label = "Categories",
                    helpText = "Comma separated list of categories",
                    helpLink = (string?)null,
                    value = new int[] { 2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060 },
                    type = "select",
                    selectOptions = new object[] { },
                    advanced = false,
                    hidden = false
                }
            }
        }
    });
});

// GET /api/v3/indexer - List all indexers (Radarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/indexer - Prowlarr requesting indexer list");

    var indexers = await db.Indexers.ToListAsync();

    // Convert our indexers to Radarr v3 format
    var radarrIndexers = indexers.Select(i =>
    {
        var isTorznab = i.Type == IndexerType.Torznab;
        var fields = new List<object>
        {
            new {  order = 0, name = "baseUrl", label = "URL", helpText = isTorznab ? "Torznab feed URL" : "Newznab feed URL", helpLink = (string?)null, value = i.Url, type = "textbox", advanced = false, hidden = false },
            new { order = 1, name = "apiPath", label = "API Path", helpText = "Path to the api, usually /api", helpLink = (string?)null, value = "/api", type = "textbox", advanced = true, hidden = false },
            new { order = 2, name = "apiKey", label = "API Key", helpText = (string?)null, helpLink = (string?)null, value = i.ApiKey ?? "", type = "textbox", privacy = "apiKey", advanced = false, hidden = false },
            new { order = 3, name = "categories", label = "Categories", helpText = "Comma separated list of categories", helpLink = (string?)null, value = i.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray(), type = "select", advanced = false, hidden = false },
            new { order = 4, name = "minimumSeeders", label = "Minimum Seeders", helpText = "Minimum number of seeders required", helpLink = (string?)null, value = i.MinimumSeeders, type = "number", advanced = false, hidden = false }
        };

        // Add optional fields if present (NOT seed criteria - those go in seedCriteria object)
        var fieldOrder = 5;
        if (i.EarlyReleaseLimit.HasValue)
            fields.Add(new { order = fieldOrder++, name = "earlyReleaseLimit", label = "Early Release Limit", helpText = (string?)null, helpLink = (string?)null, value = i.EarlyReleaseLimit.Value, type = "number", advanced = true, hidden = false });
        // Note: animeCategories is a Sonarr-only field, not used in Radarr API (Sportarr uses Radarr template only)

        return new
        {
            id = i.Id,
            name = i.Name,
            enableRss = i.EnableRss,
            enableAutomaticSearch = i.EnableAutomaticSearch,
            enableInteractiveSearch = i.EnableInteractiveSearch,
            priority = i.Priority,
            implementation = i.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            implementationName = i.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            configContract = i.Type == IndexerType.Torznab ? "TorznabSettings" : "NewznabSettings",
            infoLink = "https://github.com/Prowlarr/Prowlarr",
            protocol = i.Type == IndexerType.Torznab ? "torrent" : "usenet",
            supportsRss = i.EnableRss,
            supportsSearch = i.EnableAutomaticSearch || i.EnableInteractiveSearch,
            downloadClientId = i.DownloadClientId ?? 0,
            // Prowlarr expects seedCriteria as a top-level object (always present, values > 0 for torrents, null for usenet)
            seedCriteria = new
            {
                seedRatio = i.Type == IndexerType.Torznab ? (double?)(i.SeedRatio ?? 1.0) : null,
                seedTime = i.Type == IndexerType.Torznab ? (int?)(i.SeedTime ?? 1) : null,
                seasonPackSeedTime = i.Type == IndexerType.Torznab ? (int?)(i.SeasonPackSeedTime ?? 1) : null
            },
            tags = i.Tags.ToArray(),
            fields = fields.ToArray(),
            // Prowlarr expects capabilities object with categories
            capabilities = new
            {
                categories = i.Categories.Select(c =>
                {
                    var catId = int.TryParse(c, out var cat) ? cat : 0;
                    return new
                    {
                        id = catId,
                        name = CategoryHelper.GetCategoryName(catId),
                        subCategories = new object[] { }
                    };
                }).ToArray(),
                supportsRawSearch = true,
                searchParams = new[] { "q" },
                tvSearchParams = new[] { "q", "season", "ep" },
                movieSearchParams = new[] { "q", "imdbid" },
                musicSearchParams = new[] { "q" },
                bookSearchParams = new[] { "q" }
            }
        };
    }).ToList();

    return Results.Ok(radarrIndexers);
});

// GET /api/v3/indexer/{id} - Get specific indexer (Radarr v3 API for Prowlarr)
app.MapGet("/api/v3/indexer/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] GET /api/v3/indexer/{Id}", id);

    var indexer = await db.Indexers.FindAsync(id);
    if (indexer == null)
        return Results.NotFound();

    return Results.Ok(new
    {
        id = indexer.Id,
        name = indexer.Name,
        enableRss = indexer.EnableRss,
        enableAutomaticSearch = indexer.EnableAutomaticSearch,
        enableInteractiveSearch = indexer.EnableInteractiveSearch,
        priority = indexer.Priority,
        implementation = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
        implementationName = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
        configContract = indexer.Type == IndexerType.Torznab ? "TorznabSettings" : "NewznabSettings",
        infoLink = "https://github.com/Prowlarr/Prowlarr",
        protocol = indexer.Type == IndexerType.Torznab ? "torrent" : "usenet",
        supportsRss = indexer.EnableRss,
        supportsSearch = indexer.EnableAutomaticSearch || indexer.EnableInteractiveSearch,
        downloadClientId = indexer.DownloadClientId ?? 0,
        // Prowlarr expects seedCriteria as a top-level object (always present, values > 0 for torrents, null for usenet)
        seedCriteria = new
        {
            seedRatio = indexer.Type == IndexerType.Torznab ? (double?)(indexer.SeedRatio ?? 1.0) : null,
            seedTime = indexer.Type == IndexerType.Torznab ? (int?)(indexer.SeedTime ?? 1) : null,
            seasonPackSeedTime = indexer.Type == IndexerType.Torznab ? (int?)(indexer.SeasonPackSeedTime ?? 1) : null
        },
        tags = indexer.Tags.ToArray(),
        fields = new object[]
        {
            new { order = 0, name = "baseUrl", label = "URL", helpText = indexer.Type == IndexerType.Torznab ? "Torznab feed URL" : "Newznab feed URL", helpLink = (string?)null, value = indexer.Url, type = "textbox", advanced = false, hidden = false },
            new { order = 1, name = "apiPath", label = "API Path", helpText = "Path to the api, usually /api", helpLink = (string?)null, value = "/api", type = "textbox", advanced = true, hidden = false },
            new { order = 2, name = "apiKey", label = "API Key", helpText = (string?)null, helpLink = (string?)null, value = indexer.ApiKey ?? "", type = "textbox", privacy = "apiKey", advanced = false, hidden = false },
            new { order = 3, name = "categories", label = "Categories", helpText = "Comma separated list of categories", helpLink = (string?)null, value = indexer.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray(), type = "select", advanced = false, hidden = false },
            new { order = 4, name = "minimumSeeders", label = "Minimum Seeders", helpText = "Minimum number of seeders required", helpLink = (string?)null, value = indexer.MinimumSeeders, type = "number", advanced = false, hidden = false }
        },
        // Prowlarr expects capabilities object with categories
        capabilities = new
        {
            categories = indexer.Categories.Select(c =>
            {
                var catId = int.TryParse(c, out var cat) ? cat : 0;
                return new
                {
                    id = catId,
                    name = CategoryHelper.GetCategoryName(catId),
                    subCategories = new object[] { }
                };
            }).ToArray(),
            supportsRawSearch = true,
            searchParams = new[] { "q" },
            tvSearchParams = new[] { "q", "season", "ep" },
            movieSearchParams = new[] { "q", "imdbid" },
            musicSearchParams = new[] { "q" },
            bookSearchParams = new[] { "q" }
        }
    });
});

// POST /api/v3/indexer - Add new indexer (Radarr v3 API for Prowlarr)
app.MapPost("/api/v3/indexer", async (HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[PROWLARR] POST /api/v3/indexer - Creating indexer: {Json}", json);

    try
    {
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Extract fields from Prowlarr's format
        var name = prowlarrIndexer.GetProperty("name").GetString() ?? "Unknown";
        var implementation = prowlarrIndexer.GetProperty("implementation").GetString() ?? "Newznab";
        var fieldsArray = prowlarrIndexer.GetProperty("fields").EnumerateArray();

        var baseUrl = "";
        var apiKey = "";
        var categories = new List<string>();
        var minimumSeeders = 1;
        double? seedRatio = null;
        int? seedTime = null;
        int? seasonPackSeedTime = null;
        int? earlyReleaseLimit = null;

        // Parse seedCriteria object if present (Prowlarr sends this for torrent indexers)
        if (prowlarrIndexer.TryGetProperty("seedCriteria", out var seedCriteria))
        {
            if (seedCriteria.TryGetProperty("seedRatio", out var seedRatioValue) && seedRatioValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                seedRatio = seedRatioValue.GetDouble();
            if (seedCriteria.TryGetProperty("seedTime", out var seedTimeValue) && seedTimeValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                seedTime = seedTimeValue.GetInt32();
            if (seedCriteria.TryGetProperty("seasonPackSeedTime", out var seasonValue) && seasonValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                seasonPackSeedTime = seasonValue.GetInt32();
        }

        foreach (var field in fieldsArray)
        {
            var fieldName = field.GetProperty("name").GetString();
            if (fieldName == "baseUrl")
                baseUrl = field.GetProperty("value").GetString() ?? "";
            else if (fieldName == "apiKey")
                apiKey = field.GetProperty("value").GetString() ?? "";
            else if (fieldName == "categories" && field.TryGetProperty("value", out var catValue) && catValue.ValueKind == System.Text.Json.JsonValueKind.Array)
                categories = catValue.EnumerateArray().Select(c => c.GetInt32().ToString()).ToList();
            else if (fieldName == "minimumSeeders" && field.TryGetProperty("value", out var seedValue))
                minimumSeeders = seedValue.GetInt32();
            else if (fieldName == "earlyReleaseLimit" && field.TryGetProperty("value", out var earlyValue))
                earlyReleaseLimit = earlyValue.GetInt32();
            // Note: animeCategories is a Sonarr-only field, not used in Radarr API
        }

        var indexer = new Indexer
        {
            Name = name,
            Type = implementation == "Torznab" ? IndexerType.Torznab : IndexerType.Newznab,
            Url = baseUrl,
            ApiKey = apiKey,
            Categories = categories,
            Enabled = prowlarrIndexer.TryGetProperty("enableRss", out var enableRssProp) ? enableRssProp.GetBoolean() : true,
            EnableRss = prowlarrIndexer.TryGetProperty("enableRss", out var rss) ? rss.GetBoolean() : true,
            EnableAutomaticSearch = prowlarrIndexer.TryGetProperty("enableAutomaticSearch", out var autoSearch) ? autoSearch.GetBoolean() : true,
            EnableInteractiveSearch = prowlarrIndexer.TryGetProperty("enableInteractiveSearch", out var intSearch) ? intSearch.GetBoolean() : true,
            Priority = prowlarrIndexer.TryGetProperty("priority", out var priorityProp) ? priorityProp.GetInt32() : 25,
            MinimumSeeders = minimumSeeders,
            SeedRatio = seedRatio,
            SeedTime = seedTime,
            SeasonPackSeedTime = seasonPackSeedTime,
            EarlyReleaseLimit = earlyReleaseLimit,
            AnimeCategories = null, // Radarr doesn't use anime categories (Sonarr-only field)
            Tags = prowlarrIndexer.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == System.Text.Json.JsonValueKind.Array
                ? tagsProp.EnumerateArray().Select(t => t.GetInt32()).ToList()
                : new List<int>(),
            Created = DateTime.UtcNow
        };

        db.Indexers.Add(indexer);
        await db.SaveChangesAsync();

        logger.LogInformation("[PROWLARR] Created indexer {Name} with ID {Id}", indexer.Name, indexer.Id);

        var responseFields = new List<object>
        {
            new { name = "baseUrl", value = indexer.Url },
            new { name = "apiPath", value = indexer.ApiPath },
            new { name = "apiKey", value = indexer.ApiKey },
            new { name = "categories", value = indexer.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray() },
            new { name = "minimumSeeders", value = indexer.MinimumSeeders }
        };

        // Add optional fields if present (NOT seed criteria - those go in seedCriteria object)
        if (indexer.EarlyReleaseLimit.HasValue)
            responseFields.Add(new { name = "earlyReleaseLimit", value = indexer.EarlyReleaseLimit.Value });
        // Note: animeCategories is a Sonarr-only field, not used in Radarr API
        if (!string.IsNullOrEmpty(indexer.AdditionalParameters))
            responseFields.Add(new { name = "additionalParameters", value = indexer.AdditionalParameters });
        if (indexer.MultiLanguages != null && indexer.MultiLanguages.Count > 0)
            responseFields.Add(new { name = "multiLanguages", value = string.Join(",", indexer.MultiLanguages) });
        responseFields.Add(new { name = "rejectBlocklistedTorrentHashes", value = indexer.RejectBlocklistedTorrentHashes });
        if (indexer.DownloadClientId.HasValue)
            responseFields.Add(new { name = "downloadClientId", value = indexer.DownloadClientId.Value });
        if (indexer.Tags.Count > 0)
            responseFields.Add(new { name = "tags", value = string.Join(",", indexer.Tags) });

        return Results.Ok(new
        {
            id = indexer.Id,
            name = indexer.Name,
            enableRss = indexer.EnableRss,
            enableAutomaticSearch = indexer.EnableAutomaticSearch,
            enableInteractiveSearch = indexer.EnableInteractiveSearch,
            priority = indexer.Priority,
            implementation = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            implementationName = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            configContract = indexer.Type == IndexerType.Torznab ? "TorznabSettings" : "NewznabSettings",
            protocol = indexer.Type == IndexerType.Torznab ? "torrent" : "usenet",
            supportsRss = indexer.EnableRss,
            supportsSearch = indexer.EnableAutomaticSearch || indexer.EnableInteractiveSearch,
            downloadClientId = indexer.DownloadClientId ?? 0,
            // Prowlarr expects seedCriteria as a top-level object (always present, null values for usenet)
            seedCriteria = new
            {
                seedRatio = indexer.Type == IndexerType.Torznab ? indexer.SeedRatio : (double?)null,
                seedTime = indexer.Type == IndexerType.Torznab ? indexer.SeedTime : (int?)null,
                seasonPackSeedTime = indexer.Type == IndexerType.Torznab ? indexer.SeasonPackSeedTime : (int?)null
            },
            tags = indexer.Tags.ToArray(),
            fields = responseFields.ToArray(),
            // Add capabilities object (required for Prowlarr's BuildRadarrIndexer at line 269)
            capabilities = new
            {
                categories = indexer.Categories.Select(c =>
                {
                    var catId = int.TryParse(c, out var cat) ? cat : 0;
                    return new
                    {
                        id = catId,
                        name = CategoryHelper.GetCategoryName(catId),
                        subCategories = new object[] { }
                    };
                }).ToArray(),
                supportsRawSearch = true,
                searchParams = new[] { "q" },
                tvSearchParams = new[] { "q", "season", "ep" },
                movieSearchParams = new[] { "q", "imdbid" },
                musicSearchParams = new[] { "q" },
                bookSearchParams = new[] { "q" }
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error creating indexer");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// PUT /api/v3/indexer/{id} - Update indexer (Radarr v3 API for Prowlarr)
app.MapPut("/api/v3/indexer/{id:int}", async (int id, HttpRequest request, SportarrDbContext db, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    logger.LogInformation("[PROWLARR] PUT /api/v3/indexer/{Id} - Updating indexer: {Json}", id, json);

    try
    {
        var prowlarrIndexer = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Extract baseUrl to identify the unique indexer (contains Prowlarr's indexer ID like /7/ or /1/)
        var fieldsArray = prowlarrIndexer.GetProperty("fields").EnumerateArray();
        var baseUrl = "";
        foreach (var field in fieldsArray)
        {
            if (field.GetProperty("name").GetString() == "baseUrl")
            {
                baseUrl = field.GetProperty("value").GetString() ?? "";
                break;
            }
        }

        if (string.IsNullOrEmpty(baseUrl))
        {
            logger.LogWarning("[PROWLARR] No baseUrl found in PUT request for ID {Id}", id);
            return Results.BadRequest(new { error = "baseUrl is required" });
        }

        // Find indexer by baseUrl (unique identifier) instead of by ID
        // This prevents Prowlarr from overwriting indexers when IDs don't match
        var indexer = await db.Indexers.FirstOrDefaultAsync(i => i.Url == baseUrl);

        if (indexer == null)
        {
            // Indexer doesn't exist yet - create it instead of returning NotFound
            // This handles the case where Prowlarr tries to update before creating
            logger.LogInformation("[PROWLARR] Indexer with baseUrl {BaseUrl} not found, creating new one", baseUrl);

            var name = prowlarrIndexer.GetProperty("name").GetString() ?? "Unknown";
            var implementation = prowlarrIndexer.GetProperty("implementation").GetString() ?? "Newznab";
            var categories = new List<string>();
            var minimumSeeders = 1;
            var apiKey = "";
            double? seedRatio = null;
            int? seedTime = null;
            int? seasonPackSeedTime = null;

            // Parse seedCriteria
            if (prowlarrIndexer.TryGetProperty("seedCriteria", out var seedCriteriaCreate))
            {
                if (seedCriteriaCreate.TryGetProperty("seedRatio", out var seedRatioValue) && seedRatioValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    seedRatio = seedRatioValue.GetDouble();
                if (seedCriteriaCreate.TryGetProperty("seedTime", out var seedTimeValue) && seedTimeValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    seedTime = seedTimeValue.GetInt32();
                if (seedCriteriaCreate.TryGetProperty("seasonPackSeedTime", out var seasonValue) && seasonValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    seasonPackSeedTime = seasonValue.GetInt32();
            }

            // Parse fields
            foreach (var field in fieldsArray)
            {
                var fieldName = field.GetProperty("name").GetString();
                if (fieldName == "apiKey")
                    apiKey = field.GetProperty("value").GetString() ?? "";
                else if (fieldName == "categories" && field.TryGetProperty("value", out var catValue) && catValue.ValueKind == System.Text.Json.JsonValueKind.Array)
                    categories = catValue.EnumerateArray().Select(c => c.GetInt32().ToString()).ToList();
                else if (fieldName == "minimumSeeders" && field.TryGetProperty("value", out var seedValue))
                    minimumSeeders = seedValue.GetInt32();
            }

            indexer = new Indexer
            {
                Name = name,
                Type = implementation == "Torznab" ? IndexerType.Torznab : IndexerType.Newznab,
                Url = baseUrl,
                ApiKey = apiKey,
                Categories = categories,
                Enabled = prowlarrIndexer.TryGetProperty("enableRss", out var enableRssProp) ? enableRssProp.GetBoolean() : true,
                EnableRss = prowlarrIndexer.TryGetProperty("enableRss", out var rss) ? rss.GetBoolean() : true,
                EnableAutomaticSearch = prowlarrIndexer.TryGetProperty("enableAutomaticSearch", out var autoSearch) ? autoSearch.GetBoolean() : true,
                EnableInteractiveSearch = prowlarrIndexer.TryGetProperty("enableInteractiveSearch", out var intSearch) ? intSearch.GetBoolean() : true,
                Priority = prowlarrIndexer.TryGetProperty("priority", out var priorityProp) ? priorityProp.GetInt32() : 25,
                MinimumSeeders = minimumSeeders,
                SeedRatio = seedRatio,
                SeedTime = seedTime,
                SeasonPackSeedTime = seasonPackSeedTime,
                Tags = prowlarrIndexer.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? tagsProp.EnumerateArray().Select(t => t.GetInt32()).ToList()
                    : new List<int>(),
                Created = DateTime.UtcNow
            };

            db.Indexers.Add(indexer);
            await db.SaveChangesAsync();
            logger.LogInformation("[PROWLARR] Created new indexer {Name} (ID: {Id}) via PUT endpoint", indexer.Name, indexer.Id);
        }
        else
        {
            // Update existing indexer
            indexer.Name = prowlarrIndexer.GetProperty("name").GetString() ?? indexer.Name;
            indexer.Type = prowlarrIndexer.GetProperty("implementation").GetString() == "Torznab" ? IndexerType.Torznab : IndexerType.Newznab;

            // Parse seedCriteria object if present (Prowlarr sends this for torrent indexers)
            if (prowlarrIndexer.TryGetProperty("seedCriteria", out var seedCriteria))
            {
                if (seedCriteria.TryGetProperty("seedRatio", out var seedRatioValue) && seedRatioValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    indexer.SeedRatio = seedRatioValue.GetDouble();
                else
                    indexer.SeedRatio = null;

                if (seedCriteria.TryGetProperty("seedTime", out var seedTimeValue) && seedTimeValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    indexer.SeedTime = seedTimeValue.GetInt32();
                else
                    indexer.SeedTime = null;

                if (seedCriteria.TryGetProperty("seasonPackSeedTime", out var seasonValue) && seasonValue.ValueKind != System.Text.Json.JsonValueKind.Null)
                    indexer.SeasonPackSeedTime = seasonValue.GetInt32();
                else
                    indexer.SeasonPackSeedTime = null;
            }

            // Update fields
            foreach (var field in fieldsArray)
            {
                var fieldName = field.GetProperty("name").GetString();
                if (fieldName == "baseUrl")
                    indexer.Url = field.GetProperty("value").GetString() ?? indexer.Url;
                else if (fieldName == "apiKey")
                    indexer.ApiKey = field.GetProperty("value").GetString();
                else if (fieldName == "categories" && field.TryGetProperty("value", out var catValue) && catValue.ValueKind == System.Text.Json.JsonValueKind.Array)
                    indexer.Categories = catValue.EnumerateArray().Select(c => c.GetInt32().ToString()).ToList();
                else if (fieldName == "minimumSeeders" && field.TryGetProperty("value", out var seedValue))
                    indexer.MinimumSeeders = seedValue.GetInt32();
            }

            if (prowlarrIndexer.TryGetProperty("priority", out var priorityProp))
                indexer.Priority = priorityProp.GetInt32();
            if (prowlarrIndexer.TryGetProperty("enableRss", out var rss))
                indexer.EnableRss = rss.GetBoolean();
            if (prowlarrIndexer.TryGetProperty("enableAutomaticSearch", out var autoSearch))
                indexer.EnableAutomaticSearch = autoSearch.GetBoolean();
            if (prowlarrIndexer.TryGetProperty("enableInteractiveSearch", out var intSearch))
                indexer.EnableInteractiveSearch = intSearch.GetBoolean();

            indexer.LastModified = DateTime.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation("[PROWLARR] Updated indexer {Name} (ID: {Id})", indexer.Name, indexer.Id);
        }

        return Results.Ok(new
        {
            id = indexer.Id,
            name = indexer.Name,
            enableRss = indexer.EnableRss,
            enableAutomaticSearch = indexer.EnableAutomaticSearch,
            enableInteractiveSearch = indexer.EnableInteractiveSearch,
            priority = indexer.Priority,
            implementation = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            implementationName = indexer.Type == IndexerType.Torznab ? "Torznab" : "Newznab",
            configContract = indexer.Type == IndexerType.Torznab ? "TorznabSettings" : "NewznabSettings",
            protocol = indexer.Type == IndexerType.Torznab ? "torrent" : "usenet",
            supportsRss = indexer.EnableRss,
            supportsSearch = indexer.EnableAutomaticSearch || indexer.EnableInteractiveSearch,
            downloadClientId = indexer.DownloadClientId ?? 0,
            // Prowlarr expects seedCriteria as a top-level object (always present, null values for usenet)
            seedCriteria = new
            {
                seedRatio = indexer.Type == IndexerType.Torznab ? indexer.SeedRatio : (double?)null,
                seedTime = indexer.Type == IndexerType.Torznab ? indexer.SeedTime : (int?)null,
                seasonPackSeedTime = indexer.Type == IndexerType.Torznab ? indexer.SeasonPackSeedTime : (int?)null
            },
            tags = indexer.Tags.ToArray(),
            fields = new object[]
            {
                new { name = "baseUrl", value = indexer.Url },
                new { name = "apiPath", value = "/api" },
                new { name = "apiKey", value = indexer.ApiKey },
                new { name = "categories", value = indexer.Categories.Select(c => int.TryParse(c, out var cat) ? cat : 0).ToArray() },
                new { name = "minimumSeeders", value = indexer.MinimumSeeders }
            },
            // Add capabilities object (required for Prowlarr's BuildRadarrIndexer at line 269)
            capabilities = new
            {
                categories = indexer.Categories.Select(c =>
                {
                    var catId = int.TryParse(c, out var cat) ? cat : 0;
                    return new
                    {
                        id = catId,
                        name = CategoryHelper.GetCategoryName(catId),
                        subCategories = new object[] { }
                    };
                }).ToArray(),
                supportsRawSearch = true,
                searchParams = new[] { "q" },
                tvSearchParams = new[] { "q", "season", "ep" },
                movieSearchParams = new[] { "q", "imdbid" },
                musicSearchParams = new[] { "q" },
                bookSearchParams = new[] { "q" }
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[PROWLARR] Error updating indexer");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// DELETE /api/v3/indexer/{id} - Delete indexer (Radarr v3 API for Prowlarr)
app.MapDelete("/api/v3/indexer/{id:int}", async (int id, SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogInformation("[PROWLARR] DELETE /api/v3/indexer/{Id}", id);

    var indexer = await db.Indexers.FindAsync(id);
    if (indexer == null)
        return Results.NotFound();

    db.Indexers.Remove(indexer);
    await db.SaveChangesAsync();

    logger.LogInformation("[PROWLARR] Deleted indexer {Name} (ID: {Id})", indexer.Name, id);

    return Results.Ok(new { });
});

// GET /api/v3/downloadclient - Get download clients (Radarr v3 API for Prowlarr)
// Prowlarr uses this to determine which protocols are supported (torrent vs usenet)
// Returns actual download clients configured by the user
app.MapGet("/api/v3/downloadclient", async (SportarrDbContext db, ILogger<Program> logger) =>
{
    logger.LogWarning("[PROWLARR] *** GET /api/v3/downloadclient - ENDPOINT WAS CALLED! ***");

    var downloadClients = await db.DownloadClients.ToListAsync();
    logger.LogWarning("[PROWLARR] Found {Count} download clients in database", downloadClients.Count);

    var radarrClients = downloadClients.Select(dc =>
    {
        // Map Sportarr download client type to protocol (torrent vs usenet)
        var protocol = dc.Type switch
        {
            DownloadClientType.QBittorrent => "torrent",
            DownloadClientType.Transmission => "torrent",
            DownloadClientType.Deluge => "torrent",
            DownloadClientType.RTorrent => "torrent",
            DownloadClientType.UTorrent => "torrent",
            DownloadClientType.Sabnzbd => "usenet",
            DownloadClientType.NzbGet => "usenet",
            _ => "torrent"
        };

        // Map type to Radarr implementation name
        var (implementation, implementationName, configContract, infoLink) = dc.Type switch
        {
            DownloadClientType.QBittorrent => ("QBittorrent", "qBittorrent", "QBittorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Transmission => ("Transmission", "Transmission", "TransmissionSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Deluge => ("Deluge", "Deluge", "DelugeSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.RTorrent => ("RTorrent", "rTorrent", "RTorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.UTorrent => ("UTorrent", "uTorrent", "UTorrentSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.Sabnzbd => ("Sabnzbd", "SABnzbd", "SabnzbdSettings", "https://github.com/Sportarr/Sportarr"),
            DownloadClientType.NzbGet => ("NzbGet", "NZBGet", "NzbGetSettings", "https://github.com/Sportarr/Sportarr"),
            _ => ("QBittorrent", "qBittorrent", "QBittorrentSettings", "https://github.com/Sportarr/Sportarr")
        };

        return new
        {
            enable = dc.Enabled,
            protocol = protocol,
            priority = dc.Priority,
            removeCompletedDownloads = true,
            removeFailedDownloads = true,
            name = dc.Name,
            fields = new object[] { },
            implementationName = implementationName,
            implementation = implementation,
            configContract = configContract,
            infoLink = infoLink,
            tags = new int[] { },
            id = dc.Id
        };
    }).ToList();

    logger.LogInformation("[PROWLARR] Returning {Count} download clients", radarrClients.Count);
    return Results.Ok(radarrClients);
});

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

Log.Information("========================================");
Log.Information("Sportarr is starting...");
Log.Information("App Version: {AppVersion}", Sportarr.Api.Version.GetFullVersion());
Log.Information("API Version: {ApiVersion}", Sportarr.Api.Version.ApiVersion);
Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("URL: http://localhost:1867");
Log.Information("Logs Directory: {LogsPath}", logsPath);
Log.Information("========================================");

// Helper function: Calculate part relevance score for sorting search results
// Prioritizes main parts (Main Card, Prelims) over unlikely parts
static int GetPartRelevanceScore(string title, string? requestedPart)
{
    if (string.IsNullOrEmpty(title)) return 0;

    var titleLower = title.ToLowerInvariant();
    int score = 0;

    // If user requested a specific part, boost results that match it
    if (!string.IsNullOrEmpty(requestedPart))
    {
        if (titleLower.Contains(requestedPart.ToLowerInvariant()))
        {
            score += 100; // Strong boost for matching requested part
        }
    }

    // Boost common multi-part episode names (most likely what users want)
    if (titleLower.Contains("main card") || titleLower.Contains("maincard"))
        score += 50;
    else if (titleLower.Contains("prelim"))
        score += 40;
    else if (titleLower.Contains("early prelim"))
        score += 35;
    else if (titleLower.Contains("weigh") || titleLower.Contains("weigh-in"))
        score += 10; // Lower priority for weigh-ins
    else if (titleLower.Contains("press conference") || titleLower.Contains("presser"))
        score += 5; // Lowest priority for press conferences

    return score;
}

try
{
    Log.Information("[Sportarr] Starting web host");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "[Sportarr] Application terminated unexpectedly");
    throw;
}
finally
{
    Log.Information("[Sportarr] Shutting down...");
    Log.CloseAndFlush();
}

// Request/Response models
public record UpdateSuggestionRequest(int? EventId, string? Part);

// Make Program class accessible to integration tests
public partial class Program { }
