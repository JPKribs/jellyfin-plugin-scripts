using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class PauseTrackerTests
{
    [Fact]
    public void ReportsOneEdgePerChange()
    {
        var tracker = new PauseTracker();
        tracker.Started("s");

        Assert.Null(tracker.Progress("s", false));
        Assert.True(tracker.Progress("s", true));
        Assert.Null(tracker.Progress("s", true));
        Assert.False(tracker.Progress("s", false));
        Assert.Null(tracker.Progress("s", false));
    }

    [Fact]
    public void AnUnknownSessionRecordsWithoutAnEdge()
    {
        var tracker = new PauseTracker();

        Assert.Null(tracker.Progress("s", true));
        Assert.False(tracker.Progress("s", false));
    }

    [Fact]
    public void StopForgetsTheSession()
    {
        var tracker = new PauseTracker();
        tracker.Started("s");
        tracker.Stopped("s");

        Assert.Null(tracker.Progress("s", true));
    }
}
