using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sportarr.Api.Data;

namespace Sportarr.Api.Services;

/// <summary>
/// One-shot startup service. A recording choice that was importing when the
/// process stopped goes back to Pending, so the user can decide again.
/// </summary>
public class DvrChoiceRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DvrChoiceRecoveryService> _logger;

    public DvrChoiceRecoveryService(IServiceScopeFactory scopeFactory, ILogger<DvrChoiceRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SportarrDbContext>();
            var reopened = await DvrIndexerChoice.ReopenInterruptedChoicesAsync(db, cancellationToken);
            if (reopened > 0)
                _logger.LogInformation("[EventDVR] Reopened {Count} recording choices that stopped during an import", reopened);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EventDVR] Could not reopen interrupted recording choices");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
