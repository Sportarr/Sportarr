using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.Fightarr;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    /// <summary>
    /// REPLACED: This now delegates to FightarrProxy for fighting events metadata
    /// Legacy SkyHook/TVDB functionality removed - Fightarr uses custom metadata API
    ///
    /// This wrapper exists to maintain compatibility with existing dependency injection
    /// All actual functionality is handled by FightarrProxy
    /// </summary>
    public class SkyHookProxy : IProvideSeriesInfo, ISearchForNewSeries
    {
        private readonly FightarrProxy _fightarrProxy;

        public SkyHookProxy(IHttpClient httpClient,
                            ISeriesService seriesService,
                            Logger logger)
        {
            // Delegate all functionality to FightarrProxy
            _fightarrProxy = new FightarrProxy(httpClient, seriesService, logger);
        }

        public Tuple<Series, List<Episode>> GetSeriesInfo(int tvdbSeriesId)
        {
            return _fightarrProxy.GetSeriesInfo(tvdbSeriesId);
        }

        public List<Series> SearchForNewSeries(string title)
        {
            return _fightarrProxy.SearchForNewSeries(title);
        }

        public List<Series> SearchForNewSeriesByImdbId(string imdbId)
        {
            return _fightarrProxy.SearchForNewSeriesByImdbId(imdbId);
        }

        public List<Series> SearchForNewSeriesByAniListId(int aniListId)
        {
            return _fightarrProxy.SearchForNewSeriesByAniListId(aniListId);
        }

        public List<Series> SearchForNewSeriesByTmdbId(int tmdbId)
        {
            return _fightarrProxy.SearchForNewSeriesByTmdbId(tmdbId);
        }

        public List<Series> SearchForNewSeriesByMyAnimeListId(int malId)
        {
            return _fightarrProxy.SearchForNewSeriesByMyAnimeListId(malId);
        }
    }
}
