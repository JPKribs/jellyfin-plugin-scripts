using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Follows each playback session between its start and stop: turns the progress reports a client sends every
/// few seconds into pause and resume edges, and keeps the time spent paused and playing so the "for a while"
/// triggers fire once per pause and once per playback.
/// </summary>
public sealed class PlaybackTracker
{
    // A session that never reports a stop would otherwise stay forever. Past this many the state is dropped,
    // which at worst misses one edge per active session.
    private const int MaxTracked = 5000;

    private readonly ConcurrentDictionary<string, PlaybackState> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Records that playback started, unpaused.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    /// <param name="args">The event, kept to build the "for a while" events later.</param>
    /// <param name="now">The current time.</param>
    public void Started(string key, PlaybackProgressEventArgs? args, DateTime now)
    {
        Trim();
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
            Trim();
            _sessions[key] = new PlaybackState(args, now, isPaused);
            return null;
        }

        return state.Report(isPaused, args, now);
    }

    /// <summary>
    /// Forgets a session once playback stops.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    public void Stopped(string key) => _sessions.TryRemove(key, out _);

    /// <summary>
    /// Finds sessions paused for at least <paramref name="threshold"/> that have not yet fired for
    /// <paramref name="scriptId"/> during this pause, and marks them fired.
    /// </summary>
    /// <param name="scriptId">The script asking.</param>
    /// <param name="threshold">How long a pause must last.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Each due session's last event and how long it has been paused.</returns>
    public IReadOnlyList<(PlaybackProgressEventArgs Args, TimeSpan Paused)> DuePaused(Guid scriptId, TimeSpan threshold, DateTime now)
        => _sessions.Values
            .Select(s => s.ClaimPaused(scriptId, threshold, now))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();

    /// <summary>
    /// Finds sessions that have played, not counting pauses, for at least <paramref name="threshold"/> and have
    /// not yet fired for <paramref name="scriptId"/> during this playback, and marks them fired.
    /// </summary>
    /// <param name="scriptId">The script asking.</param>
    /// <param name="threshold">How long playback must last.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Each due session's last event and how long it has played.</returns>
    public IReadOnlyList<(PlaybackProgressEventArgs Args, TimeSpan Played)> DuePlaying(Guid scriptId, TimeSpan threshold, DateTime now)
        => _sessions.Values
            .Select(s => s.ClaimPlaying(scriptId, threshold, now))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();

    private void Trim()
    {
        if (_sessions.Count >= MaxTracked)
        {
            _sessions.Clear();
        }
    }

    private sealed class PlaybackState
    {
        private readonly object _lock = new();
        private readonly HashSet<Guid> _firedThisPause = new();
        private readonly HashSet<Guid> _firedThisPlayback = new();
        private PlaybackProgressEventArgs? _args;
        private bool _isPaused;
        private DateTime _since;
        private TimeSpan _played;

        public PlaybackState(PlaybackProgressEventArgs? args, DateTime now, bool isPaused)
        {
            _args = args;
            _isPaused = isPaused;
            _since = now;
        }

        public bool? Report(bool isPaused, PlaybackProgressEventArgs? args, DateTime now)
        {
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
