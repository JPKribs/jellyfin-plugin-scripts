using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Remembers which scheduled tasks a script queued, so a task event caused by a script can never queue another
/// task. Without it, a script on a task starting or finishing could queue tasks that set each other off forever.
/// </summary>
public static class TaskChains
{
    // A queued task normally runs within minutes. A mark older than this belongs to a run that never came.
    private static readonly TimeSpan Expiry = TimeSpan.FromHours(6);
    private static readonly ConcurrentDictionary<string, DateTime> Queued = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records that a script queued a task.
    /// </summary>
    /// <param name="key">The task key.</param>
    public static void MarkQueued(string key) => Queued[key] = DateTime.UtcNow;

    /// <summary>
    /// Gets whether the running or starting task was queued by a script.
    /// </summary>
    /// <param name="key">The task key.</param>
    /// <returns>True when a script queued it.</returns>
    public static bool WasQueued(string? key)
        => !string.IsNullOrEmpty(key) && Queued.TryGetValue(key, out var at) && DateTime.UtcNow - at < Expiry;

    /// <summary>
    /// Gets whether the finished task was queued by a script, and forgets it.
    /// </summary>
    /// <param name="key">The task key.</param>
    /// <returns>True when a script queued it.</returns>
    public static bool Finished(string? key)
    {
        if (string.IsNullOrEmpty(key) || !Queued.TryRemove(key, out var at))
        {
            return false;
        }

        return DateTime.UtcNow - at < Expiry;
    }
}
