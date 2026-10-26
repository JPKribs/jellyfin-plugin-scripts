using System;
using Jellyfin.Plugin.Scripts.Services;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class PlaybackTrackerTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PlaybackProgressEventArgs Args = new();

    [Fact]
    public void ReportsOneEdgePerChange()
    {
        var tracker = new PlaybackTracker();
        tracker.Started("s", Args, Start);

        Assert.Null(tracker.Progress("s", false, Args, Start));
        Assert.True(tracker.Progress("s", true, Args, Start));
        Assert.Null(tracker.Progress("s", true, Args, Start));
        Assert.False(tracker.Progress("s", false, Args, Start));
    }

    [Fact]
    public void AnUnknownSessionRecordsWithoutAnEdge()
    {
        var tracker = new PlaybackTracker();

        Assert.Null(tracker.Progress("s", true, Args, Start));
        Assert.False(tracker.Progress("s", false, Args, Start));
    }

    [Fact]
    public void PausedForFiresOncePerPauseAfterTheThreshold()
    {
        var script = Guid.NewGuid();
        var tracker = new PlaybackTracker();
        tracker.Started("s", Args, Start);
        tracker.Progress("s", true, Args, Start);

        Assert.Empty(tracker.DuePaused(script, TimeSpan.FromMinutes(10), Start.AddMinutes(9)));
        var due = Assert.Single(tracker.DuePaused(script, TimeSpan.FromMinutes(10), Start.AddMinutes(10)));
        Assert.Equal(TimeSpan.FromMinutes(10), due.Paused);
        Assert.Empty(tracker.DuePaused(script, TimeSpan.FromMinutes(10), Start.AddMinutes(20)));

        // A new pause starts a new countdown.
        tracker.Progress("s", false, Args, Start.AddMinutes(21));
        tracker.Progress("s", true, Args, Start.AddMinutes(22));
        Assert.Single(tracker.DuePaused(script, TimeSpan.FromMinutes(10), Start.AddMinutes(32)));
    }

    [Fact]
    public void PausedForFiresSeparatelyForEachScript()
    {
        var tracker = new PlaybackTracker();
        tracker.Started("s", Args, Start);
        tracker.Progress("s", true, Args, Start);

        Assert.Single(tracker.DuePaused(Guid.NewGuid(), TimeSpan.FromMinutes(5), Start.AddMinutes(30)));
        Assert.Single(tracker.DuePaused(Guid.NewGuid(), TimeSpan.FromMinutes(5), Start.AddMinutes(30)));
    }

    [Fact]
    public void PlayingForCountsOnlyPlayingTimeAndFiresOnce()
    {
        var script = Guid.NewGuid();
        var tracker = new PlaybackTracker();
        tracker.Started("s", Args, Start);
        tracker.Progress("s", true, Args, Start.AddMinutes(30));
        tracker.Progress("s", false, Args, Start.AddMinutes(90));

        // 30 minutes played, an hour paused, then 20 more minutes played: 50 in all.
        Assert.Empty(tracker.DuePlaying(script, TimeSpan.FromMinutes(60), Start.AddMinutes(110)));
        Assert.Single(tracker.DuePlaying(script, TimeSpan.FromMinutes(60), Start.AddMinutes(120)));
        Assert.Empty(tracker.DuePlaying(script, TimeSpan.FromMinutes(60), Start.AddMinutes(200)));
    }

    [Fact]
    public void StopForgetsTheSession()
    {
        var tracker = new PlaybackTracker();
        tracker.Started("s", Args, Start);
        tracker.Progress("s", true, Args, Start);
        tracker.Stopped("s");

        Assert.Empty(tracker.DuePaused(Guid.NewGuid(), TimeSpan.Zero, Start.AddHours(1)));
    }
}
