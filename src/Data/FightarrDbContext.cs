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
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<RootFolder> RootFolders => Set<RootFolder>();
    public DbSet<Notification> Notifications => Set<Notification>();

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
    }
}
