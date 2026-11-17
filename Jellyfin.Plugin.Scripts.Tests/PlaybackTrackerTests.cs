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
        KeepReporting(tracker, "s", paused: true, fromMinute: 1, toMinute: 30);

        Assert.Single(tracker.DuePaused(Guid.NewGuid(), TimeSpan.FromMinutes(5), Start.AddMinutes(30)));
        Assert.Single(tracker.DuePaused(Guid.NewGuid(), TimeSpan.FromMinutes(5), Start.AddMinutes(30)));
    }

    [Fact]
    public void PlayingForCountsOnlyPlayingTimeAndFiresOnce()
    {
        var script = Guid.NewGuid();
        var tracker = new PlaybackTracker();
        tracker.Started("s", Args, Start);
        KeepReporting(tracker, "s", paused: false, fromMinute: 1, toMinute: 29);
        tracker.Progress("s", true, Args, Start.AddMinutes(30));
        KeepReporting(tracker, "s", paused: true, fromMinute: 31, toMinute: 89);
        tracker.Progress("s", false, Args, Start.AddMinutes(90));
        KeepReporting(tracker, "s", paused: false, fromMinute: 91, toMinute: 110);

        // 30 minutes played, an hour paused, then 20 more minutes played: 50 in all.
        Assert.Empty(tracker.DuePlaying(script, TimeSpan.FromMinutes(60), Start.AddMinutes(110)));
        KeepReporting(tracker, "s", paused: false, fromMinute: 111, toMinute: 120);
        Assert.Single(tracker.DuePlaying(script, TimeSpan.FromMinutes(60), Start.AddMinutes(120)));
        KeepReporting(tracker, "s", paused: false, fromMinute: 121, toMinute: 200);
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

    /// <summary>
    /// A playback that stops reporting without a stop is forgotten.
    /// True: it raises nothing later and its last event is let go.
    /// False: it would be kept, with its item, session and user, for as long as the server runs.
    /// </summary>
    [Fact]
    public void ASilentPlaybackIsForgotten()
    {
        var tracker = new PlaybackTracker();
        tracker.Started("gone", Args, Start);
        tracker.Started("watching", Args, Start);
        KeepReporting(tracker, "watching", paused: false, fromMinute: 1, toMinute: 30);

        var due = tracker.DuePlaying(Guid.NewGuid(), TimeSpan.FromMinutes(20), Start.AddMinutes(30));

        Assert.Single(due);
        Assert.Equal(1, tracker.Count);
    }

    /// <summary>
    /// A playback is kept for as long as it reports, however long it runs.
    /// True: a long film or a long pause is still followed.
    /// False: the "for a while" triggers would never fire on anything longer than the limit.
    /// </summary>
    [Fact]
    public void AReportingPlaybackIsKept()
    {
        var tracker = new PlaybackTracker();
        tracker.Started("s", Args, Start);
        tracker.Progress("s", true, Args, Start.AddMinutes(1));
        KeepReporting(tracker, "s", paused: true, fromMinute: 2, toMinute: 600);

        Assert.Single(tracker.DuePaused(Guid.NewGuid(), TimeSpan.FromHours(9), Start.AddMinutes(600)));
    }

    /// <summary>
    /// A playback silent for less than the limit is kept.
    /// True: a client that misses reports for a while is still followed.
    /// False: a short network drop would lose the playback's pause and play times.
    /// </summary>
    [Fact]
    public void AShortSilenceKeepsThePlayback()
    {
        var tracker = new PlaybackTracker();
        tracker.Started("s", Args, Start);

        var quiet = PlaybackTracker.AbandonedAfter - TimeSpan.FromSeconds(1);
        Assert.Single(tracker.DuePlaying(Guid.NewGuid(), TimeSpan.FromMinutes(1), Start + quiet));
    }

    // Reports once a minute, standing in for the reports a client that is still there keeps sending.
    private static void KeepReporting(PlaybackTracker tracker, string key, bool paused, int fromMinute, int toMinute)
    {
        for (var minute = fromMinute; minute <= toMinute; minute++)
        {
            tracker.Progress(key, paused, Args, Start.AddMinutes(minute));
        }
    }
}
