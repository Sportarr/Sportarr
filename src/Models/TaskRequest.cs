namespace Fightarr.Api.Models;

/// <summary>
/// Request model for creating a new task
/// </summary>
public record TaskRequest(string Name, string CommandName, int? Priority, string? Body);
