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
}
