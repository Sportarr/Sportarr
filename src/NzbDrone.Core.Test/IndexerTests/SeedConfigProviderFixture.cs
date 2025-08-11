using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Torznab;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class SeedConfigProviderFixture : CoreTest<SeedConfigProvider>
    {
        [Test]
        public void should_not_return_config_for_non_existent_indexer()
        {
            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                  .Setup(v => v.GetSettings(It.IsAny<int>()))
                  .Returns<CachedIndexerSettings>(null);

            var result = Subject.GetSeedConfiguration(new RemoteEpisode
            {
                Release = new ReleaseInfo
                {
                    DownloadProtocol = DownloadProtocol.Torrent,
                    IndexerId = 0
                }
            });

            result.Should().BeNull();
        }

        [Test]
        public void should_not_return_config_for_invalid_indexer()
        {
            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                  .Setup(v => v.GetSettings(It.IsAny<int>()))
                  .Returns<CachedIndexerSettings>(null);

            var result = Subject.GetSeedConfiguration(new RemoteEpisode
            {
                Release = new ReleaseInfo
                {
                    DownloadProtocol = DownloadProtocol.Torrent,
                    IndexerId = 1
                },
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    FullSeason = true
                }
            });

            result.Should().BeNull();
        }

        [Test]
        public void should_return_season_time_for_season_packs()
        {
            var settings = new TorznabSettings();
            settings.SeedCriteria.SeasonPackSeedGoal = (int)SeasonPackSeedGoal.UseSeasonPackSeedGoal;
            settings.SeedCriteria.SeasonPackSeedTime = 10;

            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                     .Setup(v => v.GetSettings(It.IsAny<int>()))
                     .Returns(new CachedIndexerSettings
                     {
                         FailDownloads = new HashSet<FailDownloads> { FailDownloads.Executables },
                         SeedCriteriaSettings = settings.SeedCriteria
                     });

            var result = Subject.GetSeedConfiguration(new RemoteEpisode
            {
                Release = new ReleaseInfo
                {
                    DownloadProtocol = DownloadProtocol.Torrent,
                    IndexerId = 1
                },
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    FullSeason = true
                }
            });

            result.Should().NotBeNull();
            result.SeedTime.Should().Be(TimeSpan.FromMinutes(10));
        }

        [Test]
        public void should_return_season_ratio_for_season_packs_when_set()
        {
            var settings = new TorznabSettings();
            settings.SeedCriteria.SeasonPackSeedGoal = (int)SeasonPackSeedGoal.UseSeasonPackSeedGoal;
            settings.SeedCriteria.SeedRatio = 1.0;
            settings.SeedCriteria.SeasonPackSeedRatio = 10.0;

            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                .Setup(v => v.GetSettings(It.IsAny<int>()))
                .Returns(new CachedIndexerSettings
                {
                    FailDownloads = new HashSet<FailDownloads> { FailDownloads.Executables },
                    SeedCriteriaSettings = settings.SeedCriteria
                });

            var result = Subject.GetSeedConfiguration(new RemoteEpisode
            {
                Release = new ReleaseInfo
                {
                    DownloadProtocol = DownloadProtocol.Torrent,
                    IndexerId = 1
                },
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    FullSeason = true
                }
            });

            result.Should().NotBeNull();
            result.Ratio.Should().Be(10.0);
        }

        [Test]
        public void should_return_standard_ratio_for_season_packs_when_not_set()
        {
            var settings = new TorznabSettings();
            settings.SeedCriteria.SeasonPackSeedGoal = (int)SeasonPackSeedGoal.UseStandardSeedGoal;
            settings.SeedCriteria.SeedRatio = 1.0;
            settings.SeedCriteria.SeasonPackSeedRatio = 10.0;

            Mocker.GetMock<ICachedIndexerSettingsProvider>()
                .Setup(v => v.GetSettings(It.IsAny<int>()))
                .Returns(new CachedIndexerSettings
                {
                    FailDownloads = new HashSet<FailDownloads> { FailDownloads.Executables },
                    SeedCriteriaSettings = settings.SeedCriteria
                });

            var result = Subject.GetSeedConfiguration(new RemoteEpisode
            {
                Release = new ReleaseInfo
                {
                    DownloadProtocol = DownloadProtocol.Torrent,
                    IndexerId = 1
                },
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    FullSeason = true
                }
            });

            result.Should().NotBeNull();
            result.Ratio.Should().Be(1.0);
        }
    }
}
