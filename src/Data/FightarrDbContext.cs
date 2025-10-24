using Microsoft.EntityFrameworkCore;
using Fightarr.Api.Models;

namespace Fightarr.Api.Data;

public class FightarrDbContext : DbContext
{
    public FightarrDbContext(DbContextOptions<FightarrDbContext> options) : base(options)
    {
    }

    public DbSet<Event> Events => Set<Event>();
    public DbSet<Fight> Fights => Set<Fight>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<QualityProfile> QualityProfiles => Set<QualityProfile>();
    public DbSet<CustomFormat> CustomFormats => Set<CustomFormat>();
    public DbSet<FormatSpecification> FormatSpecifications => Set<FormatSpecification>();
    public DbSet<ProfileFormatItem> ProfileFormatItems => Set<ProfileFormatItem>();
    public DbSet<QualityDefinition> QualityDefinitions => Set<QualityDefinition>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<RootFolder> RootFolders => Set<RootFolder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<User> Users => Set<User>();
    public DbSet<DownloadClient> DownloadClients => Set<DownloadClient>();
    public DbSet<DownloadQueueItem> DownloadQueue => Set<DownloadQueueItem>();
    public DbSet<Indexer> Indexers => Set<Indexer>();
    public DbSet<AppTask> Tasks => Set<AppTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Event configuration
        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Organization).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Images).HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            );
            entity.HasIndex(e => e.EventDate);
            entity.HasIndex(e => e.Organization);
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
            );
            entity.Property(q => q.FormatItems).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<ProfileFormatItem>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<ProfileFormatItem>()
            );
        });

        // CustomFormat configuration
        modelBuilder.Entity<CustomFormat>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(200);
            entity.HasIndex(c => c.Name).IsUnique();
            entity.Property(c => c.Specifications).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<FormatSpecification>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<FormatSpecification>()
            );
        });

        // QualityDefinition configuration
        modelBuilder.Entity<QualityDefinition>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(q => q.Name).IsUnique();
        });

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

        // Indexer configuration
        modelBuilder.Entity<Indexer>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Name).IsRequired().HasMaxLength(200);
            entity.Property(i => i.Url).IsRequired().HasMaxLength(500);
            entity.Property(i => i.Categories).HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>()
            );
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
    }
}
