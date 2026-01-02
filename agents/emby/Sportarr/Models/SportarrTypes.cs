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

    #endregion

    #region Sportarr (Health) Models

    /// <summary>
    /// Represents a Health Check response from the Sportarr API.
    /// </summary>
    public class SportarrHealthResponse
    {
        [JsonPropertyName("status")]
        public required string Status { get; set; }

        [JsonPropertyName("timestamp")]
        public required DateTime Timestamp { get; set; }

        [JsonPropertyName("version")]
        public required string Version { get; set; }

        [JsonPropertyName("build")]
        public required string Build { get; set; }

    }

    #endregion
}
