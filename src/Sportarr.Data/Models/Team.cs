using System.Text.Json.Serialization;
using Sportarr.Api.Helpers;

namespace Sportarr.Api.Models;

/// <summary>
/// Represents a sports team (e.g., Lakers, Patriots, Real Madrid)
/// Used for team sports like Soccer, Basketball, Football, etc.
/// </summary>
public class Team
{
    public int Id { get; set; }

    /// <summary>
    /// Team ID from Sportarr API. Carries the hub short_id
    /// (tm-XXXXXX) after the short_id-primary flip; the
    /// TheSportsDB cross-reference rides separately in TsdbId.
    /// </summary>
    [JsonPropertyName("idTeam")]
    public string? ExternalId { get; set; }

    /// <summary>
    /// TheSportsDB cross-reference id. Inbound-only, never persisted.
    /// Used to migrate legacy ExternalIds that still carry the
    /// TheSportsDB id.
    /// </summary>
    [JsonPropertyName("tsdbId")]
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? TsdbId { get; set; }

    /// <summary>
    /// Team name (e.g., "Los Angeles Lakers", "New England Patriots")
    /// </summary>
    [JsonPropertyName("strTeam")]
    public required string Name { get; set; }

    /// <summary>
    /// Team short name/abbreviation (e.g., "LAL", "NE")
    /// </summary>
    [JsonPropertyName("strTeamShort")]
    public string? ShortName { get; set; }

    /// <summary>
    /// Alternate team name (e.g., historical names)
    /// </summary>
    [JsonPropertyName("strAlternate")]
    public string? AlternateName { get; set; }

    /// <summary>
    /// User-defined aliases (comma-separated), checked by release matching
    /// alongside the upstream alternates. Local-only: metadata sync must
    /// NEVER write this field - it exists precisely so users can teach
    /// their own install the names their release groups use ("GWS",
    /// "ManCity") without waiting for upstream data changes and without
    /// their edits being overwritten on the next sync.
    /// </summary>
    public string? UserAliases { get; set; }

    /// <summary>
    /// League/competition the team belongs to
    /// </summary>
    public int? LeagueId { get; set; }
    public League? League { get; set; }

    /// <summary>
    /// Sport type (e.g., "Soccer", "Basketball", "Baseball")
    /// Note: sportarr.net /list/teams endpoint doesn't return this field
    /// </summary>
    [JsonPropertyName("strSport")]
    public string? Sport { get; set; }

    /// <summary>
    /// Team country (e.g., "USA", "Spain", "England")
    /// </summary>
    [JsonPropertyName("strCountry")]
    public string? Country { get; set; }

    /// <summary>
    /// Team stadium/arena name
    /// </summary>
    [JsonPropertyName("strStadium")]
    public string? Stadium { get; set; }

    /// <summary>
    /// Stadium/arena location
    /// </summary>
    [JsonPropertyName("strStadiumLocation")]
    public string? StadiumLocation { get; set; }

    /// <summary>
    /// Stadium capacity
    /// </summary>
    [JsonPropertyName("intStadiumCapacity")]
    public int? StadiumCapacity { get; set; }

    /// <summary>
    /// Team description/bio
    /// </summary>
    [JsonPropertyName("strDescriptionEN")]
    public string? Description { get; set; }

    // Image URLs go through ImageUrlNormalizer so legacy
    // www.thesportsdb.com URLs get rewritten to r2.thesportsdb.com
    // on assignment. See League.LogoUrl for the rationale.

    /// <summary>
    /// Team badge/logo URL
    /// </summary>
    [JsonPropertyName("strTeamBadge")]
    public string? BadgeUrl
    {
        get => _badgeUrl;
        set => _badgeUrl = ImageUrlNormalizer.Normalize(value);
    }
    private string? _badgeUrl;

    /// <summary>
    /// Team jersey/kit image URL
    /// </summary>
    [JsonPropertyName("strTeamJersey")]
    public string? JerseyUrl
    {
        get => _jerseyUrl;
        set => _jerseyUrl = ImageUrlNormalizer.Normalize(value);
    }
    private string? _jerseyUrl;

    /// <summary>
    /// Team banner image URL
    /// </summary>
    [JsonPropertyName("strTeamBanner")]
    public string? BannerUrl
    {
        get => _bannerUrl;
        set => _bannerUrl = ImageUrlNormalizer.Normalize(value);
    }
    private string? _bannerUrl;

    /// <summary>
    /// Official team website
    /// </summary>
    [JsonPropertyName("strWebsite")]
    public string? Website { get; set; }

    /// <summary>
    /// Year the team was formed
    /// </summary>
    [JsonPropertyName("intFormedYear")]
    public int? FormedYear { get; set; }

    /// <summary>
    /// Team's primary color (hex code)
    /// </summary>
    [JsonPropertyName("strColour1")]
    public string? PrimaryColor { get; set; }

    /// <summary>
    /// Team's secondary color (hex code)
    /// </summary>
    [JsonPropertyName("strColour2")]
    public string? SecondaryColor { get; set; }

    /// <summary>
    /// When this team was added to the cache
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time team metadata was updated from Sportarr API
    /// </summary>
    public DateTime? LastUpdate { get; set; }
}
