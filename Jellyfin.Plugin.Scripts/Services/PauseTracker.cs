using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Turns the progress reports a client sends every few seconds into pause and resume edges, one per change,
/// keyed by playback session.
/// </summary>
public sealed class PauseTracker
{
    // A session that never reports a stop would otherwise stay forever. Past this many the state is dropped,
    // which at worst misses one edge per active session.
    private const int MaxTracked = 5000;

    private readonly ConcurrentDictionary<string, bool> _paused = new();

    /// <summary>
    /// Records that playback started, unpaused.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    public void Started(string key)
    {
        Trim();
        _paused[key] = false;
    }

    /// <summary>
    /// Records a progress report.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    /// <param name="isPaused">Whether the report says paused.</param>
    /// <returns>True when playback just paused, false when it just resumed, null when nothing changed.</returns>
    public bool? Progress(string key, bool isPaused)
    {
        if (!_paused.TryGetValue(key, out var was))
        {
            // First report for a session already playing when the server or plugin started: there is no
            // earlier state to compare, so record it without an edge.
            Trim();
            _paused[key] = isPaused;
            return null;
        }

        if (was == isPaused || !_paused.TryUpdate(key, isPaused, was))
        {
            return null;
        }

        return isPaused;
    }

    /// <summary>
    /// Forgets a session once playback stops.
    /// </summary>
    /// <param name="key">The playback session key.</param>
    public void Stopped(string key) => _paused.TryRemove(key, out _);

    private void Trim()
    {
        if (_paused.Count >= MaxTracked)
        {
            _paused.Clear();
        }
    }
}
