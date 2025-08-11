using System;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers
{
    public interface ISeedConfigProvider
    {
        TorrentSeedConfiguration GetSeedConfiguration(RemoteEpisode release);
        TorrentSeedConfiguration GetSeedConfiguration(int indexerId, bool fullSeason);
    }

    public class SeedConfigProvider : ISeedConfigProvider
    {
        private readonly ICachedIndexerSettingsProvider _cachedIndexerSettingsProvider;

        public SeedConfigProvider(ICachedIndexerSettingsProvider cachedIndexerSettingsProvider)
        {
            _cachedIndexerSettingsProvider = cachedIndexerSettingsProvider;
        }

        public TorrentSeedConfiguration GetSeedConfiguration(RemoteEpisode remoteEpisode)
        {
            if (remoteEpisode.Release.DownloadProtocol != DownloadProtocol.Torrent)
            {
                return null;
            }

            if (remoteEpisode.Release.IndexerId == 0)
            {
                return null;
            }

            return GetSeedConfiguration(remoteEpisode.Release.IndexerId, remoteEpisode.ParsedEpisodeInfo.FullSeason);
        }

        public TorrentSeedConfiguration GetSeedConfiguration(int indexerId, bool fullSeason)
        {
            if (indexerId == 0)
            {
                return null;
            }

            var settings = _cachedIndexerSettingsProvider.GetSettings(indexerId);
            var seedCriteria = settings?.SeedCriteriaSettings;

            if (seedCriteria == null)
            {
                return null;
            }

            var useSeasonPackSeedGoal = (SeasonPackSeedGoal)seedCriteria.SeasonPackSeedGoal == SeasonPackSeedGoal.UseSeasonPackSeedGoal;

            var seedConfig = new TorrentSeedConfiguration
            {
                Ratio = (fullSeason && useSeasonPackSeedGoal)
                    ? seedCriteria.SeasonPackSeedRatio
                    : seedCriteria.SeedRatio
            };

            var seedTime = (fullSeason && useSeasonPackSeedGoal) ? seedCriteria.SeasonPackSeedTime : seedCriteria.SeedTime;
            if (seedTime.HasValue)
            {
                seedConfig.SeedTime = TimeSpan.FromMinutes(seedTime.Value);
            }

            return seedConfig;
        }
    }
}
