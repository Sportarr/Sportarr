using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Fightarr.Resource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.Fightarr
{
    /// <summary>
    /// Fightarr Metadata Provider - Replaces SkyHook/TVDB with custom fighting events API
    /// Maps fighting events to Fightarr's Series/Episode model:
    /// - Event (UFC 300) → Series
    /// - Fight (individual matchup) → Episode
    /// - Organization (UFC, Bellator) → Network
    /// - Fighters → Actors
    /// </summary>
    public class FightarrProxy : IProvideSeriesInfo, ISearchForNewSeries
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly ISeriesService _seriesService;
        private readonly string _apiBaseUrl;

        public FightarrProxy(IHttpClient httpClient,
                            ISeriesService seriesService,
                            Logger logger)
        {
            _httpClient = httpClient;
            _seriesService = seriesService;
            _logger = logger;

            // TODO: Make this configurable via settings
            // For now, use environment variable or default to localhost
            _apiBaseUrl = Environment.GetEnvironmentVariable("FIGHTARR_API_URL") ?? "http://localhost:3000";

            _logger.Info("FightarrProxy initialized with API URL: {0}", _apiBaseUrl);
        }

        /// <summary>
        /// Get event info by ID (replaces GetSeriesInfo)
        /// </summary>
        public Tuple<Series, List<Episode>> GetSeriesInfo(int tvdbSeriesId)
        {
            try
            {
                // tvdbSeriesId is mapped to our Event ID
                var eventId = tvdbSeriesId;

                var httpRequest = new HttpRequestBuilder($"{_apiBaseUrl}/api/events/{eventId}")
                    .SetHeader("Accept", "application/json")
                    .Build();

                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpError = true;

                var httpResponse = _httpClient.Get<EventResource>(httpRequest);

                if (httpResponse.HasHttpError)
                {
                    if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new SeriesNotFoundException(eventId);
                    }
                    else
                    {
                        throw new HttpException(httpRequest, httpResponse);
                    }
                }

                var eventResource = httpResponse.Resource;
                var series = MapEventToSeries(eventResource);
                var episodes = eventResource.Fights?.Select(f => MapFightToEpisode(f, eventId)).ToList() ?? new List<Episode>();

                return new Tuple<Series, List<Episode>>(series, episodes);
            }
            catch (HttpException ex)
            {
                _logger.Error(ex, "Failed to get event info for ID {0}", tvdbSeriesId);
                throw;
            }
        }

        /// <summary>
        /// Search for events by title (replaces SearchForNewSeries)
        /// </summary>
        public List<Series> SearchForNewSeries(string title)
        {
            try
            {
                var lowerTitle = title.ToLowerInvariant();

                // Support direct event ID lookup: "event:123" or "eventid:123"
                if (lowerTitle.StartsWith("event:") || lowerTitle.StartsWith("eventid:"))
                {
                    var slug = lowerTitle.Split(':')[1].Trim();

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace) || !int.TryParse(slug, out var eventId) || eventId <= 0)
                    {
                        return new List<Series>();
                    }

                    try
                    {
                        // Check if we already have this event in our database
                        var existingSeries = _seriesService.FindByTvdbId(eventId);
                        if (existingSeries != null)
                        {
                            return new List<Series> { existingSeries };
                        }

                        return new List<Series> { GetSeriesInfo(eventId).Item1 };
                    }
                    catch (SeriesNotFoundException)
                    {
                        return new List<Series>();
                    }
                }

                // Standard search via API
                var httpRequest = new HttpRequestBuilder($"{_apiBaseUrl}/api/search")
                    .AddQueryParam("q", title.Trim())
                    .SetHeader("Accept", "application/json")
                    .Build();

                var httpResponse = _httpClient.Get<SearchResultResource>(httpRequest);

                // Map events from search results to Series
                var events = httpResponse.Resource?.Events ?? new List<EventResource>();
                return events.Select(MapEventToSeries).ToList();
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex, "Search for '{0}' failed. Unable to communicate with Fightarr API", title);
                throw new FightarrException("Search for '{0}' failed. Unable to communicate with Fightarr API. {1}", title, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Search for '{0}' failed", title);
                throw new FightarrException("Search for '{0}' failed. Invalid response received from Fightarr API. {1}", title, ex.Message);
            }
        }

        /// <summary>
        /// Search by IMDB ID - not applicable for fighting events, returns empty
        /// </summary>
        public List<Series> SearchForNewSeriesByImdbId(string imdbId)
        {
            _logger.Debug("IMDB search not supported for fighting events");
            return new List<Series>();
        }

        /// <summary>
        /// Search by AniList ID - not applicable for fighting events, returns empty
        /// </summary>
        public List<Series> SearchForNewSeriesByAniListId(int aniListId)
        {
            _logger.Debug("AniList search not supported for fighting events");
            return new List<Series>();
        }

        /// <summary>
        /// Search by TMDB ID - not applicable for fighting events, returns empty
        /// </summary>
        public List<Series> SearchForNewSeriesByTmdbId(int tmdbId)
        {
            _logger.Debug("TMDB search not supported for fighting events");
            return new List<Series>();
        }

        /// <summary>
        /// Search by MyAnimeList ID - not applicable for fighting events, returns empty
        /// </summary>
        public List<Series> SearchForNewSeriesByMyAnimeListId(int malId)
        {
            _logger.Debug("MyAnimeList search not supported for fighting events");
            return new List<Series>();
        }

        /// <summary>
        /// Map Fightarr Event to Fightarr Series model
        /// </summary>
        private Series MapEventToSeries(EventResource eventResource)
        {
            var series = new Series
            {
                // Use Event ID as TvdbId for compatibility
                TvdbId = eventResource.Id,

                Title = eventResource.Title,
                CleanTitle = Parser.Parser.CleanSeriesTitle(eventResource.Title),
                SortTitle = SeriesTitleNormalizer.Normalize(eventResource.Title, eventResource.Id),
                TitleSlug = eventResource.Slug,

                Overview = eventResource.Description,
                Status = MapEventStatus(eventResource.Status),

                // Organization becomes Network
                Network = eventResource.Organization?.Name ?? "Unknown",

                // Event date handling
                FirstAired = eventResource.EventDate,
                LastAired = eventResource.EventDate, // Events are single-day occurrences
                Year = eventResource.EventDate.Year,

                // Air time - extract from event date if available
                AirTime = eventResource.EventDate.ToString("HH:mm"),

                // Default to English for fighting events
                OriginalLanguage = Language.English,

                // Images
                Images = MapEventImages(eventResource),

                // Fighters as Actors
                Actors = new List<Actor>(), // Will be populated from fights if needed

                // Metadata
                Monitored = true,
                SeriesType = SeriesTypes.Standard, // Fighting events are episodic

                // No seasons for individual events (each event is its own series)
                Seasons = new List<Season>
                {
                    new Season
                    {
                        SeasonNumber = 1,
                        Monitored = true
                    }
                },

                // Genres based on organization type
                Genres = new List<string> { eventResource.Organization?.Type ?? "MMA", "Fighting", "Sports" },

                // Ratings - not available from API yet
                Ratings = new Ratings(),

                // Runtime - typical fight event duration (3-4 hours)
                Runtime = 180, // Default 3 hours, can be updated based on event type

                Tags = new HashSet<int>()
            };

            // Add event metadata to certification field for now
            if (eventResource.EventType.IsNotNullOrWhiteSpace())
            {
                series.Certification = eventResource.EventType; // PPV, Fight Night, etc.
            }

            return series;
        }

        /// <summary>
        /// Map Fight to Episode model
        /// </summary>
        private Episode MapFightToEpisode(FightResource fight, int eventId)
        {
            var fighter1Name = fight.Fighter1?.Name ?? "Unknown";
            var fighter2Name = fight.Fighter2?.Name ?? "Unknown";

            var episode = new Episode
            {
                // Use Fight ID as TvdbId
                TvdbId = fight.Id,
                SeriesId = eventId,

                // All fights in Season 1 (no season structure for events)
                SeasonNumber = 1,
                EpisodeNumber = fight.FightOrder,

                // Fight title: "Fighter1 vs Fighter2"
                Title = $"{fighter1Name} vs {fighter2Name}",

                // Overview includes fight details
                Overview = BuildFightOverview(fight),

                // Monitored by default
                Monitored = true,

                // No absolute episode numbers for fighting
                AbsoluteEpisodeNumber = null,

                // Ratings
                Ratings = new Ratings(),

                // Images - use fighter images if available
                Images = MapFightImages(fight),

                // Runtime - typical fight is 15-25 minutes (3-5 rounds)
                Runtime = fight.Round.HasValue ? fight.Round.Value * 5 : 15,

                // FinaleType for main event
                FinaleType = fight.IsMainEvent ? "main" : fight.IsTitleFight ? "title" : null
            };

            return episode;
        }

        /// <summary>
        /// Build detailed fight overview from fight data
        /// </summary>
        private string BuildFightOverview(FightResource fight)
        {
            var parts = new List<string>();

            // Weight class
            if (fight.WeightClass.IsNotNullOrWhiteSpace())
            {
                parts.Add($"{fight.WeightClass} bout");
            }

            // Title fight indicator
            if (fight.IsTitleFight)
            {
                parts.Add("Title Fight");
            }

            // Main event indicator
            if (fight.IsMainEvent)
            {
                parts.Add("Main Event");
            }

            // Fighter records
            if (fight.Fighter1 != null)
            {
                parts.Add($"{fight.Fighter1.Name} ({fight.Fighter1.Record})");
            }

            if (fight.Fighter2 != null)
            {
                parts.Add($"vs {fight.Fighter2.Name} ({fight.Fighter2.Record})");
            }

            // Result if available
            if (fight.Result.IsNotNullOrWhiteSpace())
            {
                parts.Add($"Result: {fight.Result}");
            }

            // Method if available
            if (fight.Method.IsNotNullOrWhiteSpace())
            {
                parts.Add($"Method: {fight.Method}");

                if (fight.Round.HasValue)
                {
                    parts.Add($"Round {fight.Round}");
                }

                if (fight.Time.IsNotNullOrWhiteSpace())
                {
                    parts.Add($"at {fight.Time}");
                }
            }

            // Notes
            if (fight.Notes.IsNotNullOrWhiteSpace())
            {
                parts.Add(fight.Notes);
            }

            return string.Join(" - ", parts);
        }

        /// <summary>
        /// Map event images to MediaCover list
        /// </summary>
        private List<MediaCover.MediaCover> MapEventImages(EventResource eventResource)
        {
            var images = new List<MediaCover.MediaCover>();

            if (eventResource.PosterUrl.IsNotNullOrWhiteSpace())
            {
                images.Add(new MediaCover.MediaCover
                {
                    RemoteUrl = eventResource.PosterUrl,
                    CoverType = MediaCoverTypes.Poster
                });
            }

            if (eventResource.BannerUrl.IsNotNullOrWhiteSpace())
            {
                images.Add(new MediaCover.MediaCover
                {
                    RemoteUrl = eventResource.BannerUrl,
                    CoverType = MediaCoverTypes.Banner
                });
            }

            return images;
        }

        /// <summary>
        /// Map fight images (fighter headshots)
        /// </summary>
        private List<MediaCover.MediaCover> MapFightImages(FightResource fight)
        {
            var images = new List<MediaCover.MediaCover>();

            // Use fighter images as episode screenshots
            if (fight.Fighter1?.ImageUrl.IsNotNullOrWhiteSpace() == true)
            {
                images.Add(new MediaCover.MediaCover
                {
                    RemoteUrl = fight.Fighter1.ImageUrl,
                    CoverType = MediaCoverTypes.Screenshot
                });
            }

            return images;
        }

        /// <summary>
        /// Map event status to series status
        /// </summary>
        private SeriesStatusType MapEventStatus(string status)
        {
            if (status.IsNullOrWhiteSpace())
            {
                return SeriesStatusType.Upcoming;
            }

            switch (status.ToLowerInvariant())
            {
                case "completed":
                    return SeriesStatusType.Ended;
                case "upcoming":
                case "announced":
                    return SeriesStatusType.Upcoming;
                case "live":
                    return SeriesStatusType.Continuing;
                case "cancelled":
                    return SeriesStatusType.Deleted;
                default:
                    return SeriesStatusType.Upcoming;
            }
        }
    }

    /// <summary>
    /// Search result wrapper
    /// </summary>
    public class SearchResultResource
    {
        public List<EventResource> Events { get; set; }
        public List<FighterResource> Fighters { get; set; }
        public List<OrganizationResource> Organizations { get; set; }
    }
}
