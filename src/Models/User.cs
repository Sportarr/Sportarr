using System.ComponentModel.DataAnnotations;

namespace Sportarr.Api.Models;

/// <summary>
/// User model for authentication (matches Sonarr/Radarr implementation)
/// </summary>
public class User
{
    [Key]
    public int Id { get; set; }

    public Guid Identifier { get; set; } = Guid.NewGuid();

    [Required]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = ""; // Hashed password

    [Required]
    public string Salt { get; set; } = ""; // Base64-encoded salt

    public int Iterations { get; set; } = 10000; // PBKDF2 iterations
}
