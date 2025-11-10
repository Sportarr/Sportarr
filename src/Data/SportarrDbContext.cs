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
    public DbSet<Fight> Fights => Set<Fight>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<Team> Teams => Set<Team>();
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
    public DbSet<Indexer> Indexers => Set<Indexer>();
    public DbSet<AppTask> Tasks => Set<AppTask>();
    public DbSet<MediaManagementSettings> MediaManagementSettings => Set<MediaManagementSettings>();
    public DbSet<ImportHistory> ImportHistories => Set<ImportHistory>();
    public DbSet<DelayProfile> DelayProfiles => Set<DelayProfile>();
    public DbSet<ReleaseProfile> ReleaseProfiles => Set<ReleaseProfile>();
    public DbSet<ImportList> ImportLists => Set<ImportList>();
    public DbSet<MetadataProvider> MetadataProviders => Set<MetadataProvider>();
    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
    public DbSet<RemotePathMapping> RemotePathMappings => Set<RemotePathMapping>();

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
        });

        // Fight configuration
        modelBuilder.Entity<Fight>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Fighter1).IsRequired().HasMaxLength(200);
            entity.Property(f => f.Fighter2).IsRequired().HasMaxLength(200);
            entity.HasOne(f => f.Event)
                  .WithMany(e => e.Fights)
                  .HasForeignKey(f => f.EventId)
                  .OnDelete(DeleteBehavior.Cascade);
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

        // Seed default delay profile
        modelBuilder.Entity<DelayProfile>().HasData(
            new DelayProfile
            {
                Id = 1,
                Order = 1,
                PreferredProtocol = "Usenet",
                UsenetDelay = 0,
                TorrentDelay = 0,
                BypassIfHighestQuality = false,
                BypassIfAboveCustomFormatScore = false,
                MinimumCustomFormatScore = 0,
                Tags = new List<int>(),
                Created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

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

        // Seed default quality profiles
        modelBuilder.Entity<QualityProfile>().HasData(
            new QualityProfile
            {
                Id = 1,
                Name = "HD 1080p",
                Items = new List<QualityItem>
                {
                    new() { Name = "1080p", Quality = 1080, Allowed = true },
                    new() { Name = "720p", Quality = 720, Allowed = false },
                    new() { Name = "480p", Quality = 480, Allowed = false }
                }
            },
            new QualityProfile
            {
                Id = 2,
                Name = "Any",
                IsDefault = true,
                Items = new List<QualityItem>
                {
                    new() { Name = "1080p", Quality = 1080, Allowed = true },
                    new() { Name = "720p", Quality = 720, Allowed = true },
                    new() { Name = "480p", Quality = 480, Allowed = true }
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
        });

        // BlocklistItem configuration
        modelBuilder.Entity<BlocklistItem>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Title).IsRequired().HasMaxLength(500);
            entity.Property(b => b.TorrentInfoHash).IsRequired().HasMaxLength(100);
            entity.Property(b => b.Indexer).HasMaxLength(200);
            entity.HasOne(b => b.Event)
                  .WithMany()
                  .HasForeignKey(b => b.EventId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(b => b.TorrentInfoHash);
            entity.HasIndex(b => b.BlockedAt);
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
            entity.HasIndex(h => h.EventId);
            entity.HasIndex(h => h.ImportedAt);
        });
    }
}
