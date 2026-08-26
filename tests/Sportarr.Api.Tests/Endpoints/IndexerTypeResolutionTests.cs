using FluentAssertions;
using Sportarr.Api.Endpoints;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Endpoints;

/// <summary>
/// Prowlarr says which protocol an indexer speaks in its implementation name.
/// The mapping used to be written out at each call site, and the one that
/// creates an indexer from a PUT was missed. Torznab is the first value of the
/// enum, so an indexer created that way came out Torznab whatever the payload
/// said, and a Newznab one then searched on the wrong protocol.
/// </summary>
public class IndexerTypeResolutionTests
{
    [Theory]
    [InlineData("Torznab")]
    [InlineData("torznab")]
    [InlineData("TORZNAB")]
    public void Torznab_is_recognised_however_it_is_written(string implementation)
    {
        SonarrIndexerEndpoints.ResolveIndexerType(implementation).Should().Be(IndexerType.Torznab);
    }

    [Theory]
    [InlineData("Newznab")]
    [InlineData("newznab")]
    public void Newznab_keeps_its_own_type(string implementation)
    {
        SonarrIndexerEndpoints.ResolveIndexerType(implementation).Should().Be(IndexerType.Newznab);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomethingElse")]
    public void Anything_that_is_not_torznab_is_treated_as_newznab(string? implementation)
    {
        // Newznab is the safer default here: it is what the payload parser
        // falls back to when the field is absent.
        SonarrIndexerEndpoints.ResolveIndexerType(implementation).Should().Be(IndexerType.Newznab);
    }
}
