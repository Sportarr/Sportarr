using System.Text.Json.Serialization;

namespace Sportarr
{
    /// <summary>
    /// Strongly-typed models for Sportarr API responses.
    /// Nullable fields are marked with ? to indicate optional data.
    /// </summary>

    #region Series (League) Models

    /// <summary>
    /// Represents a series (league) from the Sportarr API.
    /// </summary>
#nullable enable
    public class SportarrSeries
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("sort_title")]
        public string? SortTitle { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("poster_url")]
        public string? PosterUrl { get; set; }

        [JsonPropertyName("banner_url")]
        public string? BannerUrl { get; set; }

        [JsonPropertyName("fanart_url")]
        public string? FanartUrl { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("studio")]
        public string? Studio { get; set; }

        [JsonPropertyName("genres")]
        public string[]? Genres { get; set; }

        [JsonPropertyName("content_rating")]
        public string? ContentRating { get; set; }

        [JsonPropertyName("sport")]
        public string? Sport { get; set; }
    }

    /// <summary>
    /// Search results response containing multiple series.
    /// </summary>
    public class SportarrSeriesSearchResponse
    {
        [JsonPropertyName("results")]
        public SportarrSeries[] Results { get; set; } = System.Array.Empty<SportarrSeries>();

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    #endregion

    #region Season Models

    /// <summary>
    /// Represents a season from the Sportarr API.
    /// </summary>
    public class SportarrSeason
    {
        [JsonPropertyName("season_number")]
        public int SeasonNumber { get; set; }

        [JsonPropertyName("title")]

        public string? Title { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("poster_url")]
        public string? PosterUrl { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("episode_count")]
        public int? EpisodeCount { get; set; }
    }

    /// <summary>
    /// Seasons response for a series.
    /// </summary>
    public class SportarrSeasonsResponse
    {
        [JsonPropertyName("seasons")]
        public SportarrSeason[] Seasons { get; set; } = System.Array.Empty<SportarrSeason>();

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    #endregion

    #region Episode (Event) Models

    /// <summary>
    /// Represents an episode (event) from the Sportarr API.
    /// </summary>
    public class SportarrEpisode
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("episode_number")]
        public int EpisodeNumber { get; set; }

        [JsonPropertyName("season_number")]
        public int? SeasonNumber { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("air_date")]
        public string? AirDate { get; set; }

        [JsonPropertyName("duration_minutes")]
        public int? DurationMinutes { get; set; }

        [JsonPropertyName("thumb_url")]
        public string? ThumbUrl { get; set; }

        [JsonPropertyName("part_name")]
        public string? PartName { get; set; }

        [JsonPropertyName("venue")]
        public string? Venue { get; set; }

        [JsonPropertyName("home_team")]
        public string? HomeTeam { get; set; }

        [JsonPropertyName("away_team")]
        public string? AwayTeam { get; set; }

        [JsonPropertyName("cast")]
        public List<SportarrCastMember>? Cast { get; set; }
    }

    /// <summary>
    /// One player in an event's cast (the squad / athletes who featured),
    /// served per episode by the hub via sportarr's match endpoint.
    /// </summary>
    public class SportarrCastMember
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("team")]
        public string? Team { get; set; }

        [JsonPropertyName("side")]
        public string? Side { get; set; }

        [JsonPropertyName("position")]
        public string? Position { get; set; }

        [JsonPropertyName("number")]
        public string? Number { get; set; }
    }

    /// <summary>
    /// Episodes response for a season.
    /// </summary>
    public class SportarrEpisodesResponse
    {
        [JsonPropertyName("episodes")]
        public SportarrEpisode[] Episodes { get; set; } = System.Array.Empty<SportarrEpisode>();

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    /// <summary>
    /// Response from /api/metadata/match, which resolves a single event
    /// (under match.episode) instead of returning the whole season list.
    /// </summary>
    public class SportarrMatchResponse
    {
        [JsonPropertyName("match")]
        public SportarrMatch? Match { get; set; }
    }

    /// <summary>
    /// The match payload wrapper holding the resolved episode.
    /// </summary>
    public class SportarrMatch
    {
        [JsonPropertyName("episode")]
        public SportarrEpisode? Episode { get; set; }
    }

    #endregion

    #region Sportarr (Health) Models

    /// <summary>
    /// Represents a Health Check response from the Sportarr API.
    /// </summary>
    public class SportarrHealthResponse
    {
        // Plain (non-required) members so the plugin compiles on net6.0, whose
        // C# 10 default predates the `required` keyword (and its net7+ runtime
        // attribute). This is only a deserialization DTO, so the compile-time
        // required enforcement was not load-bearing.
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("build")]
        public string Build { get; set; } = string.Empty;

    }

    #endregion
}
