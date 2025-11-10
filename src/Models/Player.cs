using System.Text.Json.Serialization;

namespace Sportarr.Api.Models;

/// <summary>
/// Represents an athlete/player in team sports or individual sports
/// For combat sports, this can represent fighters
/// For team sports, this represents players on teams
/// </summary>
public class Player
{
    public int Id { get; set; }

    /// <summary>
    /// Player ID from TheSportsDB API
    /// </summary>
    [JsonPropertyName("idPlayer")]
    public string? ExternalId { get; set; }

    /// <summary>
    /// Player full name (e.g., "LeBron James", "Cristiano Ronaldo")
    /// </summary>
    [JsonPropertyName("strPlayer")]
    public required string Name { get; set; }

    /// <summary>
    /// Player first/given name
    /// </summary>
    [JsonPropertyName("strForename")]
    public string? FirstName { get; set; }

    /// <summary>
    /// Player last/family name
    /// </summary>
    [JsonPropertyName("strSurname")]
    public string? LastName { get; set; }

    /// <summary>
    /// Player nickname (e.g., "King James", "CR7")
    /// </summary>
    [JsonPropertyName("strNickname")]
    public string? Nickname { get; set; }

    /// <summary>
    /// Sport the player competes in
    /// </summary>
    [JsonPropertyName("strSport")]
    public required string Sport { get; set; }

    /// <summary>
    /// Current team ID (null if free agent or retired)
    /// </summary>
    [JsonPropertyName("idTeam")]
    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>
    /// Player position (e.g., "Forward", "Quarterback", "Midfielder")
    /// For combat sports: "Fighter"
    /// </summary>
    [JsonPropertyName("strPosition")]
    public string? Position { get; set; }

    /// <summary>
    /// Player nationality
    /// </summary>
    [JsonPropertyName("strNationality")]
    public string? Nationality { get; set; }

    /// <summary>
    /// Date of birth
    /// </summary>
    [JsonPropertyName("dateBorn")]
    public DateTime? BirthDate { get; set; }

    /// <summary>
    /// Birthplace (city, country)
    /// </summary>
    [JsonPropertyName("strBirthLocation")]
    public string? Birthplace { get; set; }

    /// <summary>
    /// Height in centimeters
    /// </summary>
    [JsonPropertyName("strHeight")]
    public int? Height { get; set; }

    /// <summary>
    /// Weight in kilograms
    /// </summary>
    [JsonPropertyName("strWeight")]
    public decimal? Weight { get; set; }

    /// <summary>
    /// Jersey/uniform number
    /// </summary>
    [JsonPropertyName("strNumber")]
    public string? Number { get; set; }

    /// <summary>
    /// Player description/bio
    /// </summary>
    [JsonPropertyName("strDescriptionEN")]
    public string? Description { get; set; }

    /// <summary>
    /// Player headshot/portrait URL
    /// </summary>
    [JsonPropertyName("strCutout")]
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Player action shot URL
    /// </summary>
    [JsonPropertyName("strThumb")]
    public string? ActionPhotoUrl { get; set; }

    /// <summary>
    /// Player banner image URL
    /// </summary>
    [JsonPropertyName("strBanner")]
    public string? BannerUrl { get; set; }

    /// <summary>
    /// Dominant side (e.g., "Right", "Left", "Both")
    /// For soccer: preferred foot
    /// For combat sports: stance (Orthodox, Southpaw)
    /// </summary>
    [JsonPropertyName("strSide")]
    public string? Dominance { get; set; }

    /// <summary>
    /// Official player website
    /// </summary>
    [JsonPropertyName("strWebsite")]
    public string? Website { get; set; }

    /// <summary>
    /// Social media handles (JSON stored as comma-separated)
    /// </summary>
    public string? SocialMedia { get; set; }

    // Combat sports specific fields
    /// <summary>
    /// Weight class (for combat sports)
    /// </summary>
    [JsonPropertyName("strWeightClass")]
    public string? WeightClass { get; set; }

    /// <summary>
    /// Fight record (e.g., "20-5-0" for 20 wins, 5 losses, 0 draws)
    /// </summary>
    [JsonPropertyName("strRecord")]
    public string? Record { get; set; }

    /// <summary>
    /// Fighting stance (Orthodox, Southpaw, Switch)
    /// </summary>
    [JsonPropertyName("strStance")]
    public string? Stance { get; set; }

    /// <summary>
    /// Reach in centimeters
    /// </summary>
    [JsonPropertyName("strReach")]
    public decimal? Reach { get; set; }

    /// <summary>
    /// When this player was added to the cache
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time player metadata was updated from TheSportsDB
    /// </summary>
    public DateTime? LastUpdate { get; set; }
}
