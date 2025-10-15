using System.ComponentModel.DataAnnotations;

namespace Fightarr.Api.Models;

public class LoginRequest
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; } = false;
}

public class LoginResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Token { get; set; }
}

public class AuthSession
{
    [Key]
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    public string Username { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public bool RememberMe { get; set; } = false;

    public string IpAddress { get; set; } = "";

    public string UserAgent { get; set; } = "";
}
