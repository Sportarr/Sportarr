using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sportarr.Api.Data;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Endpoints;

public sealed class QualityProfileCloneTests
{
    [Fact]
    public async Task CreatedCopyKeepsScoresButDoesNotInheritAutomaticSync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sportarr-profile-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<SportarrDbContext>(options =>
                options.UseSqlite("Data Source=" + Path.Combine(directory, "test.db") + ";Pooling=False"));
            builder.Services.AddSingleton<ReleaseEvaluator>(_ => null!);
            await using var app = builder.Build();
            app.MapTagAndQualityProfileEndpoints();
            int formatId;
            int sourceId;
            string sourceName;
            string? sourceTrashId;
            int? sourceCutoff;
            int[] sourceScores;
            using (var scope = app.Services.CreateScope())
            {
                var setupDb = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
                await setupDb.Database.EnsureCreatedAsync();
                var format = new CustomFormat { Name = "German Tier 01" };
                setupDb.CustomFormats.Add(format);
                await setupDb.SaveChangesAsync();
                formatId = format.Id;
                var source = await setupDb.QualityProfiles.FirstAsync();
                sourceId = source.Id;
                sourceName = source.Name;
                sourceTrashId = source.TrashId;
                sourceCutoff = source.CutoffQuality;
                sourceScores = source.FormatItems.Select(item => item.Score).ToArray();
            }
            await app.StartAsync();

            var copied = new QualityProfile
            {
                Id = sourceId,
                Name = "EPL English",
                IsSynced = true,
                IsCustomized = true,
                TrashId = "source-profile",
                TrashScoreSet = "default",
                LastTrashScoreSync = DateTime.UtcNow,
                Items = [new QualityItem { Name = "WEBDL-1080p", Quality = 15, Allowed = true }],
                CutoffQuality = 15,
                FormatItems = [new ProfileFormatItem { FormatId = formatId, Score = 1500 }]
            };
            var response = await app.GetTestClient().PostAsJsonAsync("/api/qualityprofile", copied);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<QualityProfile>();
            Assert.NotNull(created);
            Assert.Equal(1500, Assert.Single(created.FormatItems).Score);
            Assert.Equal(15, Assert.Single(created.Items).Quality);
            Assert.Equal(15, created.CutoffQuality);
            Assert.NotEqual(sourceId, created.Id);
            Assert.False(created.IsSynced);
            Assert.False(created.IsCustomized);
            Assert.Null(created.TrashId);
            Assert.Null(created.TrashScoreSet);
            Assert.Null(created.LastTrashScoreSync);

            using var scope2 = app.Services.CreateScope();
            var db = scope2.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var saved = await db.QualityProfiles.SingleAsync(p => p.Name == "EPL English");
            Assert.False(saved.IsSynced);
            Assert.Equal(1500, Assert.Single(saved.FormatItems).Score);
            var unchangedSource = await db.QualityProfiles.SingleAsync(p => p.Id == sourceId);
            Assert.Equal(sourceName, unchangedSource.Name);
            Assert.True(unchangedSource.IsSynced);
            Assert.Equal(sourceTrashId, unchangedSource.TrashId);
            Assert.Equal(sourceCutoff, unchangedSource.CutoffQuality);
            Assert.Equal(sourceScores, unchangedSource.FormatItems.Select(item => item.Score).ToArray());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
