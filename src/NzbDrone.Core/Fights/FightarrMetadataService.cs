using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Fights
{
    /// <summary>
    /// Service for fetching fight metadata from Fightarr API
    /// </summary>
    public interface IFightarrMetadataService
    {
        Task<List<FightEvent>> GetUpcomingEvents(string organizationSlug = null);
        Task<FightEvent> GetEvent(int eventId);
        Task<List<FightEvent>> SearchEvents(string query);
        Task<Fighter> GetFighter(int fighterId);
    }

    public class FightarrMetadataService : IFightarrMetadataService
    {
        private const string API_BASE_URL = "https://fightarr.com";
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public FightarrMetadataService(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        private string GetApiBaseUrl()
        {
            return API_BASE_URL;
        }

        public async Task<List<FightEvent>> GetUpcomingEvents(string organizationSlug = null)
        {
            try
            {
                var apiBaseUrl = GetApiBaseUrl();
                var url = $"{apiBaseUrl}/api/events?upcoming=true&limit=50";
                if (!string.IsNullOrEmpty(organizationSlug))
                {
                    url += $"&organization={organizationSlug}";
                }

                var request = new HttpRequest(url);
                var response = await _httpClient.GetAsync(request);

                var apiResponse = JsonSerializer.Deserialize<FightarrApiEventsResponse>(response.Content);

                return apiResponse.Events.Select(MapApiEventToFightEvent).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch upcoming events from Fightarr API");
                return new List<FightEvent>();
            }
        }

        public async Task<FightEvent> GetEvent(int eventId)
        {
            try
            {
                var apiBaseUrl = GetApiBaseUrl();
                var url = $"{apiBaseUrl}/api/events/{eventId}";
                var request = new HttpRequest(url);
                var response = await _httpClient.GetAsync(request);

                var apiEvent = JsonSerializer.Deserialize<FightarrApiEvent>(response.Content);

                return MapApiEventDetailToFightEvent(apiEvent);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to fetch event {eventId} from Fightarr API");
                return null;
            }
        }

        public async Task<List<FightEvent>> SearchEvents(string query)
        {
            try
            {
                var apiBaseUrl = GetApiBaseUrl();
                var url = $"{apiBaseUrl}/api/search?q={Uri.EscapeDataString(query)}&type=events";
                var request = new HttpRequest(url);
                var response = await _httpClient.GetAsync(request);

                var apiResponse = JsonSerializer.Deserialize<FightarrApiEventsResponse>(response.Content);

                return apiResponse.Events.Select(MapApiEventToFightEvent).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to search events with query '{query}'");
                return new List<FightEvent>();
            }
        }

        public async Task<Fighter> GetFighter(int fighterId)
        {
            try
            {
                var apiBaseUrl = GetApiBaseUrl();
                var url = $"{apiBaseUrl}/api/fighters/{fighterId}";
                var request = new HttpRequest(url);
                var response = await _httpClient.GetAsync(request);

                var apiFighter = JsonSerializer.Deserialize<FightarrApiFighter>(response.Content);

                return MapApiFighterToFighter(apiFighter);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to fetch fighter {fighterId}");
                return null;
            }
        }

        private FightEvent MapApiEventToFightEvent(FightarrApiEvent apiEvent)
        {
            return new FightEvent
            {
                FightarrEventId = apiEvent.Id,
                OrganizationId = apiEvent.OrganizationId,
                OrganizationName = apiEvent.Organization?.Name,
                OrganizationType = apiEvent.Organization?.Type,
                Title = apiEvent.Title,
                CleanTitle = apiEvent.Title.CleanSeriesTitle(),
                Slug = apiEvent.Slug,
                EventNumber = apiEvent.EventNumber,
                EventDate = apiEvent.EventDate,
                EventType = apiEvent.EventType,
                Location = apiEvent.Location,
                Venue = apiEvent.Venue,
                Broadcaster = apiEvent.Broadcaster,
                Overview = apiEvent.Description,
                Status = apiEvent.Status,
                Monitored = false,
                Images = MapApiImages(apiEvent),
            };
        }

        private FightEvent MapApiEventDetailToFightEvent(FightarrApiEvent apiEvent)
        {
            var fightEvent = MapApiEventToFightEvent(apiEvent);

            // Map fights to fight cards (Early Prelims, Prelims, Main Card)
            if (apiEvent.Fights != null && apiEvent.Fights.Any())
            {
                fightEvent.FightCards = CreateFightCardsFromFights(apiEvent, fightEvent);
            }

            return fightEvent;
        }

        private List<FightCard> CreateFightCardsFromFights(FightarrApiEvent apiEvent, FightEvent fightEvent)
        {
            var fightCards = new List<FightCard>();
            var allFights = apiEvent.Fights.OrderBy(f => f.FightOrder).ToList();

            // Determine fight distribution based on total fights
            var totalFights = allFights.Count;

            // Logic: Distribute fights into 3 cards
            // Main Card: top 5 fights (or fewer if less than 5)
            // Prelims: next 4-5 fights
            // Early Prelims: remaining fights

            var mainCardCount = Math.Min(5, totalFights);
            var prelimsCount = Math.Min(5, Math.Max(0, totalFights - mainCardCount));
            var earlyPrelimsCount = Math.Max(0, totalFights - mainCardCount - prelimsCount);

            // Create Main Card (Episode 3)
            if (mainCardCount > 0)
            {
                var mainCardFights = allFights.Take(mainCardCount).ToList();
                fightCards.Add(new FightCard
                {
                    FightEventId = fightEvent.Id,
                    CardNumber = 3,
                    CardSection = "Main Card",
                    Title = $"{fightEvent.Title} - Main Card",
                    AirDateUtc = fightEvent.EventDate,
                    Monitored = true,
                    Fights = mainCardFights.Select(MapApiFightToFight).ToList()
                });
            }

            // Create Prelims (Episode 2)
            if (prelimsCount > 0)
            {
                var prelimsFights = allFights.Skip(mainCardCount).Take(prelimsCount).ToList();
                fightCards.Add(new FightCard
                {
                    FightEventId = fightEvent.Id,
                    CardNumber = 2,
                    CardSection = "Prelims",
                    Title = $"{fightEvent.Title} - Prelims",
                    AirDateUtc = fightEvent.EventDate.AddHours(-2), // Typically 2 hours before main card
                    Monitored = true,
                    Fights = prelimsFights.Select(MapApiFightToFight).ToList()
                });
            }

            // Create Early Prelims (Episode 1)
            if (earlyPrelimsCount > 0)
            {
                var earlyPrelimsFights = allFights.Skip(mainCardCount + prelimsCount).ToList();
                fightCards.Add(new FightCard
                {
                    FightEventId = fightEvent.Id,
                    CardNumber = 1,
                    CardSection = "Early Prelims",
                    Title = $"{fightEvent.Title} - Early Prelims",
                    AirDateUtc = fightEvent.EventDate.AddHours(-4), // Typically 4 hours before main card
                    Monitored = false, // Early prelims often not monitored by default
                    Fights = earlyPrelimsFights.Select(MapApiFightToFight).ToList()
                });
            }

            return fightCards;
        }

        private Fight MapApiFightToFight(FightarrApiFight apiFight)
        {
            return new Fight
            {
                FightarrFightId = apiFight.Id,
                Fighter1Id = apiFight.Fighter1Id,
                Fighter1Name = apiFight.Fighter1?.Name,
                Fighter1Record = $"{apiFight.Fighter1?.Wins}-{apiFight.Fighter1?.Losses}-{apiFight.Fighter1?.Draws}",
                Fighter2Id = apiFight.Fighter2Id,
                Fighter2Name = apiFight.Fighter2?.Name,
                Fighter2Record = $"{apiFight.Fighter2?.Wins}-{apiFight.Fighter2?.Losses}-{apiFight.Fighter2?.Draws}",
                WeightClass = apiFight.WeightClass,
                IsTitleFight = apiFight.IsTitleFight,
                IsMainEvent = apiFight.IsMainEvent,
                FightOrder = apiFight.FightOrder,
                Result = apiFight.Result,
                Method = apiFight.Method,
                Round = apiFight.Round,
                Time = apiFight.Time,
                Referee = apiFight.Referee,
                Notes = apiFight.Notes
            };
        }

        private Fighter MapApiFighterToFighter(FightarrApiFighter apiFighter)
        {
            return new Fighter
            {
                FightarrFighterId = apiFighter.Id,
                Name = apiFighter.Name,
                Slug = apiFighter.Slug,
                Nickname = apiFighter.Nickname,
                WeightClass = apiFighter.WeightClass,
                Nationality = apiFighter.Nationality,
                Wins = apiFighter.Wins,
                Losses = apiFighter.Losses,
                Draws = apiFighter.Draws,
                NoContests = apiFighter.NoContests,
                BirthDate = apiFighter.BirthDate,
                Height = apiFighter.Height,
                Reach = apiFighter.Reach,
                ImageUrl = apiFighter.ImageUrl,
                Bio = apiFighter.Bio,
                IsActive = apiFighter.IsActive
            };
        }

        private List<MediaCover.MediaCover> MapApiImages(FightarrApiEvent apiEvent)
        {
            var images = new List<MediaCover.MediaCover>();

            if (!string.IsNullOrEmpty(apiEvent.PosterUrl))
            {
                images.Add(new MediaCover.MediaCover
                {
                    CoverType = MediaCover.MediaCoverTypes.Poster,
                    Url = apiEvent.PosterUrl
                });
            }

            if (!string.IsNullOrEmpty(apiEvent.BannerUrl))
            {
                images.Add(new MediaCover.MediaCover
                {
                    CoverType = MediaCover.MediaCoverTypes.Banner,
                    Url = apiEvent.BannerUrl
                });
            }

            return images;
        }
    }

    // API Response Models

    public class FightarrApiEventsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("events")]
        public List<FightarrApiEvent> Events { get; set; }
    }

    public class FightarrApiEvent
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("organizationId")]
        public int OrganizationId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("organization")]
        public FightarrApiOrganization Organization { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string Title { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("slug")]
        public string Slug { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("eventNumber")]
        public string EventNumber { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("eventDate")]
        public DateTime EventDate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("eventType")]
        public string EventType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("location")]
        public string Location { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("venue")]
        public string Venue { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("broadcaster")]
        public string Broadcaster { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string Description { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("posterUrl")]
        public string PosterUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bannerUrl")]
        public string BannerUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string Status { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fights")]
        public List<FightarrApiFight> Fights { get; set; }
    }

    public class FightarrApiOrganization
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("slug")]
        public string Slug { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string Type { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("logoUrl")]
        public string LogoUrl { get; set; }
    }

    public class FightarrApiFight
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fighter1Id")]
        public int Fighter1Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fighter1")]
        public FightarrApiFighter Fighter1 { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fighter2Id")]
        public int Fighter2Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fighter2")]
        public FightarrApiFighter Fighter2 { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("weightClass")]
        public string WeightClass { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("isTitleFight")]
        public bool IsTitleFight { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("isMainEvent")]
        public bool IsMainEvent { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fightOrder")]
        public int FightOrder { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("result")]
        public string Result { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("method")]
        public string Method { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("round")]
        public int? Round { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("time")]
        public string Time { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("referee")]
        public string Referee { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("notes")]
        public string Notes { get; set; }
    }

    public class FightarrApiFighter
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("slug")]
        public string Slug { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nickname")]
        public string Nickname { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("weightClass")]
        public string WeightClass { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nationality")]
        public string Nationality { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("wins")]
        public int Wins { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("losses")]
        public int Losses { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("draws")]
        public int Draws { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("noContests")]
        public int NoContests { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("birthDate")]
        public DateTime? BirthDate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("height")]
        public string Height { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("reach")]
        public string Reach { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("imageUrl")]
        public string ImageUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bio")]
        public string Bio { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }
}
