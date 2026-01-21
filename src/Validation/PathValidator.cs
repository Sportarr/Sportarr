namespace Sportarr.Api.Validation;

/// <summary>
/// Validation helpers for file paths to prevent path traversal and other security issues.
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Validates that a path is safe to use (no path traversal, valid characters).
    /// </summary>
    /// <param name="path">The path to validate</param>
    /// <param name="error">Error message if validation fails</param>
    /// <returns>True if path is valid, false otherwise</returns>
    public static bool IsValidPath(string? path, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path cannot be empty";
            return false;
        }

        // Check for path traversal attempts
        if (path.Contains(".."))
        {
            error = "Path traversal (..) is not allowed";
            return false;
        }

        // Check for invalid path characters
        var invalidChars = Path.GetInvalidPathChars();
        if (path.Any(c => invalidChars.Contains(c)))
        {
            error = "Path contains invalid characters";
            return false;
        }

        // Check for null bytes (potential injection)
        if (path.Contains('\0'))
        {
            error = "Path contains null character";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that a filename is safe to use (valid characters, no directory separators).
    /// </summary>
    /// <param name="filename">The filename to validate</param>
    /// <param name="error">Error message if validation fails</param>
    /// <returns>True if filename is valid, false otherwise</returns>
    public static bool IsValidFilename(string? filename, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(filename))
        {
            error = "Filename cannot be empty";
            return false;
        }

        // Check for path separators (shouldn't be in a filename)
        if (filename.Contains('/') || filename.Contains('\\'))
        {
            error = "Filename cannot contain path separators";
            return false;
        }

        // Check for invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (filename.Any(c => invalidChars.Contains(c)))
        {
            error = "Filename contains invalid characters";
            return false;
        }

        // Check for reserved names on Windows
        var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
            "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
            "LPT6", "LPT7", "LPT8", "LPT9" };

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename).ToUpperInvariant();
        if (reservedNames.Contains(nameWithoutExtension))
        {
            error = $"'{filename}' is a reserved filename";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that a path exists within a specified base directory (prevents escaping).
    /// </summary>
    /// <param name="basePath">The base directory that the path must be within</param>
    /// <param name="targetPath">The path to validate</param>
    /// <param name="error">Error message if validation fails</param>
    /// <returns>True if path is within the base directory, false otherwise</returns>
    public static bool IsPathWithinBase(string basePath, string targetPath, out string? error)
    {
        error = null;

        if (!IsValidPath(basePath, out error) || !IsValidPath(targetPath, out error))
        {
            return false;
        }

        try
        {
            var fullBasePath = Path.GetFullPath(basePath);
            var fullTargetPath = Path.GetFullPath(targetPath);

            // Ensure base path ends with separator for proper comparison
            if (!fullBasePath.EndsWith(Path.DirectorySeparatorChar))
            {
                fullBasePath += Path.DirectorySeparatorChar;
            }

            if (!fullTargetPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase))
            {
                error = "Path is outside the allowed directory";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid path: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Combines a base path with a relative path safely, ensuring the result stays within the base.
    /// </summary>
    /// <param name="basePath">The base directory</param>
    /// <param name="relativePath">The relative path to append</param>
    /// <param name="combinedPath">The combined path if successful</param>
    /// <param name="error">Error message if validation fails</param>
    /// <returns>True if combination is safe, false otherwise</returns>
    public static bool TryCombinePaths(string basePath, string relativePath, out string? combinedPath, out string? error)
    {
        combinedPath = null;
        error = null;

        if (!IsValidPath(basePath, out error) || !IsValidPath(relativePath, out error))
        {
            return false;
        }

        try
        {
            var combined = Path.Combine(basePath, relativePath);
            var fullCombined = Path.GetFullPath(combined);

            if (!IsPathWithinBase(basePath, fullCombined, out error))
            {
                return false;
            }

            combinedPath = fullCombined;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to combine paths: {ex.Message}";
            return false;
        }
    }
}

/// <summary>
/// Validation helpers for URL validation.
/// </summary>
public static class UrlValidator
{
    private static readonly string[] AllowedSchemes = { "http", "https" };

    /// <summary>
    /// Validates that a URL is well-formed and uses HTTP/HTTPS.
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <param name="error">Error message if validation fails</param>
    /// <returns>True if URL is valid, false otherwise</returns>
    public static bool IsValidUrl(string? url, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            error = "URL cannot be empty";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "URL is not well-formed";
            return false;
        }

        if (!AllowedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
        {
            error = $"URL scheme must be HTTP or HTTPS, got '{uri.Scheme}'";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that a URL points to a specific host (for security-sensitive operations).
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <param name="allowedHosts">List of allowed hostnames</param>
    /// <param name="error">Error message if validation fails</param>
    /// <returns>True if URL host is allowed, false otherwise</returns>
    public static bool IsHostAllowed(string url, IEnumerable<string> allowedHosts, out string? error)
    {
        error = null;

        if (!IsValidUrl(url, out error))
        {
            return false;
        }

        var uri = new Uri(url);
        var host = uri.Host.ToLowerInvariant();

        if (!allowedHosts.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Host '{host}' is not in the allowed list";
            return false;
        }

        return true;
    }
}

/// <summary>
/// Validation helpers for common input types.
/// </summary>
public static class InputValidator
{
    /// <summary>
    /// Validates that a string doesn't exceed maximum length.
    /// </summary>
    public static bool IsValidLength(string? input, int maxLength, out string? error, string fieldName = "Input")
    {
        error = null;

        if (input != null && input.Length > maxLength)
        {
            error = $"{fieldName} exceeds maximum length of {maxLength} characters";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that a required string is not null or empty.
    /// </summary>
    public static bool IsRequired(string? input, out string? error, string fieldName = "Field")
    {
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = $"{fieldName} is required";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that a number is within a specified range.
    /// </summary>
    public static bool IsInRange(int value, int min, int max, out string? error, string fieldName = "Value")
    {
        error = null;

        if (value < min || value > max)
        {
            error = $"{fieldName} must be between {min} and {max}, got {value}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that an ID is positive (valid database ID).
    /// </summary>
    public static bool IsValidId(int id, out string? error, string fieldName = "ID")
    {
        error = null;

        if (id <= 0)
        {
            error = $"{fieldName} must be a positive integer";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates an API key format (alphanumeric, specific length).
    /// </summary>
    public static bool IsValidApiKey(string? apiKey, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            error = "API key cannot be empty";
            return false;
        }

        // API keys should be at least 16 characters for security
        if (apiKey.Length < 16)
        {
            error = "API key must be at least 16 characters";
            return false;
        }

        // API keys should only contain alphanumeric characters and hyphens
        if (!apiKey.All(c => char.IsLetterOrDigit(c) || c == '-'))
        {
            error = "API key contains invalid characters";
            return false;
        }

        return true;
    }
}
