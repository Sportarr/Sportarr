using FluentAssertions;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// A stream proxy request takes a lease before it knows the content type. On
/// an HLS playlist it then records a longer-lived viewer entry instead. Both
/// stand for the same viewer, so counting them together refused a one-stream
/// source its only viewer on every playlist fetch.
/// </summary>
public class StreamSessionTrackerTests
{
    [Fact]
    public void A_single_slot_source_admits_an_hls_viewer_once_its_request_lease_is_handed_over()
    {
        var tracker = new StreamSessionTracker();

        var lease = tracker.TryAcquire(sourceId: 1, maxViewerSlots: 1);
        lease.Should().NotBeNull("the first viewer fits within the cap");

        lease!.Dispose();

        tracker.TouchHlsViewer(sourceId: 1, viewerKey: "10.0.0.5|7", maxViewerSlots: 1)
            .Should().BeTrue("the lease stood for this same viewer");
    }

    [Fact]
    public void A_single_slot_source_refuses_an_hls_viewer_while_the_slot_is_genuinely_taken()
    {
        var tracker = new StreamSessionTracker();

        using var otherViewer = tracker.TryAcquire(sourceId: 1, maxViewerSlots: 1);
        otherViewer.Should().NotBeNull();

        tracker.TouchHlsViewer(sourceId: 1, viewerKey: "10.0.0.9|7", maxViewerSlots: 1)
            .Should().BeFalse("someone else holds the only slot");
    }

    [Fact]
    public void A_returning_hls_viewer_keeps_its_slot_without_taking_another()
    {
        var tracker = new StreamSessionTracker();

        tracker.TouchHlsViewer(sourceId: 1, viewerKey: "10.0.0.5|7", maxViewerSlots: 1).Should().BeTrue();
        tracker.TouchHlsViewer(sourceId: 1, viewerKey: "10.0.0.5|7", maxViewerSlots: 1).Should().BeTrue();

        tracker.GetActiveCount(1).Should().Be(1);
    }

    /// <summary>
    /// The whole request sequence for one HLS viewer on a one-stream source.
    /// The first playlist reserves, later playlists must be recognised as the
    /// same viewer before anything tries to reserve again, or playback stops
    /// after the first playlist.
    /// </summary>
    [Fact]
    public void An_hls_viewer_keeps_playing_across_playlist_refreshes_on_a_one_stream_source()
    {
        var tracker = new StreamSessionTracker();
        const string viewer = "10.0.0.5|VLC/3.0|7";

        // First playlist: not yet known, so it reserves and hands over.
        tracker.RefreshHlsViewer(sourceId: 1, viewerKey: viewer).Should().BeFalse();
        var lease = tracker.TryAcquire(sourceId: 1, maxViewerSlots: 1);
        lease.Should().NotBeNull();
        lease!.Dispose();
        tracker.TouchHlsViewer(sourceId: 1, viewerKey: viewer, maxViewerSlots: 1).Should().BeTrue();

        // Every later playlist is the same viewer and must not reserve again.
        for (var refresh = 0; refresh < 5; refresh++)
        {
            tracker.RefreshHlsViewer(sourceId: 1, viewerKey: viewer)
                .Should().BeTrue("refresh {0} belongs to a viewer that already holds the slot", refresh);
        }

        tracker.GetActiveCount(1).Should().Be(1, "one viewer is watching, not six");
    }

    /// <summary>
    /// A master playlist sends the player to the variant and segment proxy for
    /// the rest of the session. That proxy knows the viewer but not the
    /// source, so it refreshes on the key alone. Without it the entry lapses
    /// while the viewer is still watching and the cap can be exceeded.
    /// </summary>
    [Fact]
    public void A_viewer_can_be_kept_alive_without_naming_its_source()
    {
        var tracker = new StreamSessionTracker();
        const string viewer = "10.0.0.5|VLC/3.0|7";

        tracker.RefreshHlsViewer(viewer).Should().BeFalse("nobody is watching yet");

        tracker.TouchHlsViewer(sourceId: 3, viewerKey: viewer, maxViewerSlots: 1).Should().BeTrue();

        tracker.RefreshHlsViewer(viewer).Should().BeTrue("the segment proxy keeps the entry alive");
        tracker.GetActiveCount(3).Should().Be(1, "refreshing never reserves a second slot");
    }

    [Fact]
    public void A_different_client_at_the_same_address_is_a_separate_viewer()
    {
        var tracker = new StreamSessionTracker();

        tracker.TouchHlsViewer(1, "10.0.0.5|VLC/3.0|7", maxViewerSlots: 2).Should().BeTrue();
        tracker.RefreshHlsViewer(1, "10.0.0.5|Kodi/21|7").Should().BeFalse(
            "a different player behind the same router has not been counted yet");

        tracker.TouchHlsViewer(1, "10.0.0.5|Kodi/21|7", maxViewerSlots: 2).Should().BeTrue();
        tracker.GetActiveCount(1).Should().Be(2);
    }

    [Fact]
    public void A_second_hls_viewer_is_refused_when_the_cap_is_one()
    {
        var tracker = new StreamSessionTracker();

        tracker.TouchHlsViewer(sourceId: 1, viewerKey: "10.0.0.5|7", maxViewerSlots: 1).Should().BeTrue();
        tracker.TouchHlsViewer(sourceId: 1, viewerKey: "10.0.0.6|7", maxViewerSlots: 1).Should().BeFalse();
    }
}
