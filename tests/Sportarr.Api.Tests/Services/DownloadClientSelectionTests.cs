using Sportarr.Api.Models;
using Sportarr.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Covers DownloadClientService.PickAssignedClient, the shared resolution of
/// an indexer's explicitly assigned download client used by every grab path
/// (RSS sync, automatic search, interactive grab, pending release reaper,
/// history re-grabs). Regression guard for #198: the assignment must win over
/// priority ordering when the assigned client is eligible, and quietly fall
/// back (return null) when it isn't.
/// </summary>
public class DownloadClientSelectionTests
{
    private static DownloadClient Client(int id, string name, int priority = 1)
        => new() { Id = id, Name = name, Host = "localhost", Priority = priority };

    [Fact]
    public void Returns_null_when_no_assignment_exists()
    {
        var clients = new[] { Client(1, "qbit-a"), Client(2, "qbit-b") };

        var result = DownloadClientService.PickAssignedClient(
            clients, null, NullLogger.Instance, "[Test]");

        result.Should().BeNull();
    }

    [Fact]
    public void Assigned_client_wins_over_priority_ordering()
    {
        var clients = new[] { Client(1, "qbit-a", priority: 1), Client(2, "qbit-b", priority: 50) };

        var result = DownloadClientService.PickAssignedClient(
            clients, 2, NullLogger.Instance, "[Test]");

        result.Should().NotBeNull();
        result!.Name.Should().Be("qbit-b");
    }

    [Fact]
    public void Returns_null_when_the_assigned_client_is_not_eligible()
    {
        // The eligible list is pre-filtered to enabled + protocol-compatible
        // clients, so an assignment pointing outside it (deleted client,
        // disabled, wrong protocol) must fall back to default selection.
        var clients = new[] { Client(1, "qbit-a") };

        var result = DownloadClientService.PickAssignedClient(
            clients, 99, NullLogger.Instance, "[Test]");

        result.Should().BeNull();
    }
}
