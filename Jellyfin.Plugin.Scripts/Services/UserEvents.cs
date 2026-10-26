using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Drops the second report of one account change. Jellyfin reports an update through its event bus, the user
/// manager's own event, or both, depending on what changed.
/// </summary>
public static class UserEvents
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<Guid, DateTime> LastDispatched = new();

    /// <summary>
    /// Gets whether an update of this user should run scripts, recording it when it should.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>False when the same user's update was dispatched moments ago.</returns>
    public static bool ShouldDispatch(Guid userId)
    {
        var now = DateTime.UtcNow;
        var allowed = true;
        LastDispatched.AddOrUpdate(userId, now, (_, last) =>
        {
            if (now - last < Window)
            {
                allowed = false;
                return last;
            }

            return now;
        });

        if (LastDispatched.Count > 1000)
        {
            LastDispatched.Clear();
        }

        return allowed;
    }
}
