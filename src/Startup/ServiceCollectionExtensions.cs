using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Sportarr.Api.Data;
using Sportarr.Api.Health;
using Sportarr.Api.Middleware;
using Sportarr.Api.Services;
using System.Net.Http;
using System.Text.Json;

namespace Sportarr.Api.Startup;

public static class ServiceCollectionExtensions
{
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

        // IPTV stream proxying (avoids CORS issues in browser)
        services.AddHttpClient("StreamProxy")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                PooledConnectionLifetime = TimeSpan.FromMinutes(1),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
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
        // Without an explicit timeout, .NET defaults to 100s × 3 retries = 5+ min thread-pin on a hung sportarr.net request.
        services.AddHttpClient<SportarrApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            })
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Sportarr/1.0");
            })
            .AddTransientHttpErrorPolicy(policyBuilder =>
                policyBuilder.WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        Console.WriteLine($"[SportarrAPI] Retry {retryAttempt} after {timespan.TotalSeconds}s delay");
                    }
                ));

        return services;
    }

    public static IServiceCollection AddSportarrCoreServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddTransient<RateLimitHandler>();

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
        services.AddScoped<EventMappingService>();

        return services;
    }

    public static IServiceCollection AddSportarrFileServices(this IServiceCollection services)
    {
        services.AddScoped<MediaFileParser>();
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
        services.AddScoped<DvrRecordingService>();
        services.AddScoped<EventDvrService>();
        services.AddScoped<DvrQualityScoreCalculator>();
        services.AddScoped<XmltvParserService>();
        services.AddScoped<EpgService>();
        services.AddScoped<EpgSchedulingService>();
        services.AddScoped<FilteredExportService>();

        return services;
    }

    public static IServiceCollection AddSportarrBackgroundServices(this IServiceCollection services)
    {
        services.AddSingleton<TaskService>();
        services.AddSingleton<DiskScanService>();
        services.AddHostedService(sp => sp.GetRequiredService<DiskScanService>());

        services.AddHostedService<TrashSyncBackgroundService>();
        services.AddHostedService<EnhancedDownloadMonitorService>();
        services.AddHostedService<RssSyncService>();
        services.AddHostedService<BacklogSearchService>();
        services.AddHostedService<PendingReleaseReaperService>();
        services.AddHostedService<TvScheduleSyncService>();
        services.AddHostedService<FileWatcherService>();
        services.AddHostedService<EventMappingSyncBackgroundService>();
        services.AddHostedService<LeagueEventAutoSyncService>();
        services.AddHostedService<DvrSchedulerService>();

        services.AddSingleton<DvrAutoSchedulerService>();
        services.AddHostedService(sp => sp.GetRequiredService<DvrAutoSchedulerService>());

        return services;
    }

    public static IServiceCollection AddSportarrDatabase(this IServiceCollection services, string dbPath)
    {
        services.AddDbContext<SportarrDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}")
                   .ConfigureWarnings(w => w
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning)
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning)));

        services.AddDbContextFactory<SportarrDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}")
                   .ConfigureWarnings(w => w
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.AmbientTransactionWarning)
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)
                       .Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning)), ServiceLifetime.Scoped);

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
        });

        return services;
    }

    public static IServiceCollection AddSportarrSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
