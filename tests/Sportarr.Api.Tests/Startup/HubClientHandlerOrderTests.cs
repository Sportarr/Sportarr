using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sportarr.Api.Services;
using Sportarr.Api.Startup;

namespace Sportarr.Api.Tests.Startup;

/// <summary>
/// Handler order on the hub client is load bearing, and reading the chain is
/// how it went wrong twice. Each handler added is nested inside the one
/// before it, so the pacer has to sit below the retry policy (or retries
/// jump the pacing queue and the flood continues) and above the per-attempt
/// timeout (or waiting for a slot is counted as time spent on the request,
/// and a long Retry-After times the call out before it is ever sent).
/// </summary>
public class HubClientHandlerOrderTests
{
    /// <summary>Collects what the registration actually adds, in order.</summary>
    private sealed class ProbeBuilder : HttpMessageHandlerBuilder
    {
        public ProbeBuilder(IServiceProvider services) => Services = services;

        public override IServiceProvider Services { get; }
        public override string? Name { get; set; }
        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();
        public override HttpMessageHandler Build() => PrimaryHandler;
    }

    private static IList<DelegatingHandler> BuildHubHandlerChain()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSportarrHttpClients();

        var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(nameof(SportarrApiClient));

        var probe = new ProbeBuilder(provider) { Name = nameof(SportarrApiClient) };

        foreach (var action in options.HttpMessageHandlerBuilderActions)
        {
            action(probe);
        }

        return probe.AdditionalHandlers;
    }

    [Fact]
    public void The_pacer_sits_between_the_retry_and_the_timeout()
    {
        var handlers = BuildHubHandlerChain();

        var pacerAt = handlers.ToList().FindIndex(h => h is HubRequestPacer);
        pacerAt.Should().BeGreaterThan(-1, "the hub client has to be paced at all");

        // Policy handlers are the retry and the timeout, in registration order.
        var policyPositions = handlers
            .Select((h, i) => (h, i))
            .Where(x => x.h.GetType().Name.Contains("PolicyHttpMessageHandler"))
            .Select(x => x.i)
            .ToList();

        policyPositions.Should().HaveCountGreaterThanOrEqualTo(2,
            "the client keeps a retry policy and a per-attempt timeout");

        var retryAt = policyPositions.First();
        var timeoutAt = policyPositions.Last();

        pacerAt.Should().BeGreaterThan(retryAt,
            "nested inside the retry, so every attempt is paced");
        pacerAt.Should().BeLessThan(timeoutAt,
            "outside the timeout, so waiting for a slot is not charged to the request");
    }
}
