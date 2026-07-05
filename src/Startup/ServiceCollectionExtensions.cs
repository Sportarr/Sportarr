using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Sportarr.Api.Data;
using Sportarr.Api.Health;
using Sportarr.Api.Middleware;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Sportarr.Api.Startup;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// CORS policy name for public, unauthenticated endpoints that browser-based
    /// plugin config pages probe cross-origin (e.g. /api/health). Apply with
    /// .RequireCors(ServiceCollectionExtensions.PublicProbeCorsPolicy).
    /// </summary>
    public const string PublicProbeCorsPolicy = "PublicProbe";

    public static IServiceCollection AddSportarrHttpClients(this IServiceCollection services)
    {
        // Default named HttpClient used by services that don't configure their own.
        // PooledConnectionLifetime keeps DNS fresh (Docker container names rotate),
        // timeout prevents hung calls from pinning thread pool threads.
        services.AddHttpClient(string.Empty)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
            });

        // EPG/XMLTV downloads can be very large gzipped feeds, so allow a longer timeout.
        services.AddHttpClient("EpgClient")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
            });

        // PooledConnectionLifetime ensures DNS is re-resolved periodically (important for Docker container name resolution)
        services.AddHttpClient("DownloadClient")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(100);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
            });

        // Self-signed certificate bypass for qBittorrent/other clients behind reverse proxies
        services.AddHttpClient("DownloadClientSkipSsl")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                }
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(100);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
            });

        services.AddHttpClient("TrashGuides")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0 (https://github.com/Sportarr/Sportarr)");
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            });

        // Indexer searches with rate limiting and Polly retry policy
        services.AddHttpClient("IndexerClient")
            .AddHttpMessageHandler<RateLimitHandler>()
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
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
            });

        // BTN requires a dedicated client: no auto-redirect (so Cloudflare/site redirects surface as
        // 3xx rather than silently resolving to an HTML page), and a recognised User-Agent.
        services.AddHttpClient("BtnClient")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sonarr/4.0.14.3025");
            });

        // IPTV stream proxying (avoids CORS issues in browser)
        services.AddHttpClient("StreamProxy")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                PooledConnectionLifetime = TimeSpan.FromMinutes(1),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                // SSRF guard: the stream proxy is reachable anonymously and fetches
                // caller-supplied URLs, so validate the actual IP on every connection
                // (initial request + each redirect hop) and refuse internal targets.
                ConnectCallback = async (ctx, ct) =>
                    await Sportarr.Api.Helpers.SsrfGuard.ConnectValidatedAsync(ctx.DnsEndPoint, ct)
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
            });

        // IPTV services (source syncing, channel testing, API calls)
        // CRITICAL: Allow redirects - many IPTV providers (especially Xtream Codes) use 302 redirects
        services.AddHttpClient("IptvClient")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VLC/3.0.18 LibVLC/3.0.18");
            });

        // Sportarr API client (sportarr.net) for sports metadata.
        // Timeout of 90s rather than the previous 30s. Cache hits on
        // sportarr.net come back in <100ms regardless; the long tail
        // is cache misses, where sportarr.net has to go upstream to
        // TheSportsDB. On slow-upstream days TheSportsDB can take
        // 30-90s to respond, and the prior 30s cap was firing before
        // those responses landed -- every cold-cache historical
        // season for the user's library was timing out and being
        // skipped, leaving gaps in the local Sportarr DB even when
        // a few extra seconds would have completed the fetch. The
        // outer 90s also leaves enough room for the Polly retry
        // policy below (2s+4s+8s+16s = 30s of backoff alone) to
        // actually fire its full sequence on transient 5xx, instead
        // of being clipped by the request timeout half-way through.
        // Outermost handler that tallies one hub HTTP call per logical
        // request into the ambient SyncMetrics counter (registered before
        // the Polly policy below so retries aren't double-counted).
        services.AddTransient<SyncHttpCountingHandler>();

        services.AddHttpClient<SportarrApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(90);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
            })
            .AddHttpMessageHandler<SyncHttpCountingHandler>()
            // Retry transient 5xx / network errors with a short exponential
            // backoff (2s, 4s, 8s). Retry 429 separately with a much longer
            // base (8s, 16s, 32s, 64s) and honor the server's Retry-After
            // header when present. 429 means sportarr.net is explicitly
            // asking us to slow down, so doubling down with a fast retry
            // schedule would make things worse for everyone.
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    retryCount: 4,
                    sleepDurationProvider: (attempt, outcome, _) =>
                    {
                        var status = outcome.Result?.StatusCode;
                        if (status == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            var retryAfter = outcome.Result?.Headers.RetryAfter?.Delta;
                            if (retryAfter is { } hint && hint > TimeSpan.Zero)
                            {
                                return hint;
                            }
                            return TimeSpan.FromSeconds(Math.Pow(2, attempt + 2));
                        }
                        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    },
                    onRetryAsync: (outcome, timespan, retryAttempt, _) =>
                    {
                        var status = outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.GetType().Name ?? "unknown";
                        Console.WriteLine($"[SportarrAPI] Retry {retryAttempt} after {timespan.TotalSeconds:F1}s ({status})");
                        return Task.CompletedTask;
                    }));

        return services;
    }

    public static IServiceCollection AddSportarrCoreServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddTransient<RateLimitHandler>();

        // Throttle login attempts per client IP to blunt online password guessing.
        // The login endpoint opts in via .RequireRateLimiting("login").
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("login", httpContext =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    }));
        });

        services.AddControllers();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        });

        services.AddSingleton<ConfigService>();
        services.AddScoped<UserService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<SimpleAuthService>();
        services.AddScoped<SessionService>();

        services.AddSingleton<DiskSpaceService>();
        services.AddScoped<HealthCheckService>();
        services.AddScoped<BackupService>();
        services.AddScoped<NotificationService>();
        // Backup-restore reconciliation stack. PathRemap + LibraryRescan
        // are scoped so they pick up a fresh DbContext per request (the
        // db file gets replaced during restore so reusing a singleton
        // would hold the old handle); RestoreReconciliationService
        // composes them.
        services.AddScoped<PathRemapService>();
        services.AddScoped<LibraryRescanService>();
        services.AddScoped<RestoreReconciliationService>();

        return services;
    }

    public static IServiceCollection AddSportarrIndexing(this IServiceCollection services)
    {
        services.AddScoped<DownloadClientService>();
        services.AddScoped<IndexerStatusService>();
        services.AddScoped<IndexerSearchService>();
        services.AddScoped<ReleaseMatchingService>();
        services.AddSingleton<ReleaseMatchScorer>();
        services.AddScoped<ReleaseCacheService>();
        services.AddSingleton<SearchQueueService>();
        services.AddSingleton<SearchResultCache>();
        services.AddSingleton<CustomFormatMatchCache>();
        services.AddScoped<AutomaticSearchService>();
        services.AddScoped<DelayProfileService>();
        services.AddScoped<QualityDetectionService>();
        services.AddScoped<ReleaseEvaluator>();
        services.AddScoped<ReleaseProfileService>();
        services.AddScoped<CustomFormatService>();
        services.AddScoped<TrashGuideSyncService>();
        services.AddScoped<SeasonSearchService>();

        return services;
    }

    public static IServiceCollection AddSportarrFileServices(this IServiceCollection services)
    {
        services.AddScoped<MediaFileParser>();
        services.AddScoped<MediaFileInspector>();
        services.AddScoped<SportsFileNameParser>();
        services.AddScoped<FileNamingService>();
        services.AddScoped<FileRenameService>();
        services.AddScoped<EventPartDetector>();
        services.AddScoped<FileFormatManager>();
        services.AddScoped<FileImportService>();
        services.AddScoped<ImportMatchingService>();
        services.AddScoped<LibraryImportService>();
        services.AddScoped<ImportListService>();
        services.AddScoped<ProvideImportItemService>();
        services.AddScoped<EventQueryService>();
        services.AddScoped<LeagueEventSyncService>();
        services.AddScoped<TeamLeagueDiscoveryService>();
        services.AddScoped<PackImportService>();
        services.AddScoped<LeagueMoveService>();

        return services;
    }

    public static IServiceCollection AddSportarrIptv(this IServiceCollection services)
    {
        services.AddScoped<M3uParserService>();
        services.AddScoped<XtreamCodesClient>();
        services.AddScoped<IptvSourceService>();
        services.AddScoped<ChannelAutoMappingService>();
        services.AddSingleton<FFmpegRecorderService>();
        services.AddSingleton<FFmpegStreamService>();
        // Viewer-stream counting for per-source MaxStreams enforcement on
        // the proxy/HDHomeRun path.
        services.AddSingleton<StreamSessionTracker>();
        services.AddScoped<DvrRecordingService>();
        services.AddScoped<EventDvrService>();
        services.AddScoped<DvrQualityScoreCalculator>();
        services.AddScoped<XmltvParserService>();
        services.AddScoped<EpgService>();
        services.AddScoped<EpgSchedulingService>();
        services.AddScoped<EventChannelResolverService>();
        services.AddScoped<FilteredExportService>();
        // Singleton because it caches the iptv-org/database CSV
        // (~30k rows) in memory across requests.
        services.AddSingleton<IptvOrgSyncService>();

        return services;
    }

    public static IServiceCollection AddSportarrBackgroundServices(this IServiceCollection services)
    {
        services.AddSingleton<TaskService>();
        services.AddHostedService<TaskQueueRecoveryService>();

        services.AddSingleton<DiskScanService>();
        services.AddHostedService(sp => sp.GetRequiredService<DiskScanService>());

        services.AddHostedService<TrashSyncBackgroundService>();
        services.AddHostedService<EnhancedDownloadMonitorService>();
        services.AddHostedService<RssSyncService>();
        services.AddHostedService<BacklogSearchService>();
        services.AddHostedService<PendingReleaseReaperService>();
        services.AddHostedService<TvScheduleSyncService>();
        services.AddHostedService<FileWatcherService>();
        // LeagueEventAutoSyncService (24h full league walk) is deliberately
        // NOT registered on this branch: the hub changes poller below is
        // being soak-tested as the sole ongoing event ingest. Initial league
        // add still runs its one-time full historical sync (the feed is
        // delta-only and cannot backfill history), and the manual refresh
        // button remains as the escape hatch. Before this promotes to dev,
        // decide the final shape: re-register the auto-sync at a stretched
        // interval (e.g. weekly) as a self-heal pass, or keep poller-only.
        //
        // Singleton + hosted wrapper (same pattern as DiskScanService) so
        // TaskService can trigger an immediate poll cycle on demand — the
        // refresh button's "current" scope is wired to PollNowAsync.
        services.AddSingleton<HubChangesPollerService>();
        services.AddHostedService(sp => sp.GetRequiredService<HubChangesPollerService>());
        services.AddHostedService<DvrSchedulerService>();

        services.AddSingleton<DvrAutoSchedulerService>();
        services.AddHostedService(sp => sp.GetRequiredService<DvrAutoSchedulerService>());

        // Reconciles DvrRecording.Status against actual ffmpeg state -
        // catches crashes, app restarts, frozen upstream sources.
        services.AddHostedService<DvrWatchdogService>();

        // Downloads finished events from the provider's catchup/timeshift
        // archive (Method=Catchup rows) - the post-air counterpart to the
        // live scheduler above. Method ported from timeshifter by
        // scottrobertson (github.com/scottrobertson/timeshifter).
        services.AddHostedService<CatchupDownloadService>();

        // Auto-unmonitor + delete files for events past Config.EventRetentionDays.
        // Off by default (Config.EnableEventRetention).
        services.AddSingleton<EventRetentionService>();
        services.AddHostedService(sp => sp.GetRequiredService<EventRetentionService>());

        // Daily maintenance: scheduled backups (BackupInterval), recycle-bin
        // cleanup, DVR recording retention, system-event pruning.
        services.AddHostedService<HousekeepingService>();

        // Periodic health evaluation + OnHealthIssue/OnHealthRestored
        // notifications; previously checks only ran when the UI asked.
        services.AddHostedService<HealthCheckMonitorService>();

        // Periodic sync of enabled import lists (previously manual-only).
        services.AddHostedService<ImportListSyncService>();

        return services;
    }

    public static IServiceCollection AddSportarrDatabase(this IServiceCollection services, IConfiguration configuration, string dbPath)
    {
        // Single shared interceptor instance. It only does work inside a
        // SyncMetrics measured block (one AsyncLocal read otherwise), so it
        // is safe to attach to every context including the request path.
        var commandCounter = new Sportarr.Api.Data.CommandCountingInterceptor();
        var dbSettings = DatabaseSettings.FromConfiguration(configuration);

        void ConfigureProvider(DbContextOptionsBuilder options)
        {
            if (dbSettings.Provider == DatabaseProviderKind.Postgres)
            {
                // Postgres migrations live in the separate Sportarr.Migrations.Postgres
                // project/assembly - not alongside the SQLite ones in Sportarr.Data - so
                // Migrate() only ever sees the Postgres-only migration history. See that
                // project's csproj comment for why it's a separate assembly rather than a
                // subfolder of Sportarr.Data.
                options.UseNpgsql(dbSettings.ResolvePostgresConnectionString(),
                    npgsql => npgsql.MigrationsAssembly("Sportarr.Migrations.Postgres"));
            }
            else
            {
                // Default migrations assembly (Sportarr.Data, where SportarrDbContext and
                // the SQLite migration history both live) - no override needed.
                options.UseSqlite($"Data Source={dbPath}");
            }
        }

        services.AddDbContext<SportarrDbContext>(options =>
        {
            ConfigureProvider(options);
            options.AddInterceptors(commandCounter)
                   .ConfigureWarnings(w => w
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning)
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning));
        });

        // No AddInterceptors here: EF merges every options configuration
        // registered for the same context type, so the AddDbContext call
        // above already attaches the counter to factory-created contexts
        // too. Registering it in both lambdas made the interceptor fire
        // twice per command — the first [Sync Metrics] baselines showed
        // every per-shape count even, including queries that run once.
        services.AddDbContextFactory<SportarrDbContext>(options =>
        {
            ConfigureProvider(options);
            options.ConfigureWarnings(w => w
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning)
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning));
        }, ServiceLifetime.Scoped);

        return services;
    }

    public static IServiceCollection AddSportarrCors(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (environment.IsDevelopment())
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                }
                else
                {
                    policy.WithOrigins(
                            "http://localhost:5000",
                            "http://localhost:5001",
                            "https://localhost:5000",
                            "https://localhost:5001",
                            "http://127.0.0.1:5000",
                            "http://127.0.0.1:5001")
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
            });

            // Permissive policy for the few public, unauthenticated endpoints
            // that media-server plugin config pages probe cross-origin from the
            // browser — e.g. the Jellyfin/Emby/Plex "Test Connection" button
            // fetching /api/health from the media server's own web UI, which is
            // a different origin/port than the Sportarr instance. The default
            // policy only allows the local dev UI origins, so that fetch was
            // blocked by CORS even though the configured URL was reachable (the
            // plugin itself works, since it calls server-to-server with no CORS).
            // No credentials, and applied only to specific endpoints via
            // RequireCors, so this does not widen the authenticated API surface.
            options.AddPolicy(PublicProbeCorsPolicy, policy =>
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader());
        });

        return services;
    }

    public static IServiceCollection AddSportarrSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }

    public static IServiceCollection AddSportarrValidation(this IServiceCollection services)
    {
        // Register all FluentValidation validators in the assembly.
        services.AddValidatorsFromAssembly(typeof(LoginRequestValidator).Assembly);
        return services;
    }
}
