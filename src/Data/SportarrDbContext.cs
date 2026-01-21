using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sportarr.Api.Models;

namespace Sportarr.Api.Data;

public class SportarrDbContext : DbContext
{
    public SportarrDbContext(DbContextOptions<SportarrDbContext> options) : base(options)
    {
    }

    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventFile> EventFiles => Set<EventFile>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<LeagueTeam> LeagueTeams => Set<LeagueTeam>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<QualityProfile> QualityProfiles => Set<QualityProfile>();
    public DbSet<CustomFormat> CustomFormats => Set<CustomFormat>();
    public DbSet<ProfileFormatItem> ProfileFormatItems => Set<ProfileFormatItem>();
    public DbSet<QualityDefinition> QualityDefinitions => Set<QualityDefinition>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<RootFolder> RootFolders => Set<RootFolder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DownloadClient> DownloadClients => Set<DownloadClient>();
    public DbSet<DownloadQueueItem> DownloadQueue => Set<DownloadQueueItem>();
    public DbSet<BlocklistItem> Blocklist => Set<BlocklistItem>();
    public DbSet<PendingImport> PendingImports => Set<PendingImport>();
    public DbSet<Indexer> Indexers => Set<Indexer>();
    public DbSet<IndexerStatus> IndexerStatuses => Set<IndexerStatus>();
    public DbSet<AppTask> Tasks => Set<AppTask>();
    public DbSet<MediaManagementSettings> MediaManagementSettings => Set<MediaManagementSettings>();
    public DbSet<ImportHistory> ImportHistories => Set<ImportHistory>();
    public DbSet<DelayProfile> DelayProfiles => Set<DelayProfile>();
    public DbSet<ReleaseProfile> ReleaseProfiles => Set<ReleaseProfile>();
    public DbSet<ImportList> ImportLists => Set<ImportList>();
    public DbSet<MetadataProvider> MetadataProviders => Set<MetadataProvider>();
    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
    public DbSet<RemotePathMapping> RemotePathMappings => Set<RemotePathMapping>();
    public DbSet<GrabHistory> GrabHistory => Set<GrabHistory>();

    // Release cache for RSS-first search strategy
    public DbSet<ReleaseCache> ReleaseCache => Set<ReleaseCache>();

    // IPTV/DVR entities
    public DbSet<IptvSource> IptvSources => Set<IptvSource>();
    public DbSet<IptvChannel> IptvChannels => Set<IptvChannel>();
    public DbSet<ChannelLeagueMapping> ChannelLeagueMappings => Set<ChannelLeagueMapping>();
    public DbSet<DvrRecording> DvrRecordings => Set<DvrRecording>();
    public DbSet<DvrQualityProfile> DvrQualityProfiles => Set<DvrQualityProfile>();
    public DbSet<EpgSource> EpgSources => Set<EpgSource>();
    public DbSet<EpgChannel> EpgChannels => Set<EpgChannel>();
    public DbSet<EpgProgram> EpgPrograms => Set<EpgProgram>();

    // Event mapping (synced from Sportarr-API with local overrides)
    public DbSet<EventMapping> EventMappings => Set<EventMapping>();

    // Submitted mapping requests (tracks requests sent to Sportarr-API for status checking)
    public DbSet<SubmittedMappingRequest> SubmittedMappingRequests => Set<SubmittedMappingRequest>();

