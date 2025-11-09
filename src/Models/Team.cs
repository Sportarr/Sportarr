namespace Fightarr.Api.Models;

/// <summary>
/// Represents a sports team (e.g., Lakers, Patriots, Real Madrid)
/// Used for team sports like Soccer, Basketball, Football, etc.
/// </summary>
public class Team
{
    public int Id { get; set; }

    /// <summary>
    /// Team ID from TheSportsDB API
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Team name (e.g., "Los Angeles Lakers", "New England Patriots")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Team short name/abbreviation (e.g., "LAL", "NE")
    /// </summary>
    public string? ShortName { get; set; }

    /// <summary>
    /// Alternate team name (e.g., historical names)
    /// </summary>
    public string? AlternateName { get; set; }

    /// <summary>
    /// League/competition the team belongs to
    /// </summary>
    public int? LeagueId { get; set; }
    public League? League { get; set; }

    /// <summary>
    /// Sport type (e.g., "Soccer", "Basketball", "Baseball")
    /// </summary>
    public required string Sport { get; set; }

    /// <summary>
    /// Team country (e.g., "USA", "Spain", "England")
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Team stadium/arena name
    /// </summary>
    public string? Stadium { get; set; }

    /// <summary>
    /// Stadium/arena location
    /// </summary>
    public string? StadiumLocation { get; set; }

    /// <summary>
    /// Stadium capacity
    /// </summary>
    public int? StadiumCapacity { get; set; }

    /// <summary>
    /// Team description/bio
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Team badge/logo URL
    /// </summary>
    public string? BadgeUrl { get; set; }

    /// <summary>
    /// Team jersey/kit image URL
    /// </summary>
    public string? JerseyUrl { get; set; }

    /// <summary>
    /// Team banner image URL
    /// </summary>
    public string? BannerUrl { get; set; }

    /// <summary>
    /// Official team website
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// Year the team was formed
    /// </summary>
    public int? FormedYear { get; set; }

    /// <summary>
    /// Team's primary color (hex code)
    /// </summary>
    public string? PrimaryColor { get; set; }

    /// <summary>
    /// Team's secondary color (hex code)
    /// </summary>
    public string? SecondaryColor { get; set; }

    /// <summary>
    /// When this team was added to the cache
    /// </summary>
    public DateTime Added { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time team metadata was updated from TheSportsDB
    /// </summary>
    public DateTime? LastUpdate { get; set; }
}
