using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Follows each playback session between its start and stop: turns the progress reports a client sends every
/// few seconds into pause and resume edges, and keeps the time spent paused and playing so the "for a while"
/// triggers fire once per pause and once per playback.
/// </summary>
public sealed class PlaybackTracker
{
    /// <summary>
    /// How long a playback may go without a report before it is forgotten. Jellyfin itself ends a playback that
    /// sends no report for five to ten minutes, so one silent for this long has no viewer. Its last event holds
    /// the item, the session and the user.
    /// </summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(15);

    // The backstop for a burst of playbacks. Past this many the state is dropped, which at worst misses one edge
    // per active session.
    private const int MaxTracked = 5000;

    // How often the abandoned playbacks are looked for.
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);

    private long _lastSweepTicks;

    private const string TranscodeMethod = "Transcode";

    private readonly ConcurrentDictionary<string, PlaybackState> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Records that playback started, unpaused.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    /// <param name="args">The event, kept to build the "for a while" events later.</param>
    /// <param name="now">The current time.</param>
    public void Started(string key, PlaybackProgressEventArgs? args, DateTime now)
    {
        Trim(now);
        _sessions[key] = new PlaybackState(args, now, isPaused: false);
    }

    /// <summary>
    /// Records a progress report.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    /// <param name="isPaused">Whether the report says paused.</param>
    /// <param name="args">The event.</param>
    /// <param name="now">The current time.</param>
    /// <returns>True when playback just paused, false when it just resumed, null when nothing changed.</returns>
    public bool? Progress(string key, bool isPaused, PlaybackProgressEventArgs? args, DateTime now)
    {
        if (!_sessions.TryGetValue(key, out var state))
        {
            // First report for a session already playing when the server or plugin started: there is no
            // earlier state to compare, so record it without an edge.
            Trim(now);
            _sessions[key] = new PlaybackState(args, now, isPaused);
            return null;
        }

        return state.Report(isPaused, args, now);
    }

    /// <summary>
    /// Forgets a session once playback stops.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    /// <returns>The play method seen while it played, since the server clears it before reporting the stop.</returns>
    public string? Stopped(string key) => _sessions.TryRemove(key, out var state) ? state.PlayMethod : null;

    /// <summary>
    /// Records a session's play method, so the stop event can report it.
    /// </summary>
    /// <param name="key">The playback's key.</param>
    /// <param name="playMethod">The play method.</param>
    /// <returns>True when a playback that was playing directly has just started transcoding.</returns>
    public bool RememberPlayMethod(string key, string playMethod)
    {
        if (!_sessions.TryGetValue(key, out var state))
        {
            return false;
        }

        var previous = state.PlayMethod;
        state.PlayMethod = playMethod;
        return previous is not null
            && !string.Equals(previous, TranscodeMethod, StringComparison.Ordinal)
            && string.Equals(playMethod, TranscodeMethod, StringComparison.Ordinal);
    }

    /// <summary>
    /// Records how far through its item a playback is.
    /// </summary>
    /// <param name="key">The playback's key.</param>
    /// <param name="percent">The position as a percentage of the runtime.</param>
    /// <returns>The percentage last recorded, or null when this is the first.</returns>
    public double? AdvanceProgress(string key, double percent)
        => _sessions.TryGetValue(key, out var state) ? state.Advance(percent) : null;

    /// <summary>
    /// Claims a playback's one run of a script's percentage trigger.
    /// </summary>
    /// <param name="key">The playback's key.</param>
    /// <param name="scriptId">The script.</param>
    /// <returns>True the first time per playback.</returns>
    public bool ClaimProgress(string key, Guid scriptId)
        => _sessions.TryGetValue(key, out var state) && state.ClaimProgress(scriptId);

    /// <summary>
    /// Gets the play method last seen for a session.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    /// <returns>The play method, or null when none was seen.</returns>
    public string? PlayMethodOf(string key) => _sessions.TryGetValue(key, out var state) ? state.PlayMethod : null;

    /// <summary>
    /// Finds sessions paused for at least <paramref name="threshold"/> that have not yet fired for
    /// <paramref name="scriptId"/> during this pause, and marks them fired.
    /// </summary>
    /// <param name="scriptId">The script asking.</param>
    /// <param name="threshold">How long a pause must last.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Each due session's last event and how long it has been paused.</returns>
    public IReadOnlyList<(PlaybackProgressEventArgs Args, TimeSpan Paused)> DuePaused(Guid scriptId, TimeSpan threshold, DateTime now)
    {
        Trim(now);
        return _sessions.Values
            .Select(s => s.ClaimPaused(scriptId, threshold, now))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();
    }

    /// <summary>
    /// Finds sessions that have played, not counting pauses, for at least <paramref name="threshold"/> and have
    /// not yet fired for <paramref name="scriptId"/> during this playback, and marks them fired.
    /// </summary>
    /// <param name="scriptId">The script asking.</param>
    /// <param name="threshold">How long playback must last.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Each due session's last event and how long it has played.</returns>
    public IReadOnlyList<(PlaybackProgressEventArgs Args, TimeSpan Played)> DuePlaying(Guid scriptId, TimeSpan threshold, DateTime now)
    {
        Trim(now);
        return _sessions.Values
            .Select(s => s.ClaimPlaying(scriptId, threshold, now))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();
    }

    /// <summary>Gets how many playbacks are being followed.</summary>
    public int Count => _sessions.Count;

    // Forgets the playbacks that stopped reporting without a stop, at most once per sweep interval.
    private void Trim(DateTime now)
    {
        if (_sessions.Count >= MaxTracked)
        {
            _sessions.Clear();
            return;
        }

        var last = Interlocked.Read(ref _lastSweepTicks);
        if (now.Ticks - last < SweepEvery.Ticks || Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, last) != last)
        {
            return;
        }

        foreach (var pair in _sessions)
        {
            if (now - pair.Value.LastReport > AbandonedAfter)
            {
                _sessions.TryRemove(pair);
            }
        }
    }

    private sealed class PlaybackState
    {
        private readonly object _lock = new();
        private readonly HashSet<Guid> _firedThisPause = new();
        private readonly HashSet<Guid> _firedThisPlayback = new();
        private readonly HashSet<Guid> _firedProgress = new();
        private double? _percent;
        private PlaybackProgressEventArgs? _args;
        private bool _isPaused;
        private DateTime _since;
        private TimeSpan _played;

        private long _lastReportTicks;

        public string? PlayMethod { get; set; }

        public DateTime LastReport => new(Interlocked.Read(ref _lastReportTicks), DateTimeKind.Utc);

        public PlaybackState(PlaybackProgressEventArgs? args, DateTime now, bool isPaused)
        {
            _args = args;
            _isPaused = isPaused;
            _since = now;
            _lastReportTicks = now.Ticks;
        }

        public bool? Report(bool isPaused, PlaybackProgressEventArgs? args, DateTime now)
        {
            Interlocked.Exchange(ref _lastReportTicks, now.Ticks);
            lock (_lock)
            {
                _args = args ?? _args;
                if (isPaused == _isPaused)
                {
                    return null;
                }

                if (isPaused)
                {
                    _played += now - _since;
                }
                else
                {
                    _firedThisPause.Clear();
                }

                _isPaused = isPaused;
                _since = now;
                return isPaused;
            }
        }

        public double? Advance(double percent)
        {
            lock (_lock)
            {
                var previous = _percent;
                _percent = percent;
                return previous;
            }
        }

        public bool ClaimProgress(Guid scriptId)
        {
            lock (_lock)
            {
                return _firedProgress.Add(scriptId);
            }
        }

        public (PlaybackProgressEventArgs Args, TimeSpan Paused)? ClaimPaused(Guid scriptId, TimeSpan threshold, DateTime now)
        {
            lock (_lock)
            {
                var paused = now - _since;
                if (!_isPaused || _args is null || paused < threshold || !_firedThisPause.Add(scriptId))
                {
                    return null;
                }

                return (_args, paused);
            }
        }

        public (PlaybackProgressEventArgs Args, TimeSpan Played)? ClaimPlaying(Guid scriptId, TimeSpan threshold, DateTime now)
        {
            lock (_lock)
            {
                var played = _played + (_isPaused ? TimeSpan.Zero : now - _since);
                if (_args is null || played < threshold || !_firedThisPlayback.Add(scriptId))
                {
                    return null;
                }

                return (_args, played);
            }
        }
    }
}
