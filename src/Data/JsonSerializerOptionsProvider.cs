using System.Text.Json;

namespace Sportarr.Api.Data;

/// <summary>
/// Provides static JsonSerializerOptions instances to avoid repeated allocations.
/// JSON serialization can create significant memory pressure when options are
/// created per-call, especially in EF Core value converters that run frequently.
/// </summary>
public static class JsonSerializerOptionsProvider
{
    /// <summary>
    /// Default options for JSON serialization in the application.
    /// Uses camelCase property names and compact (non-indented) output.
    /// </summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Options for database storage - compact output with no indentation.
    /// Optimized for minimal storage space in SQLite TEXT columns.
    /// </summary>
    public static readonly JsonSerializerOptions Database = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };
}
