namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// A server event a script can run on. The names are stored in configuration and passed to scripts as
/// <c>JF_EVENT</c>, so never rename one.
/// </summary>
public enum ScriptTrigger
{
    /// <summary>Playback began on a session.</summary>
    PlaybackStart,

    /// <summary>Playback ended on a session.</summary>
    PlaybackStop,

    /// <summary>A playing session paused.</summary>
    PlaybackPause,

    /// <summary>A paused session resumed.</summary>
    PlaybackResume,

    /// <summary>An item was added to a library, dispatched once its metadata has arrived.</summary>
    ItemAdded,

    /// <summary>An item was removed from a library.</summary>
    ItemRemoved,

    /// <summary>A user signed in.</summary>
    AuthenticationSuccess,

    /// <summary>A sign in attempt failed.</summary>
    AuthenticationFailure,

    /// <summary>A scheduled task finished, whatever its outcome.</summary>
    TaskCompleted
}
