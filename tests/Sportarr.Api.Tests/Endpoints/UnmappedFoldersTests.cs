using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>
/// The unlinked folders list of a root folder shows only the folders that a
/// Library Import scan cannot link to a league. It uses the same league rule
/// as the scan.
/// </summary>
public sealed class UnmappedFoldersTests : IDisposable
{
    private readonly string _directory;

    public UnmappedFoldersTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "sportarr-unmapped-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static League F1() => new() { Name = "Formula 1", Sport = "Motorsport" };

    private static League Ufc() => new()
    {
        Name = "Ultimate Fighting Championship", Sport = "Fighting", AlternateName = "UFC",
    };

    private async Task<List<string>> UnmappedNamesAsync(string rootPath, params League[] leagues)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<SportarrDbContext>(options =>
            options.UseSqlite("Data Source=" + Path.Combine(_directory, "test.db") + ";Pooling=False"));
        builder.Services.AddSingleton<DiskSpaceService>();
        builder.Services.AddSingleton<NotificationService>(_ => null!);
        await using var app = builder.Build();
        app.MapRootFolderAndNotificationEndpoints();

        int rootId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Leagues.AddRange(leagues);
            var root = new RootFolder { Path = rootPath };
            db.RootFolders.Add(root);
            await db.SaveChangesAsync();
            rootId = root.Id;
        }
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync($"/api/rootfolder/{rootId}/unmappedfolders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("accessible").GetBoolean());
        return body.GetProperty("unmapped").EnumerateArray()
            .Select(u =>
            {
                // The settings page reads name and path from each row.
                Assert.Equal(JsonValueKind.String, u.GetProperty("path").ValueKind);
                return u.GetProperty("name").GetString()!;
            })
            .ToList();
    }

    private string MakeFolders(string root, params string[] children)
    {
        var rootPath = Path.Combine(_directory, root);
        Directory.CreateDirectory(rootPath);
        foreach (var child in children)
            Directory.CreateDirectory(Path.Combine(rootPath, child));
        return rootPath;
    }

    [Fact]
    public async Task ARootNamedAfterALeagueListsNoSeasonFolder()
    {
        var root = MakeFolders("Formula 1", "Season 2024");

        var names = await UnmappedNamesAsync(root, F1());

        Assert.Empty(names);
    }

    [Fact]
    public async Task AFolderNamedByALeaguesAlternateNameIsNotListed()
    {
        var root = MakeFolders("media", "UFC");

        var names = await UnmappedNamesAsync(root, Ufc());

        Assert.Empty(names);
    }

    [Fact]
    public async Task AFolderNamedByTheShortFormOfALeagueIsNotListed()
    {
        var root = MakeFolders("media", "F1");

        var names = await UnmappedNamesAsync(root, F1());

        Assert.Empty(names);
    }

    [Fact]
    public async Task AFolderForALeagueYouDoNotHaveIsStillListed()
    {
        var root = MakeFolders("media", "Formula 1", "UFC", "NBA");

        var names = await UnmappedNamesAsync(root, F1(), Ufc());

        Assert.Equal(new[] { "NBA" }, names);
    }
}
