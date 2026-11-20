using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Sends one release choice as anonymous usage data, away from the import.
/// It holds no database context, so the send can outlive the import scope.
/// </summary>
public class ReleaseChoiceReporter
{
    private readonly ConfigService _configService;
    private readonly SportarrApiClient _client;
    private readonly ILogger<ReleaseChoiceReporter> _logger;

    public ReleaseChoiceReporter(ConfigService configService, SportarrApiClient client, ILogger<ReleaseChoiceReporter> logger)
    {
        _configService = configService;
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Starts the send and returns at once. The import does not await the
    /// returned task. The task never faults.
    /// </summary>
    public virtual Task Report(ReleaseChoice? choice)
    {
        if (choice == null)
            return Task.CompletedTask;
        return Task.Run(() => SendAsync(choice));
    }

    private async Task SendAsync(ReleaseChoice choice)
    {
        try
        {
            // Read the live setting. A user who opts out after the grab sends nothing.
            var config = await _configService.GetConfigAsync();
            if (!config.SendAnonymousUsageData)
                return;
            var installId = await _configService.GetOrCreateAnalyticsInstanceIdAsync();
            if (installId == null)
                return;
            await _client.SubmitReleaseChoicesAsync(installId, Sportarr.Api.Version.AppVersion, new[] { choice });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Import] Release choice report skipped");
        }
    }
}
