namespace Sportarr.Api.Constants;

/// <summary>
/// IConfiguration keys used throughout startup. Centralized to prevent typos
/// and make settings visible at a glance.
/// </summary>
public static class ConfigurationKeys
{
    public const string DataPath = "Sportarr:DataPath";
    public const string ApiKey = "Sportarr:ApiKey";
    public const string Branch = "Sportarr:Branch";
    public const string DatabaseProvider = "Sportarr:Database:Provider";
    public const string DatabaseHost = "Sportarr:Database:Host";
    public const string DatabasePort = "Sportarr:Database:Port";
    public const string DatabaseName = "Sportarr:Database:Name";
    public const string DatabaseUsername = "Sportarr:Database:Username";
    public const string DatabasePassword = "Sportarr:Database:Password";
    public const string DatabaseConnectionString = "Sportarr:Database:ConnectionString";
}

/// <summary>
/// Environment variable names. Note: ASP.NET Core configuration uses double-underscore (__)
/// to map to colon-separated config keys (Sportarr__DataPath -> Sportarr:DataPath).
/// </summary>
public static class EnvironmentVariableNames
{
    public const string DataPath = "Sportarr__DataPath";
    public const string ApiKey = "Sportarr__ApiKey";
    public const string Branch = "SPORTARR_BRANCH";
    public const string AspNetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";
    public const string DotNetCliTelemetryOptOut = "DOTNET_CLI_TELEMETRY_OPTOUT";
    public const string RunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";
    public const string DatabaseProvider = "Sportarr__Database__Provider";
    public const string DatabaseHost = "Sportarr__Database__Host";
    public const string DatabasePort = "Sportarr__Database__Port";
    public const string DatabaseName = "Sportarr__Database__Name";
    public const string DatabaseUsername = "Sportarr__Database__Username";
    public const string DatabasePassword = "Sportarr__Database__Password";
    public const string DatabaseConnectionString = "Sportarr__Database__ConnectionString";

    /// <summary>
    /// Prefix for the Docker-secrets convention: FILE__&lt;VarName&gt; points at a file whose
    /// contents become the value of &lt;VarName&gt;. Generic - works for any Sportarr__* secret,
    /// not just the database password (matches Radarr/Sonarr's FILE__ convention).
    /// </summary>
    public const string SecretFilePrefix = "FILE__";
}
