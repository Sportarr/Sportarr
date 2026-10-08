using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Library imports used to store a custom format score of 0, so every release
/// with a positive score looked like an upgrade over the file. The rescore
/// gives those files the score their names earn.
/// </summary>
public class LibraryFileFormatScoresTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly SportarrDbContext _db;
    private readonly CustomFormatService _customFormats =
        new(new MediaFileParser(Mock.Of<ILogger<MediaFileParser>>()));

    public LibraryFileFormatScoresTests() => _db = NewContext();

    /// <summary>A context with nothing tracked, as a startup chore would get.</summary>
    private SportarrDbContext NewContext() => new(
        new DbContextOptionsBuilder<SportarrDbContext>().UseInMemoryDatabase(_dbName).Options);

    public void Dispose() => _db.Dispose();

    private Event SeedEventScoringBetterAt50()
    {
        var format = new CustomFormat
        {
            Name = "Better",
            Specifications = new List<FormatSpecification>
            {
                new()
                {
                    Name = "Title", Implementation = "ReleaseTitle", Required = true,
                    Fields = new Dictionary<string, object> { { "value", "Better" } },
                },
            },
        };
        _db.CustomFormats.Add(format);
        _db.SaveChanges();
        _db.QualityProfiles.Add(new QualityProfile
        {
            Name = "Default", IsDefault = true,
            FormatItems = new List<ProfileFormatItem> { new() { FormatId = format.Id, Score = 50 } },
        });
        var nfl = new League { Name = "NFL", Sport = "American Football", ExternalId = "lg-000032" };
        _db.Leagues.Add(nfl);
        _db.SaveChanges();
        var evt = new Event
        {
            Title = "Carolina Panthers vs Cleveland Browns", Sport = "American Football", Season = "2025",
            EventDate = new DateTime(2025, 8, 8), LeagueId = nfl.Id, ExternalId = "ev-312923",
            SeasonNumber = 2025, EpisodeNumber = 6, Status = "completed", HasFile = true, Monitored = true,
        };
        _db.Events.Add(evt);
        _db.SaveChanges();
        return evt;
    }

    [Fact]
    public async Task AnUnscoredLibraryFileGetsTheScoreItsNameEarns()
    {
        var evt = SeedEventScoringBetterAt50();
        _db.EventFiles.Add(new EventFile
        {
            EventId = evt.Id, Quality = "WEBDL-2160p", Exists = true,
            FilePath = "/data/NFL/Season 2025/NFL - S2025E06 - Better Copy - WEBDL-2160p.mkv",
        });
        _db.SaveChanges();

        await LibraryFileFormatScores.RescoreUnscoredAsync(_db, _customFormats);

        _db.EventFiles.Single().CustomFormatScore.Should().Be(50,
            "a stored score of 0 makes every scored release look like an upgrade over this file");
    }

    [Fact]
    public async Task AGrabbedFileKeepsTheScoreItsReleaseEarned()
    {
        var evt = SeedEventScoringBetterAt50();
        _db.EventFiles.Add(new EventFile
        {
            EventId = evt.Id, Quality = "WEBDL-2160p", Exists = true,
            FilePath = "/data/NFL/Season 2025/NFL - S2025E06 - Better Copy - WEBDL-2160p.mkv",
            ReleaseTitle = "NFL.2025.Week.6.Panthers.vs.Browns.2160p.WEB-DL",
        });
        _db.SaveChanges();

        await LibraryFileFormatScores.RescoreUnscoredAsync(_db, _customFormats);

        _db.EventFiles.Single().CustomFormatScore.Should().Be(0,
            "a grabbed file's score was set from its release at grab time");
    }

    [Fact]
    public async Task AFileIsScoredAgainstItsLeaguesProfile()
    {
        var evt = SeedEventScoringBetterAt50();
        _db.QualityProfiles.Single().IsDefault = false;
        var plain = new QualityProfile { Name = "Plain", IsDefault = true };
        _db.QualityProfiles.Add(plain);
        _db.SaveChanges();
        _db.Leagues.Single().QualityProfileId = _db.QualityProfiles.Single(p => p.Name == "Default").Id;
        _db.EventFiles.Add(new EventFile
        {
            EventId = evt.Id, Quality = "WEBDL-2160p", Exists = true,
            FilePath = "/data/NFL/Season 2025/NFL - S2025E06 - Better Copy - WEBDL-2160p.mkv",
        });
        _db.SaveChanges();

        using (var fresh = NewContext())
        {
            await LibraryFileFormatScores.RescoreUnscoredAsync(fresh, _customFormats);
        }

        using var check = NewContext();
        check.EventFiles.Single().CustomFormatScore.Should().Be(50,
            "the league's profile governs the event, not the default one");
    }

    [Fact]
    public async Task ADvrRecordingKeepsTheScoreTheDvrGaveIt()
    {
        var evt = SeedEventScoringBetterAt50();
        _db.EventFiles.Add(new EventFile
        {
            EventId = evt.Id, Quality = "HDTV-720p", Source = "IPTV", Exists = true,
            FilePath = "/data/NFL/Season 2025/NFL - S2025E06 - Better Copy - HDTV-720p.ts",
            OriginalTitle = "DVR Recording - Better Sports HD",
        });
        _db.SaveChanges();

        await LibraryFileFormatScores.RescoreUnscoredAsync(_db, _customFormats);

        _db.EventFiles.Single().CustomFormatScore.Should().Be(0,
            "a recording was scored from the DVR's probe title, not from a name");
    }
}
