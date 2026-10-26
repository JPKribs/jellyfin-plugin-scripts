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
    TaskCompleted,

    /// <summary>A session has stayed paused for the script's <see cref="ScriptDefinition.PausedForMinutes"/>.</summary>
    PlaybackPausedFor,

    /// <summary>A session has played for the script's <see cref="ScriptDefinition.PlayingForMinutes"/>, not counting pauses.</summary>
    PlaybackPlayingFor,

    /// <summary>An item's metadata or images changed.</summary>
    ItemUpdated,

    /// <summary>A collection was created.</summary>
    CollectionCreated,

    /// <summary>An item was added to a collection.</summary>
    ItemAddedToCollection,

    /// <summary>An item was removed from a collection.</summary>
    ItemRemovedFromCollection,

    /// <summary>A subtitle download failed.</summary>
    SubtitleDownloadFailed,

    /// <summary>A user marked an item played.</summary>
    MarkedPlayed,

    /// <summary>A user marked an item unplayed.</summary>
    MarkedUnplayed,

    /// <summary>A user added or removed an item as a favorite.</summary>
    FavoriteChanged,

    /// <summary>A client session began.</summary>
    SessionStarted,

    /// <summary>A client session ended.</summary>
    SessionEnded,

    /// <summary>A user account was created.</summary>
    UserCreated,

    /// <summary>A user account was deleted.</summary>
    UserDeleted,

    /// <summary>A user account was changed.</summary>
    UserUpdated,

    /// <summary>A user was locked out after too many failed sign ins.</summary>
    UserLockedOut,

    /// <summary>A user's password was changed.</summary>
    UserPasswordChanged,

    /// <summary>The server finished starting.</summary>
    ServerStarted,

    /// <summary>The server needs a restart to apply a change.</summary>
    PendingRestart,

    /// <summary>A scheduled task started.</summary>
    TaskStarted,

    /// <summary>A plugin was installed.</summary>
    PluginInstalled,

    /// <summary>A plugin was updated.</summary>
    PluginUpdated,

    /// <summary>A plugin was uninstalled.</summary>
    PluginUninstalled,

    /// <summary>The script's own schedule came due.</summary>
    Schedule,

    /// <summary>A metadata refresh of an item started.</summary>
    MetadataRefreshStarted,

    /// <summary>A metadata refresh of an item finished.</summary>
    MetadataRefreshCompleted,

    /// <summary>A lyrics download failed.</summary>
    LyricDownloadFailed,

    /// <summary>A library's settings changed.</summary>
    LibrarySettingsChanged,

    /// <summary>A Live TV recording was scheduled.</summary>
    RecordingScheduled,

    /// <summary>A scheduled Live TV recording was cancelled.</summary>
    RecordingCancelled,

    /// <summary>A Live TV series recording was scheduled.</summary>
    SeriesRecordingScheduled,

    /// <summary>A Live TV series recording was cancelled.</summary>
    SeriesRecordingCancelled,

    /// <summary>A client opened its live connection to the server.</summary>
    ClientConnected,

    /// <summary>A device's settings, such as its custom name, changed.</summary>
    DeviceSettingsChanged,

    /// <summary>An entry was written to the activity log, other than by this plugin.</summary>
    ActivityLogged,

    /// <summary>The server's settings changed.</summary>
    ServerSettingsChanged,

    /// <summary>The server's network interfaces or addresses changed.</summary>
    NetworkChanged,

    /// <summary>A plugin installation started.</summary>
    PluginInstalling,

    /// <summary>A plugin installation was cancelled.</summary>
    PluginInstallationCancelled
}