    // Import list exclusions (for Maintainerr/Sonarr API compatibility)
    public DbSet<ImportListExclusion> ImportListExclusions => Set<ImportListExclusion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Event configuration
        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Sport).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ExternalId).HasMaxLength(50);
            // DateEventFallback is only used during API deserialization, not stored in DB
            entity.Ignore(e => e.DateEventFallback);
            // Image URL fields are only used during API deserialization, not stored in DB
            // They get collected into the Images list during sync
            entity.Ignore(e => e.PosterUrl);
            entity.Ignore(e => e.ThumbUrl);
            entity.Ignore(e => e.BannerUrl);
            entity.Ignore(e => e.FanartUrl);
            entity.Property(e => e.Season).HasMaxLength(50);
            entity.Property(e => e.Round).HasMaxLength(100);
            entity.Property(e => e.Broadcast).HasMaxLength(500);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.Images).HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            ).Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
            entity.HasOne(e => e.League)
                  .WithMany()
                  .HasForeignKey(e => e.LeagueId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.HomeTeam)
                  .WithMany()
                  .HasForeignKey(e => e.HomeTeamId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.AwayTeam)
                  .WithMany()
                  .HasForeignKey(e => e.AwayTeamId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.EventDate);
            entity.HasIndex(e => e.Sport);
            entity.HasIndex(e => e.LeagueId);
            entity.HasIndex(e => e.ExternalId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Monitored); // Frequently filtered in searches
        });

        // EventFile configuration
        modelBuilder.Entity<EventFile>(entity =>
        {
            entity.HasKey(ef => ef.Id);
            entity.Property(ef => ef.FilePath).IsRequired().HasMaxLength(1000);
            entity.Property(ef => ef.Quality).HasMaxLength(200);
            entity.Property(ef => ef.PartName).HasMaxLength(100);
            entity.HasOne(ef => ef.Event)
                  .WithMany(e => e.Files)
                  .HasForeignKey(ef => ef.EventId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(ef => ef.EventId);
            entity.HasIndex(ef => ef.PartNumber);
            entity.HasIndex(ef => ef.Exists);
        });

        // League configuration (universal for all sports - UFC, Premier League, NBA are all leagues)
        modelBuilder.Entity<League>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Name).IsRequired().HasMaxLength(200);
            entity.Property(l => l.Sport).IsRequired().HasMaxLength(100);
            entity.Property(l => l.ExternalId).HasMaxLength(50);
            entity.Property(l => l.Country).HasMaxLength(100);
            entity.Property(l => l.Description).HasMaxLength(2000);
            entity.Property(l => l.LogoUrl).HasMaxLength(500);
            entity.Property(l => l.BannerUrl).HasMaxLength(500);
            entity.Property(l => l.PosterUrl).HasMaxLength(500);
            entity.Property(l => l.Website).HasMaxLength(500);
            entity.HasIndex(l => l.ExternalId);
            entity.HasIndex(l => l.Sport);
            entity.HasIndex(l => new { l.Name, l.Sport });
        });

        // Team configuration
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(200);
            entity.Property(t => t.Sport).IsRequired().HasMaxLength(100);
            entity.Property(t => t.ExternalId).HasMaxLength(50);
            entity.Property(t => t.ShortName).HasMaxLength(50);
            entity.Property(t => t.AlternateName).HasMaxLength(200);
            entity.Property(t => t.Country).HasMaxLength(100);
            entity.Property(t => t.Stadium).HasMaxLength(200);
            entity.Property(t => t.StadiumLocation).HasMaxLength(200);
            entity.Property(t => t.Description).HasMaxLength(2000);
            entity.Property(t => t.BadgeUrl).HasMaxLength(500);
            entity.Property(t => t.JerseyUrl).HasMaxLength(500);
            entity.Property(t => t.BannerUrl).HasMaxLength(500);
            entity.Property(t => t.Website).HasMaxLength(500);
            entity.Property(t => t.PrimaryColor).HasMaxLength(20);
            entity.Property(t => t.SecondaryColor).HasMaxLength(20);
            entity.HasOne(t => t.League)
                  .WithMany()
                  .HasForeignKey(t => t.LeagueId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(t => t.ExternalId);
            entity.HasIndex(t => t.Sport);
            entity.HasIndex(t => t.LeagueId);
        });

        // LeagueTeam join table configuration (for team-based monitoring)
        modelBuilder.Entity<LeagueTeam>(entity =>
        {
            entity.HasKey(lt => lt.Id);
            entity.HasOne(lt => lt.League)
                  .WithMany(l => l.MonitoredTeams)
                  .HasForeignKey(lt => lt.LeagueId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(lt => lt.Team)
                  .WithMany()
                  .HasForeignKey(lt => lt.TeamId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(lt => new { lt.LeagueId, lt.TeamId }).IsUnique();
            entity.HasIndex(lt => lt.Monitored);
        });

        // Player configuration
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Sport).IsRequired().HasMaxLength(100);
            entity.Property(p => p.ExternalId).HasMaxLength(50);
            entity.Property(p => p.FirstName).HasMaxLength(100);
            entity.Property(p => p.LastName).HasMaxLength(100);
            entity.Property(p => p.Nickname).HasMaxLength(100);
            entity.Property(p => p.Position).HasMaxLength(100);
            entity.Property(p => p.Nationality).HasMaxLength(100);
            entity.Property(p => p.Birthplace).HasMaxLength(200);
            entity.Property(p => p.Number).HasMaxLength(10);
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.PhotoUrl).HasMaxLength(500);
            entity.Property(p => p.ActionPhotoUrl).HasMaxLength(500);
            entity.Property(p => p.BannerUrl).HasMaxLength(500);
            entity.Property(p => p.Dominance).HasMaxLength(50);
            entity.Property(p => p.Website).HasMaxLength(500);
            entity.Property(p => p.SocialMedia).HasMaxLength(1000);
            entity.Property(p => p.WeightClass).HasMaxLength(100);
            entity.Property(p => p.Record).HasMaxLength(50);
            entity.Property(p => p.Stance).HasMaxLength(50);
            entity.Property(p => p.Weight).HasPrecision(10, 2);
            entity.Property(p => p.Reach).HasPrecision(10, 2);
            entity.HasOne(p => p.Team)
                  .WithMany()
                  .HasForeignKey(p => p.TeamId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(p => p.ExternalId);
            entity.HasIndex(p => p.Sport);
            entity.HasIndex(p => p.TeamId);
        });

        // Tag configuration
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Label).IsRequired().HasMaxLength(100);
            entity.HasIndex(t => t.Label).IsUnique();
        });

        // QualityProfile configuration
        modelBuilder.Entity<QualityProfile>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Name).IsRequired().HasMaxLength(200);
            entity.Property(q => q.Items).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<QualityItem>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<QualityItem>()
            ).Metadata.SetValueComparer(new ValueComparer<List<QualityItem>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
            entity.Property(q => q.FormatItems).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<ProfileFormatItem>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<ProfileFormatItem>()
            ).Metadata.SetValueComparer(new ValueComparer<List<ProfileFormatItem>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        });

        // CustomFormat configuration
        modelBuilder.Entity<CustomFormat>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(c => c.Name).IsUnique();

            // Serialize specifications as JSON with Fields dictionary support
            var jsonOptions = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false
            };

            entity.Property(c => c.Specifications).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, jsonOptions),
                v => System.Text.Json.JsonSerializer.Deserialize<List<FormatSpecification>>(v, jsonOptions) ?? new List<FormatSpecification>()
            ).Metadata.SetValueComparer(new ValueComparer<List<FormatSpecification>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        });

        // QualityDefinition configuration
        modelBuilder.Entity<QualityDefinition>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Title).IsRequired().HasMaxLength(100);
            entity.HasIndex(q => q.Quality).IsUnique();
            entity.Property(q => q.MinSize).HasPrecision(10, 2);
            entity.Property(q => q.MaxSize).HasPrecision(10, 2);
            entity.Property(q => q.PreferredSize).HasPrecision(10, 2);
        });

        // DelayProfile configuration
        modelBuilder.Entity<DelayProfile>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.PreferredProtocol).IsRequired().HasMaxLength(50);
            entity.Property(d => d.Tags).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<int>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<int>()
            ).Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        });


        // Release Profile configuration
        modelBuilder.Entity<ReleaseProfile>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Name).IsRequired().HasMaxLength(200);
            entity.Property(r => r.Required).HasMaxLength(2000);
            entity.Property(r => r.Ignored).HasMaxLength(2000);
            entity.Property(r => r.Preferred).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<PreferredKeyword>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<PreferredKeyword>()
            ).Metadata.SetValueComparer(new ValueComparer<List<PreferredKeyword>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
            entity.Property(r => r.Tags).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<int>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<int>()
            ).Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
            entity.Property(r => r.IndexerId).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<int>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<int>()
            ).Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        });

        // Seed quality definitions (sizes in MB per minute, converted to GB per hour for display)
        // Sizes use MB/min internally but display as MiB/h and GiB/h in the UI
        var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<QualityDefinition>().HasData(
            // Unknown quality
            new QualityDefinition { Id = 1, Quality = 0, Title = "Unknown", MinSize = 1, MaxSize = 199.9m, PreferredSize = 194.9m, Created = seedDate },

            // SD qualities
            new QualityDefinition { Id = 2, Quality = 1, Title = "SDTV", MinSize = 2, MaxSize = 100, PreferredSize = 95, Created = seedDate },
            new QualityDefinition { Id = 3, Quality = 8, Title = "WEBRip-480p", MinSize = 2, MaxSize = 100, PreferredSize = 95, Created = seedDate },
            new QualityDefinition { Id = 4, Quality = 2, Title = "WEBDL-480p", MinSize = 2, MaxSize = 100, PreferredSize = 95, Created = seedDate },
            new QualityDefinition { Id = 5, Quality = 4, Title = "DVD", MinSize = 2, MaxSize = 100, PreferredSize = 95, Created = seedDate },
            new QualityDefinition { Id = 6, Quality = 9, Title = "Bluray-480p", MinSize = 2, MaxSize = 100, PreferredSize = 95, Created = seedDate },
            new QualityDefinition { Id = 7, Quality = 16, Title = "Bluray-576p", MinSize = 2, MaxSize = 100, PreferredSize = 95, Created = seedDate },

            // HD 720p qualities
            new QualityDefinition { Id = 8, Quality = 5, Title = "HDTV-720p", MinSize = 10, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 9, Quality = 6, Title = "HDTV-1080p", MinSize = 15, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 10, Quality = 20, Title = "Raw-HD", MinSize = 4, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 11, Quality = 10, Title = "WEBRip-720p", MinSize = 10, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 12, Quality = 3, Title = "WEBDL-720p", MinSize = 10, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 13, Quality = 7, Title = "Bluray-720p", MinSize = 17.1m, MaxSize = 1000, PreferredSize = 995, Created = seedDate },

            // HD 1080p qualities
            new QualityDefinition { Id = 14, Quality = 14, Title = "WEBRip-1080p", MinSize = 15, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 15, Quality = 15, Title = "WEBDL-1080p", MinSize = 15, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 16, Quality = 11, Title = "Bluray-1080p", MinSize = 50.4m, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 17, Quality = 12, Title = "Bluray-1080p Remux", MinSize = 69.1m, MaxSize = 1000, PreferredSize = 995, Created = seedDate },

            // UHD 4K qualities
            new QualityDefinition { Id = 18, Quality = 17, Title = "HDTV-2160p", MinSize = 25, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 19, Quality = 18, Title = "WEBRip-2160p", MinSize = 25, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 20, Quality = 19, Title = "WEBDL-2160p", MinSize = 25, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 21, Quality = 13, Title = "Bluray-2160p", MinSize = 94.6m, MaxSize = 1000, PreferredSize = 995, Created = seedDate },
            new QualityDefinition { Id = 22, Quality = 21, Title = "Bluray-2160p Remux", MinSize = 187.4m, MaxSize = 1000, PreferredSize = 995, Created = seedDate }
        );

        // Import List configuration
        modelBuilder.Entity<ImportList>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Name).IsRequired().HasMaxLength(200);
            entity.Property(i => i.Url).HasMaxLength(500);
            entity.Property(i => i.RootFolderPath).HasMaxLength(500);
            entity.Property(i => i.Tags).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<int>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<int>()
            ).Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        });

        // Metadata Provider configuration
        modelBuilder.Entity<MetadataProvider>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Name).IsRequired().HasMaxLength(200);
            entity.Property(m => m.EventNfoFilename).HasMaxLength(200);
            entity.Property(m => m.EventPosterFilename).HasMaxLength(200);
            entity.Property(m => m.EventFanartFilename).HasMaxLength(200);
            entity.Property(m => m.Tags).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<int>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<int>()
            ).Metadata.SetValueComparer(new ValueComparer<List<int>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        });

        // Seed default metadata provider (Kodi)
        modelBuilder.Entity<MetadataProvider>().HasData(
            new MetadataProvider
            {
                Id = 1,
                Name = "Kodi/XBMC",
                Type = MetadataType.Kodi,
                Enabled = false,
                EventNfo = true,
                EventCardNfo = false,
                EventImages = true,
                PlayerImages = false,
                LeagueLogos = false,
                EventNfoFilename = "{Event Title}.nfo",
                EventPosterFilename = "poster.jpg",
                EventFanartFilename = "fanart.jpg",
                UseEventFolder = true,
                ImageQuality = 95,
                Tags = new List<int>(),
                Created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // Seed default quality profiles - TRaSH Guides style
        // These profiles match the "WEB-1080p (Alternative)" and "WEB-2160p (Alternative)" from TRaSH Guides
        // Quality IDs match QualityDefinition.Quality values:
        //   1=SDTV, 2=WEBDL-480p, 3=WEBDL-720p, 4=DVD, 5=HDTV-720p, 6=HDTV-1080p,
        //   7=Bluray-720p, 8=WEBRip-480p, 9=Bluray-480p, 10=WEBRip-720p, 11=Bluray-1080p,
        //   12=Bluray-1080p Remux, 13=Bluray-2160p, 14=WEBRip-1080p, 15=WEBDL-1080p,
        //   16=Bluray-576p, 17=HDTV-2160p, 18=WEBRip-2160p, 19=WEBDL-2160p, 21=Bluray-2160p Remux
        modelBuilder.Entity<QualityProfile>().HasData(
            // WEB-1080p (Alternative) - TRaSH Guides based
            // Covers: SD, DVD, HDTV 720p/1080p, WEB 480p/720p/1080p, Bluray 480p/576p/720p/1080p
            // Cutoff: WEB 1080p (Quality 15)
            new QualityProfile
            {
                Id = 1,
                Name = "WEB-1080p (Alternative)",
                IsDefault = true,
                UpgradesAllowed = true,
                CutoffQuality = 15, // WEBDL-1080p
                MinFormatScore = 0,
                // TRaSH Guides recommended scores for essential custom formats
                FormatItems = new List<ProfileFormatItem>
                {
                    new() { FormatId = 1, Score = -10000 }, // BR-DISK
                    new() { FormatId = 2, Score = -10000 }, // LQ
                    new() { FormatId = 3, Score = 5 },      // Repack/Proper
                    new() { FormatId = 4, Score = -10000 }, // x265 (HD)
                    new() { FormatId = 5, Score = -10000 }, // Upscaled
                    new() { FormatId = 6, Score = 0 },      // Scene
                    new() { FormatId = 7, Score = 10 }      // WEB-DL
                },
                Items = new List<QualityItem>
                {
                    // Quality groups in priority order (highest first)
                    // WEB 1080p group
                    new() { Name = "WEB 1080p", Quality = 0, Allowed = true, Items = new List<QualityItem>
                    {
                        new() { Name = "WEBDL-1080p", Quality = 15, Allowed = true },
                        new() { Name = "WEBRip-1080p", Quality = 14, Allowed = true }
                    }},
                    // Bluray 1080p
                    new() { Name = "Bluray-1080p", Quality = 11, Allowed = true },
                    // HDTV 1080p
                    new() { Name = "HDTV-1080p", Quality = 6, Allowed = true },
                    // WEB 720p group
                    new() { Name = "WEB 720p", Quality = 0, Allowed = true, Items = new List<QualityItem>
                    {
                        new() { Name = "WEBDL-720p", Quality = 3, Allowed = true },
                        new() { Name = "WEBRip-720p", Quality = 10, Allowed = true }
                    }},
                    // Bluray 720p
                    new() { Name = "Bluray-720p", Quality = 7, Allowed = true },
                    // HDTV 720p
                    new() { Name = "HDTV-720p", Quality = 5, Allowed = true },
                    // WEB 480p group
                    new() { Name = "WEB 480p", Quality = 0, Allowed = true, Items = new List<QualityItem>
                    {
                        new() { Name = "WEBDL-480p", Quality = 2, Allowed = true },
                        new() { Name = "WEBRip-480p", Quality = 8, Allowed = true }
                    }},
                    // Bluray 480p/576p
                    new() { Name = "Bluray-576p", Quality = 16, Allowed = true },
                    new() { Name = "Bluray-480p", Quality = 9, Allowed = true },
                    // DVD
                    new() { Name = "DVD", Quality = 4, Allowed = true },
                    // SDTV
                    new() { Name = "SDTV", Quality = 1, Allowed = true }
                }
            },
            // WEB-2160p (Alternative) - TRaSH Guides based
            // Covers: All qualities including 2160p/4K
            // Cutoff: WEB 2160p (Quality 19)
            new QualityProfile
            {
                Id = 2,
                Name = "WEB-2160p (Alternative)",
                UpgradesAllowed = true,
                CutoffQuality = 19, // WEBDL-2160p
                MinFormatScore = 0,
                // TRaSH Guides recommended scores for essential custom formats
                FormatItems = new List<ProfileFormatItem>
                {
                    new() { FormatId = 1, Score = -10000 }, // BR-DISK
                    new() { FormatId = 2, Score = -10000 }, // LQ
                    new() { FormatId = 3, Score = 5 },      // Repack/Proper
                    new() { FormatId = 4, Score = -10000 }, // x265 (HD)
                    new() { FormatId = 5, Score = -10000 }, // Upscaled
                    new() { FormatId = 6, Score = 0 },      // Scene
                    new() { FormatId = 7, Score = 10 }      // WEB-DL
                },
                Items = new List<QualityItem>
                {
                    // 2160p/4K qualities first
                    // WEB 2160p group
                    new() { Name = "WEB 2160p", Quality = 0, Allowed = true, Items = new List<QualityItem>
                    {
                        new() { Name = "WEBDL-2160p", Quality = 19, Allowed = true },
                        new() { Name = "WEBRip-2160p", Quality = 18, Allowed = true }
                    }},
                    // Bluray 2160p
                    new() { Name = "Bluray-2160p", Quality = 13, Allowed = true },
                    // HDTV 2160p
                    new() { Name = "HDTV-2160p", Quality = 17, Allowed = true },
                    // 1080p qualities
                    // WEB 1080p group
                    new() { Name = "WEB 1080p", Quality = 0, Allowed = true, Items = new List<QualityItem>
                    {
                        new() { Name = "WEBDL-1080p", Quality = 15, Allowed = true },
                        new() { Name = "WEBRip-1080p", Quality = 14, Allowed = true }
                    }},
                    // Bluray 1080p
                    new() { Name = "Bluray-1080p", Quality = 11, Allowed = true },
                    // HDTV 1080p
                    new() { Name = "HDTV-1080p", Quality = 6, Allowed = true },
                    // 720p qualities
                    // WEB 720p group
                    new() { Name = "WEB 720p", Quality = 0, Allowed = true, Items = new List<QualityItem>
                    {
                        new() { Name = "WEBDL-720p", Quality = 3, Allowed = true },
                        new() { Name = "WEBRip-720p", Quality = 10, Allowed = true }
                    }},
                    // Bluray 720p
                    new() { Name = "Bluray-720p", Quality = 7, Allowed = true },
                    // HDTV 720p
                    new() { Name = "HDTV-720p", Quality = 5, Allowed = true },
                    // 480p/SD qualities
                    // WEB 480p group
                    new() { Name = "WEB 480p", Quality = 0, Allowed = true, Items = new List<QualityItem>
                    {
                        new() { Name = "WEBDL-480p", Quality = 2, Allowed = true },
                        new() { Name = "WEBRip-480p", Quality = 8, Allowed = true }
                    }},
                    // Bluray 480p/576p
                    new() { Name = "Bluray-576p", Quality = 16, Allowed = true },
                    new() { Name = "Bluray-480p", Quality = 9, Allowed = true },
                    // DVD
                    new() { Name = "DVD", Quality = 4, Allowed = true },
                    // SDTV
                    new() { Name = "SDTV", Quality = 1, Allowed = true }
                }
            }
        );

        // Seed essential custom formats - TRaSH Guides based
        // These are the most critical formats users need to avoid bad releases
        var cfSeedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        modelBuilder.Entity<CustomFormat>().HasData(
            // BR-DISK - Rejects raw Blu-ray disc structures
            new CustomFormat
            {
                Id = 1,
                Name = "BR-DISK",
                TrashId = "85c61753-c413-4d8b-9e0d-f7f6f61e8c42",
                TrashCategory = "unwanted",
                TrashDescription = "BR-DISK refers to raw Blu-ray disc structures that are not video files",
                TrashDefaultScore = -10000,
                IsSynced = true,
                Created = cfSeedDate,
                Specifications = new List<FormatSpecification>
                {
                    new() { Id = 1, Name = "BR-DISK", Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, object> { { "value", @"(?i)\b(M2TS|BDMV|MPEG-?[24])\b" } } }
                }
            },
            // LQ (Low Quality) - Rejects low quality release groups
            new CustomFormat
            {
                Id = 2,
                Name = "LQ",
                TrashId = "90a6f9a0-8c26-40f7-b4e2-25d86656e7a8",
                TrashCategory = "unwanted",
                TrashDescription = "Releases from groups known for low quality encodes",
                TrashDefaultScore = -10000,
                IsSynced = true,
                Created = cfSeedDate,
                Specifications = new List<FormatSpecification>
                {
                    new() { Id = 2, Name = "LQ Groups", Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, object> { { "value", @"(?i)\b(YIFY|YTS|RARBG|PSA|MeGusta|SPARKS|EVO|MZABI)\b" } } }
                }
            },
            // Repack/Proper - Prefers fixed releases
            new CustomFormat
            {
                Id = 3,
                Name = "Repack/Proper",
                TrashId = "e6258996-0e87-4d8d-8c5e-4e5ab1a7c8e3",
                TrashCategory = "release-version",
                TrashDescription = "Repack or Proper releases fix issues with the original release",
                TrashDefaultScore = 5,
                IsSynced = true,
                Created = cfSeedDate,
                Specifications = new List<FormatSpecification>
                {
                    new() { Id = 3, Name = "Repack/Proper", Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, object> { { "value", @"(?i)\b(REPACK|PROPER)\b" } } }
                }
            },
            // x265 (HD) - Penalizes x265 for non-4K content (compatibility issues)
            new CustomFormat
            {
                Id = 4,
                Name = "x265 (HD)",
                TrashId = "dc98083d-a25b-4e2e-9dcc-9aa4c3c33e87",
                TrashCategory = "unwanted",
                TrashDescription = "x265/HEVC for non-4K content can have compatibility issues",
                TrashDefaultScore = -10000,
                IsSynced = true,
                Created = cfSeedDate,
                Specifications = new List<FormatSpecification>
                {
                    new() { Id = 4, Name = "x265/HEVC", Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, object> { { "value", @"(?i)\b(x265|HEVC)\b" } }, Required = true },
                    new() { Id = 5, Name = "Not 2160p", Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, object> { { "value", @"(?i)\b2160p\b" } }, Negate = true, Required = true }
                }
            },
            // Upscaled - Rejects upscaled content
            new CustomFormat
            {
                Id = 5,
                Name = "Upscaled",
                TrashId = "1b3994c5-51c6-4d4d-9c82-f5f6c46f2c3d",
                TrashCategory = "unwanted",
                TrashDescription = "Content that has been upscaled from a lower resolution",
                TrashDefaultScore = -10000,
                IsSynced = true,
                Created = cfSeedDate,
                Specifications = new List<FormatSpecification>
                {
                    new() { Id = 6, Name = "Upscaled", Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, object> { { "value", @"(?i)\b(upscale[sd]?|AI[-\. ]?enhanced)\b" } } }
                }
            },
            // Scene - Identifies scene releases
            new CustomFormat
            {
                Id = 6,
                Name = "Scene",
                TrashId = "a1b2c3d4-5e6f-7a8b-9c0d-e1f2a3b4c5d6",
                TrashCategory = "indexer-flags",
                TrashDescription = "Scene releases follow strict naming and encoding rules",
                TrashDefaultScore = 0,
                IsSynced = true,
                Created = cfSeedDate,
                Specifications = new List<FormatSpecification>
                {
                    new() { Id = 7, Name = "Scene Flag", Implementation = "IndexerFlagSpecification",
                        Fields = new Dictionary<string, object> { { "value", 1 } } }
                }
            },
            // WEB-DL - Web downloads (higher quality than WEBRip)
            new CustomFormat
            {
                Id = 7,
                Name = "WEB-DL",
                TrashId = "2b3c4d5e-6f7a-8b9c-0d1e-2f3a4b5c6d7e",
                TrashCategory = "source",
                TrashDescription = "WEB-DL is typically higher quality than WEBRip",
                TrashDefaultScore = 10,
                IsSynced = true,
                Created = cfSeedDate,
                Specifications = new List<FormatSpecification>
                {
                    new() { Id = 8, Name = "WEB-DL", Implementation = "ReleaseTitleSpecification",
                        Fields = new Dictionary<string, object> { { "value", @"(?i)\bWEB[-\. ]?DL\b" } } }
                }
            }
        );

        // AppSettings configuration
        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.HasKey(s => s.Id);
        });

        // RootFolder configuration
        modelBuilder.Entity<RootFolder>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Path).IsRequired().HasMaxLength(500);
            entity.HasIndex(r => r.Path).IsUnique();
        });

        // SystemEvent configuration
        modelBuilder.Entity<SystemEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Details).HasMaxLength(4000);
            entity.Property(e => e.User).HasMaxLength(200);
            entity.Property(e => e.RelatedEntityType).HasMaxLength(100);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.Category);
        });

        // Notification configuration
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Name).IsRequired().HasMaxLength(200);
            entity.Property(n => n.Implementation).IsRequired().HasMaxLength(100);
        });

        // AuthSession configuration
        modelBuilder.Entity<AuthSession>(entity =>
        {
            entity.HasKey(s => s.SessionId);
            entity.Property(s => s.Username).IsRequired().HasMaxLength(100);
            entity.Property(s => s.IpAddress).HasMaxLength(50);
            entity.Property(s => s.UserAgent).HasMaxLength(500);
            entity.HasIndex(s => s.ExpiresAt);
        });

        // User configuration (matches Sonarr/Radarr)
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Username).IsRequired().HasMaxLength(100);
            entity.Property(u => u.Password).IsRequired();
            entity.Property(u => u.Salt).IsRequired();
            entity.HasIndex(u => u.Username).IsUnique();
        });

        // DownloadClient configuration
        modelBuilder.Entity<DownloadClient>(entity =>
        {
            entity.HasKey(dc => dc.Id);
            entity.Property(dc => dc.Name).IsRequired().HasMaxLength(200);
            entity.Property(dc => dc.Host).IsRequired().HasMaxLength(500);
            entity.Property(dc => dc.Category).HasMaxLength(100);
        });

        // DownloadQueueItem configuration
        modelBuilder.Entity<DownloadQueueItem>(entity =>
        {
            entity.HasKey(dq => dq.Id);
            entity.Property(dq => dq.Title).IsRequired().HasMaxLength(500);
            entity.Property(dq => dq.DownloadId).IsRequired().HasMaxLength(100);
            entity.HasOne(dq => dq.Event)
                  .WithMany()
                  .HasForeignKey(dq => dq.EventId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(dq => dq.DownloadClient)
                  .WithMany()
                  .HasForeignKey(dq => dq.DownloadClientId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(dq => dq.DownloadId);
            entity.HasIndex(dq => dq.Status);
            entity.HasIndex(dq => new { dq.EventId, dq.Status }); // Composite index for efficient joins
        });

        // BlocklistItem configuration
        // Note: TorrentInfoHash is nullable to support Usenet (which has no info hash)
        // Protocol distinguishes between "Torrent" and "Usenet" entries
        modelBuilder.Entity<BlocklistItem>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Title).IsRequired().HasMaxLength(500);
            entity.Property(b => b.TorrentInfoHash).HasMaxLength(100); // Nullable for Usenet support
            entity.Property(b => b.Indexer).HasMaxLength(200);
            entity.Property(b => b.Protocol).HasMaxLength(50); // "Usenet" or "Torrent"
            entity.Property(b => b.Part).HasMaxLength(100); // For multi-part events
            entity.HasOne(b => b.Event)
                  .WithMany()
                  .HasForeignKey(b => b.EventId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(b => b.TorrentInfoHash);
            entity.HasIndex(b => b.BlockedAt);
            entity.HasIndex(b => b.EventId);
        });

        // PendingImport configuration
        modelBuilder.Entity<PendingImport>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Title).IsRequired();
            entity.Property(p => p.DownloadId).IsRequired();
            entity.Property(p => p.FilePath).IsRequired();
            entity.HasOne(p => p.DownloadClient)
                  .WithMany()
                  .HasForeignKey(p => p.DownloadClientId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.SuggestedEvent)
                  .WithMany()
                  .HasForeignKey(p => p.SuggestedEventId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(p => p.DownloadId);
            entity.HasIndex(p => p.Status);
        });

        // Indexer configuration
        modelBuilder.Entity<Indexer>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Name).IsRequired().HasMaxLength(200);
            entity.Property(i => i.Url).IsRequired().HasMaxLength(500);
            entity.Property(i => i.Categories).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>()
            ).Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
            entity.HasOne(i => i.Status)
                  .WithOne(s => s.Indexer)
                  .HasForeignKey<IndexerStatus>(s => s.IndexerId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // IndexerStatus configuration (Sonarr-style health and rate limiting)
        // Enhanced with separate query/grab backoffs (Sonarr #3132 pattern)
        modelBuilder.Entity<IndexerStatus>(entity =>
        {
            entity.HasKey(s => s.Id);

            // Legacy fields
            entity.Property(s => s.LastFailureReason).HasMaxLength(1000);
            entity.HasIndex(s => s.IndexerId).IsUnique();
            entity.HasIndex(s => s.DisabledUntil);

            // Query failure tracking
            entity.Property(s => s.LastQueryFailureReason).HasMaxLength(1000);
            entity.HasIndex(s => s.QueryDisabledUntil);

            // Grab failure tracking (separate from query)
            entity.Property(s => s.LastGrabFailureReason).HasMaxLength(1000);
            entity.HasIndex(s => s.GrabDisabledUntil);
        });

        // AppTask configuration
        modelBuilder.Entity<AppTask>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(200);
            entity.Property(t => t.CommandName).IsRequired().HasMaxLength(200);
            entity.Property(t => t.Status).IsRequired();
            entity.Property(t => t.Queued).IsRequired();
            entity.Property(t => t.Message).HasMaxLength(2000);
            entity.Property(t => t.Exception).HasMaxLength(5000);
            entity.HasIndex(t => t.Status);
            entity.HasIndex(t => t.Queued);
            entity.HasIndex(t => t.CommandName);
        });

        // MediaManagementSettings configuration
        modelBuilder.Entity<MediaManagementSettings>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.Property(m => m.StandardFileFormat).IsRequired().HasMaxLength(500);
            entity.Property(m => m.EventFolderFormat).IsRequired().HasMaxLength(500);
            entity.Property(m => m.RootFolders).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<RootFolder>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<RootFolder>()
            ).Metadata.SetValueComparer(new ValueComparer<List<RootFolder>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
        });

        // ImportHistory configuration
        modelBuilder.Entity<ImportHistory>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.SourcePath).IsRequired().HasMaxLength(1000);
            entity.Property(h => h.DestinationPath).IsRequired().HasMaxLength(1000);
            entity.Property(h => h.Quality).IsRequired().HasMaxLength(100);
            entity.Property(h => h.Warnings).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>()
            ).Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
            entity.Property(h => h.Errors).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>()
            ).Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
            // Set EventId to null when Event is deleted (keeps history like Sonarr)
            entity.HasOne(h => h.Event)
                  .WithMany()
                  .HasForeignKey(h => h.EventId)
                  .OnDelete(DeleteBehavior.SetNull);
            // Set DownloadQueueItemId to null when queue item is deleted
            entity.HasOne(h => h.DownloadQueueItem)
                  .WithMany()
                  .HasForeignKey(h => h.DownloadQueueItemId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(h => h.EventId);
            entity.HasIndex(h => h.ImportedAt);
            entity.HasIndex(h => h.DestinationPath); // For file lookup operations
        });

        // GrabHistory configuration - stores original release info for re-grabbing
        modelBuilder.Entity<GrabHistory>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Title).IsRequired().HasMaxLength(500);
            entity.Property(g => g.Indexer).IsRequired().HasMaxLength(200);
            entity.Property(g => g.DownloadUrl).IsRequired().HasMaxLength(2000);
            entity.Property(g => g.Guid).IsRequired().HasMaxLength(500);
            entity.Property(g => g.Protocol).IsRequired().HasMaxLength(50);
            entity.Property(g => g.TorrentInfoHash).HasMaxLength(100);
            entity.Property(g => g.Quality).HasMaxLength(100);
            entity.Property(g => g.Codec).HasMaxLength(50);
            entity.Property(g => g.Source).HasMaxLength(50);
            entity.Property(g => g.PartName).HasMaxLength(100);

            // Foreign key to Event - keep history when event is deleted
            entity.HasOne(g => g.Event)
                  .WithMany()
                  .HasForeignKey(g => g.EventId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Indexes for efficient queries
            entity.HasIndex(g => g.EventId);
            entity.HasIndex(g => g.GrabbedAt);
            entity.HasIndex(g => g.WasImported);
            entity.HasIndex(g => g.FileExists);
            entity.HasIndex(g => g.Guid); // For deduplication
        });

        // ============================================================================
        // RELEASE CACHE Configuration (RSS-first search strategy)
        // ============================================================================

        modelBuilder.Entity<ReleaseCache>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Title).IsRequired().HasMaxLength(500);
            entity.Property(r => r.NormalizedTitle).IsRequired().HasMaxLength(500);
            entity.Property(r => r.SearchTerms).IsRequired().HasMaxLength(2000);
            entity.Property(r => r.Guid).IsRequired().HasMaxLength(500);
            entity.Property(r => r.DownloadUrl).IsRequired().HasMaxLength(2000);
            entity.Property(r => r.InfoUrl).HasMaxLength(2000);
            entity.Property(r => r.Indexer).IsRequired().HasMaxLength(200);
            entity.Property(r => r.Protocol).IsRequired().HasMaxLength(50);
            entity.Property(r => r.TorrentInfoHash).HasMaxLength(100);
            entity.Property(r => r.Quality).HasMaxLength(100);
            entity.Property(r => r.Source).HasMaxLength(50);
            entity.Property(r => r.Codec).HasMaxLength(50);
            entity.Property(r => r.Language).HasMaxLength(50);
            entity.Property(r => r.IndexerFlags).HasMaxLength(200);
            entity.Property(r => r.SportPrefix).HasMaxLength(50);

            // Indexes for efficient querying
            entity.HasIndex(r => r.Guid).IsUnique(); // Prevent duplicates
            entity.HasIndex(r => r.NormalizedTitle); // Fast title lookups
            entity.HasIndex(r => r.ExpiresAt); // For cache cleanup
            entity.HasIndex(r => r.CachedAt); // For sorting by freshness
            entity.HasIndex(r => r.PublishDate); // For sorting by release date
            entity.HasIndex(r => r.Indexer); // Filter by indexer
            entity.HasIndex(r => r.Year); // Date-based filtering
            entity.HasIndex(r => r.SportPrefix); // Sport-based filtering
            entity.HasIndex(r => r.RoundNumber); // Round/week filtering

            // Composite index for common search patterns
            entity.HasIndex(r => new { r.SportPrefix, r.Year, r.RoundNumber });
        });

        // ============================================================================
        // IPTV/DVR Configuration
        // ============================================================================

        // IptvSource configuration
        modelBuilder.Entity<IptvSource>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.Property(s => s.Url).IsRequired().HasMaxLength(2000);
            entity.Property(s => s.Username).HasMaxLength(200);
            entity.Property(s => s.Password).HasMaxLength(500);
            entity.Property(s => s.UserAgent).HasMaxLength(500);
            entity.Property(s => s.LastError).HasMaxLength(1000);
            entity.HasIndex(s => s.Name);
            entity.HasIndex(s => s.IsActive);
        });

        // IptvChannel configuration
        modelBuilder.Entity<IptvChannel>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(500);
            entity.Property(c => c.StreamUrl).IsRequired().HasMaxLength(2000);
            entity.Property(c => c.LogoUrl).HasMaxLength(1000);
            entity.Property(c => c.Group).HasMaxLength(200);
            entity.Property(c => c.TvgId).HasMaxLength(200);
            entity.Property(c => c.TvgName).HasMaxLength(500);
            entity.Property(c => c.Country).HasMaxLength(50);
            entity.Property(c => c.Language).HasMaxLength(50);
            entity.Property(c => c.LastError).HasMaxLength(1000);
            entity.HasOne(c => c.Source)
                  .WithMany(s => s.Channels)
                  .HasForeignKey(c => c.SourceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(c => c.SourceId);
            entity.HasIndex(c => c.Name);
            entity.HasIndex(c => c.Group);
            entity.HasIndex(c => c.IsSportsChannel);
            entity.HasIndex(c => c.Status);
            entity.HasIndex(c => c.TvgId);
        });

        // ChannelLeagueMapping configuration
        modelBuilder.Entity<ChannelLeagueMapping>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasOne(m => m.Channel)
                  .WithMany(c => c.LeagueMappings)
                  .HasForeignKey(m => m.ChannelId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.League)
                  .WithMany()
                  .HasForeignKey(m => m.LeagueId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(m => new { m.ChannelId, m.LeagueId }).IsUnique();
            entity.HasIndex(m => m.IsPreferred);
        });

        // DvrRecording configuration
        modelBuilder.Entity<DvrRecording>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Title).IsRequired().HasMaxLength(500);
            entity.Property(r => r.OutputPath).HasMaxLength(1000);
            entity.Property(r => r.ErrorMessage).HasMaxLength(2000);
            entity.Property(r => r.PartName).HasMaxLength(100);
            entity.Property(r => r.Quality).HasMaxLength(100);
            entity.HasOne(r => r.Event)
                  .WithMany()
                  .HasForeignKey(r => r.EventId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(r => r.Channel)
                  .WithMany()
                  .HasForeignKey(r => r.ChannelId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(r => r.EventId);
            entity.HasIndex(r => r.ChannelId);
            entity.HasIndex(r => r.Status);
            entity.HasIndex(r => r.ScheduledStart);
            entity.HasIndex(r => r.ScheduledEnd);
        });

        // EpgSource configuration
        modelBuilder.Entity<EpgSource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Url).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.LastError).HasMaxLength(1000);
            entity.HasIndex(e => e.IsActive);
        });

        // EpgChannel configuration
        modelBuilder.Entity<EpgChannel>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.ChannelId).IsRequired().HasMaxLength(200);
            entity.Property(c => c.DisplayName).IsRequired().HasMaxLength(500);
            entity.Property(c => c.NormalizedName).HasMaxLength(500);
            entity.Property(c => c.IconUrl).HasMaxLength(1000);
            entity.HasOne(c => c.EpgSource)
                  .WithMany()
                  .HasForeignKey(c => c.EpgSourceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(c => c.EpgSourceId);
            entity.HasIndex(c => c.ChannelId);
            entity.HasIndex(c => c.NormalizedName);
        });

        // EpgProgram configuration
        modelBuilder.Entity<EpgProgram>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.ChannelId).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Title).IsRequired().HasMaxLength(500);
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.Category).HasMaxLength(200);
            entity.Property(p => p.IconUrl).HasMaxLength(1000);
            entity.HasOne(p => p.EpgSource)
                  .WithMany()
                  .HasForeignKey(p => p.EpgSourceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.MatchedEvent)
                  .WithMany()
                  .HasForeignKey(p => p.MatchedEventId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(p => p.EpgSourceId);
            entity.HasIndex(p => p.ChannelId);
            entity.HasIndex(p => p.StartTime);
            entity.HasIndex(p => p.EndTime);
            entity.HasIndex(p => p.IsSportsProgram);
            entity.HasIndex(p => p.MatchedEventId);
        });

        // ============================================================================
        // EVENT MAPPING Configuration
        // ============================================================================

        modelBuilder.Entity<EventMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SportType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LeagueId).HasMaxLength(50);
            entity.Property(e => e.LeagueName).HasMaxLength(200);
            entity.Property(e => e.Source).IsRequired().HasMaxLength(50);
            entity.Property(e => e.SessionPatternsJson).HasMaxLength(4000);
            entity.Property(e => e.QueryConfigJson).HasMaxLength(2000);

            // ReleaseNames stored as JSON array
            entity.Property(e => e.ReleaseNames).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>()
            ).Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));

            // Unique constraint: one mapping per sport/league combination
            entity.HasIndex(e => new { e.SportType, e.LeagueId }).IsUnique();
            entity.HasIndex(e => e.RemoteId);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.Priority);
            entity.HasIndex(e => e.Source);
        });

        // ============================================================================
        // SUBMITTED MAPPING REQUEST Configuration (for status tracking)
        // ============================================================================

        modelBuilder.Entity<SubmittedMappingRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SportType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LeagueName).HasMaxLength(200);
            entity.Property(e => e.ReleaseNames).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ReviewNotes).HasMaxLength(1000);

            entity.HasIndex(e => e.RemoteRequestId).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UserNotified);
        });

        // ============================================================================
        // IMPORT LIST EXCLUSION Configuration (for Maintainerr/Sonarr API compatibility)
        // ============================================================================

        modelBuilder.Entity<ImportListExclusion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.HasIndex(e => e.TvdbId).IsUnique();
        });
    }
}
