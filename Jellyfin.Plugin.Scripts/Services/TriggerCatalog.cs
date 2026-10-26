using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// The single source for what each trigger supplies: the variables it fills, whether it carries an item, a
/// session, or a user, and therefore which condition fields apply to it. The editor, the validator, the
/// condition evaluator, and the test runs all read from here.
/// </summary>
public static class TriggerCatalog
{
    /// <summary>The prefix every variable carries in a script's environment.</summary>
    public const string EnvironmentPrefix = "JF_";

    /// <summary>The prefix a script secret's variable carries, so secrets cannot shadow event values.</summary>
    public const string SecretPrefix = "SECRET_";

    private static readonly VariableInfo[] Common =
    {
        new("EVENT", "The trigger that ran the script, such as PlaybackStart."),
        new("TIMESTAMP", "When the event happened, in UTC (ISO 8601)."),
        new("LOCAL_DATE", "The server's local date, yyyy-MM-dd."),
        new("LOCAL_TIME", "The server's local time, HH:mm:ss."),
        new("HOUR", "The server's local hour, 0 to 23."),
        new("DAY_OF_WEEK", "The server's local day of the week, such as Monday."),
        new("SCRIPT_NAME", "The script's name."),
        new("SERVER_NAME", "The server's name."),
        new("SERVER_ID", "The server's ID."),
        new("SERVER_VERSION", "The Jellyfin version."),
        new("EVENT_JSON", "Every other variable as one JSON object (environment only)."),
    };

    private static readonly VariableInfo[] User =
    {
        new("USER_ID", "The user's ID."),
        new("USER_NAME", "The user's name."),
        new("IS_ADMIN", "true when the user is an administrator, otherwise false."),
    };

    private static readonly VariableInfo[] Session =
    {
        new("SESSION_ID", "The session's ID."),
        new("CLIENT", "The client application, such as Jellyfin Web."),
        new("CLIENT_VERSION", "The client application's version."),
        new("DEVICE_ID", "The device's ID."),
        new("DEVICE_NAME", "The device's name."),
        new("REMOTE_ADDRESS", "The client's IP address."),
        new("IS_LOCAL", "true when the client is inside the local network, otherwise false."),
    };

    private static readonly VariableInfo[] Item =
    {
        new("ITEM_ID", "The item's ID."),
        new("ITEM_NAME", "The item's name."),
        new("ITEM_KIND", "The item's kind, such as Movie or Episode."),
        new("ITEM_PATH", "The item's file or folder path."),
        new("LIBRARY_ID", "The ID of the library holding the item."),
        new("LIBRARY_NAME", "The name of the library holding the item."),
        new("YEAR", "The production year."),
        new("RUNTIME_SECONDS", "The runtime in seconds."),
        new("SERIES_NAME", "The series, for an episode or season."),
        new("SEASON_NUMBER", "The season number, for an episode."),
        new("EPISODE_NUMBER", "The episode number, or a track number for audio."),
        new("ALBUM", "The album, for audio."),
        new("ARTIST", "The artists, comma separated, for audio."),
        new("GENRES", "The genres, comma separated."),
        new("TAGS", "The tags, comma separated."),
        new("STUDIOS", "The studios, comma separated."),
        new("OFFICIAL_RATING", "The parental rating, such as PG-13."),
        new("COMMUNITY_RATING", "The community rating."),
        new("VIDEO_WIDTH", "The video width in pixels."),
        new("VIDEO_HEIGHT", "The video height in pixels."),
        new("DATE_ADDED", "When the item was added, in UTC (ISO 8601)."),
        new("PROVIDER_IMDB", "The IMDb ID."),
        new("PROVIDER_TMDB", "The TMDb ID."),
        new("PROVIDER_TVDB", "The TVDb ID."),
    };

    private static readonly VariableInfo[] PlaybackVars =
    {
        new("PLAY_SESSION_ID", "The playback session's ID."),
        new("PLAY_METHOD", "DirectPlay, DirectStream, or Transcode. On stop, the method seen while playing."),
        new("POSITION_SECONDS", "The playback position in seconds."),
        new("PROGRESS_PERCENT", "The playback position as a percentage of the runtime."),
        new("IS_PAUSED", "true when playback is paused, otherwise false."),
        new("VIDEO_CODEC", "The video codec sent to the client, when transcoding."),
        new("AUDIO_CODEC", "The audio codec sent to the client, when transcoding."),
        new("TRANSCODE_REASONS", "Why the server is transcoding, comma separated."),
    };

    private static readonly VariableInfo[] Stop = { new("PLAYED_TO_COMPLETION", "true when playback reached the end, otherwise false.") };

    private static readonly VariableInfo[] Paused = { new("PAUSED_SECONDS", "How long the session has been paused, in seconds.") };

    private static readonly VariableInfo[] Playing = { new("PLAYING_SECONDS", "How long the session has played, not counting pauses, in seconds.") };

    private static readonly VariableInfo[] UserData =
    {
        new("IS_PLAYED", "true when the item is now played, otherwise false."),
        new("IS_FAVORITE", "true when the item is now a favorite, otherwise false."),
        new("PLAY_COUNT", "How many times the user has played the item."),
    };

    private static readonly VariableInfo[] Collection =
    {
        new("COLLECTION_ID", "The collection's ID."),
        new("COLLECTION_NAME", "The collection's name."),
    };

    private static readonly VariableInfo[] Update = { new("UPDATE_REASON", "Why the item changed: MetadataImport, ImageUpdate, MetadataDownload, or MetadataEdit.") };

    private static readonly VariableInfo[] Failure =
    {
        new("PROVIDER", "The provider that failed."),
        new("ERROR_MESSAGE", "Why it failed."),
    };

    private static readonly VariableInfo[] Streams =
    {
        new("ACTIVE_STREAMS", "How many sessions are playing something, counting this one."),
        new("ACTIVE_TRANSCODES", "How many sessions are transcoding, counting this one."),
    };

    private static readonly VariableInfo[] Recording =
    {
        new("TIMER_ID", "The recording timer's ID."),
        new("PROGRAM_ID", "The program's ID, when it is known."),
    };

    private static readonly VariableInfo[] Activity =
    {
        new("ACTIVITY_NAME", "The entry's name."),
        new("ACTIVITY_TYPE", "The entry's type, such as VideoPlayback or SessionStarted."),
        new("ACTIVITY_OVERVIEW", "The entry's details."),
        new("ACTIVITY_SEVERITY", "Information, Warning, Error, and so on."),
        new("USER_ID", "The ID of the user the entry is about, when it has one."),
    };

    private static readonly VariableInfo[] Device =
    {
        new("DEVICE_ID", "The device's ID."),
        new("DEVICE_NAME", "The device's custom name."),
    };

    private static readonly VariableInfo[] LibrarySettings = { new("LIBRARY_PATH", "The library's folder.") };

    private static readonly VariableInfo[] Settings = { new("SETTINGS_KEY", "Which settings changed, such as encoding or network. Empty for the main settings.") };

    private static readonly VariableInfo[] AuthFailure =
    {
        new("USERNAME_ATTEMPTED", "The username that was typed."),
        new("USER_ID", "The ID of the matching user."),
    };

    private static readonly VariableInfo[] TaskStart =
    {
        new("TASK_NAME", "The scheduled task's name."),
        new("TASK_KEY", "The scheduled task's key."),
        new("TASK_CATEGORY", "The scheduled task's category."),
    };

    private static readonly VariableInfo[] TaskEnd =
    {
        new("TASK_STATUS", "Completed, Failed, Cancelled, or Aborted."),
        new("TASK_ERROR", "The error message, when the task failed."),
        new("TASK_DURATION_SECONDS", "How long the task ran, in seconds."),
    };

    private static readonly VariableInfo[] PluginVars =
    {
        new("PLUGIN_ID", "The plugin's ID."),
        new("PLUGIN_NAME", "The plugin's name."),
        new("PLUGIN_VERSION", "The plugin's version."),
    };

    private static readonly Dictionary<ConditionField, string> FieldVariables = new()
    {
        [ConditionField.User] = "USER_ID",
        [ConditionField.Library] = "LIBRARY_ID",
        [ConditionField.ItemName] = "ITEM_NAME",
        [ConditionField.SeriesName] = "SERIES_NAME",
        [ConditionField.Client] = "CLIENT",
        [ConditionField.DeviceName] = "DEVICE_NAME",
        [ConditionField.PlayMethod] = "PLAY_METHOD",
        [ConditionField.IsLocal] = "IS_LOCAL",
        [ConditionField.PlayedToCompletion] = "PLAYED_TO_COMPLETION",
        [ConditionField.UsernameAttempted] = "USERNAME_ATTEMPTED",
        [ConditionField.TaskKey] = "TASK_KEY",
        [ConditionField.TaskStatus] = "TASK_STATUS",
        [ConditionField.Hour] = "HOUR",
        [ConditionField.DayOfWeek] = "DAY_OF_WEEK",
        [ConditionField.ItemPath] = "ITEM_PATH",
        [ConditionField.Genre] = "GENRES",
        [ConditionField.Tag] = "TAGS",
        [ConditionField.Studio] = "STUDIOS",
        [ConditionField.OfficialRating] = "OFFICIAL_RATING",
        [ConditionField.Year] = "YEAR",
        [ConditionField.VideoHeight] = "VIDEO_HEIGHT",
        [ConditionField.CommunityRating] = "COMMUNITY_RATING",
        [ConditionField.ProgressPercent] = "PROGRESS_PERCENT",
        [ConditionField.TranscodeReasons] = "TRANSCODE_REASONS",
        [ConditionField.VideoCodec] = "VIDEO_CODEC",
        [ConditionField.AudioCodec] = "AUDIO_CODEC",
        [ConditionField.RemoteAddress] = "REMOTE_ADDRESS",
        [ConditionField.DeviceId] = "DEVICE_ID",
        [ConditionField.ClientVersion] = "CLIENT_VERSION",
        [ConditionField.IsAdmin] = "IS_ADMIN",
        [ConditionField.TaskCategory] = "TASK_CATEGORY",
        [ConditionField.PluginName] = "PLUGIN_NAME",
        [ConditionField.IsFavorite] = "IS_FAVORITE",
        [ConditionField.IsPlayed] = "IS_PLAYED",
        [ConditionField.CollectionName] = "COLLECTION_NAME",
        [ConditionField.Provider] = "PROVIDER",
        [ConditionField.UpdateReason] = "UPDATE_REASON",
        [ConditionField.ActiveTranscodes] = "ACTIVE_TRANSCODES",
        [ConditionField.ActiveStreams] = "ACTIVE_STREAMS",
        [ConditionField.ActivityType] = "ACTIVITY_TYPE",
        [ConditionField.ActivitySeverity] = "ACTIVITY_SEVERITY",
    };

    private static readonly Dictionary<ScriptTrigger, TriggerInfo> Triggers = new[]
    {
        PlaybackTrigger(ScriptTrigger.PlaybackStart, "Playback started", "A client starts playing something."),
        PlaybackTrigger(ScriptTrigger.PlaybackStop, "Playback stopped", "A client stops playing.", Stop),
        PlaybackTrigger(ScriptTrigger.PlaybackPause, "Playback paused", "A playing client pauses."),
        PlaybackTrigger(ScriptTrigger.PlaybackResume, "Playback resumed", "A paused client resumes."),
        PlaybackTrigger(ScriptTrigger.PlaybackPausedFor, "Paused for a while", "A client stays paused for the minutes you set, once per pause.", Paused),
        PlaybackTrigger(ScriptTrigger.PlaybackPlayingFor, "Playing for a while", "A client plays for the minutes you set, not counting pauses, once per playback.", Playing),

        Build(ScriptTrigger.ItemAdded, "Item added", "Library", "An item arrives in a library, once its metadata is in.", true, false, Item),
        Build(ScriptTrigger.ItemUpdated, "Item updated", "Library", "An item's metadata or images change. Busy during scans.", true, false, Item, Update),
        Build(ScriptTrigger.ItemRemoved, "Item removed", "Library", "An item leaves a library.", true, false, Item),
        Build(ScriptTrigger.CollectionCreated, "Collection created", "Library", "A new collection is made.", true, false, Item, Collection),
        Build(ScriptTrigger.ItemAddedToCollection, "Added to collection", "Library", "An item is added to a collection.", true, false, Item, Collection),
        Build(ScriptTrigger.ItemRemovedFromCollection, "Removed from collection", "Library", "An item is removed from a collection.", true, false, Item, Collection),
        Build(ScriptTrigger.SubtitleDownloadFailed, "Subtitle download failed", "Library", "A subtitle provider fails to download subtitles.", true, false, Item, Failure),
        Build(ScriptTrigger.LyricDownloadFailed, "Lyrics download failed", "Library", "A lyrics provider fails to download lyrics.", true, false, Item, Failure),
        Build(ScriptTrigger.MetadataRefreshStarted, "Metadata refresh started", "Library", "A metadata refresh of an item starts.", true, false, Item),
        Build(ScriptTrigger.MetadataRefreshCompleted, "Metadata refresh finished", "Library", "A metadata refresh of an item finishes.", true, false, Item),
        Build(ScriptTrigger.LibrarySettingsChanged, "Library settings changed", "Library", "A library's settings are saved.", false, false, LibrarySettings),

        Build(ScriptTrigger.RecordingScheduled, "Recording scheduled", "Live TV", "A Live TV recording is scheduled.", true, false, Recording, Item),
        Build(ScriptTrigger.RecordingCancelled, "Recording cancelled", "Live TV", "A scheduled Live TV recording is cancelled.", true, false, Recording, Item),
        Build(ScriptTrigger.SeriesRecordingScheduled, "Series recording scheduled", "Live TV", "A Live TV series recording is scheduled.", true, false, Recording, Item),
        Build(ScriptTrigger.SeriesRecordingCancelled, "Series recording cancelled", "Live TV", "A Live TV series recording is cancelled.", true, false, Recording, Item),

        Build(ScriptTrigger.MarkedPlayed, "Marked played", "Activity", "A user marks an item played.", true, false, User, Item, UserData),
        Build(ScriptTrigger.MarkedUnplayed, "Marked unplayed", "Activity", "A user marks an item unplayed.", true, false, User, Item, UserData),
        Build(ScriptTrigger.FavoriteChanged, "Favorite changed", "Activity", "A user adds or removes a favorite. IS_FAVORITE says which.", true, false, User, Item, UserData),

        Build(ScriptTrigger.SessionStarted, "Session started", "Sessions", "A client connects and starts a session.", false, true, User, Session),
        Build(ScriptTrigger.SessionEnded, "Session ended", "Sessions", "A client session ends.", false, true, User, Session),
        Build(ScriptTrigger.AuthenticationSuccess, "Signed in", "Sessions", "A user signs in.", false, true, User, Session),
        Build(ScriptTrigger.ClientConnected, "Client connected", "Sessions", "A client opens its live connection, so it can receive messages and commands.", false, true, User, Session),
        Build(ScriptTrigger.DeviceSettingsChanged, "Device settings changed", "Sessions", "A device's settings, such as its custom name, change.", false, false, Device),
        Build(ScriptTrigger.AuthenticationFailure, "Sign in failed", "Sessions", "A sign in fails for an existing user.", false, false, AuthFailure, Session.Where(v => v.Name != "SESSION_ID")),

        Build(ScriptTrigger.UserCreated, "User created", "Accounts", "A user account is created.", false, false, User),
        Build(ScriptTrigger.UserDeleted, "User deleted", "Accounts", "A user account is deleted.", false, false, User),
        Build(ScriptTrigger.UserUpdated, "User edited", "Accounts", "A user's name or profile is edited. Jellyfin does not report permission changes.", false, false, User),
        Build(ScriptTrigger.UserLockedOut, "User locked out", "Accounts", "A user is locked out after too many failed sign ins.", false, false, User),
        Build(ScriptTrigger.UserPasswordChanged, "Password changed", "Accounts", "A user's password changes.", false, false, User),

        Build(ScriptTrigger.ServerStarted, "Server started", "System", "The server finishes starting.", false, false),
        Build(ScriptTrigger.PendingRestart, "Restart needed", "System", "The server needs a restart to apply a change.", false, false),
        Build(ScriptTrigger.TaskStarted, "Scheduled task started", "System", "A scheduled task starts.", false, false, TaskStart),
        Build(ScriptTrigger.TaskCompleted, "Scheduled task finished", "System", "A scheduled task finishes, whatever its outcome.", false, false, TaskStart, TaskEnd),
        Build(ScriptTrigger.ActivityLogged, "Activity logged", "System", "Any entry is written to the activity log, by Jellyfin or another plugin.", false, false, Activity),
        Build(ScriptTrigger.ServerSettingsChanged, "Server settings changed", "System", "The server's settings are saved.", false, false, Settings),
        Build(ScriptTrigger.NetworkChanged, "Network changed", "System", "The server's network interfaces or addresses change.", false, false),
        Build(ScriptTrigger.PluginInstalling, "Plugin installing", "System", "A plugin starts installing.", false, false, PluginVars),
        Build(ScriptTrigger.PluginInstallationCancelled, "Plugin install cancelled", "System", "A plugin installation is cancelled.", false, false, PluginVars),
        Build(ScriptTrigger.PluginInstalled, "Plugin installed", "System", "A plugin is installed.", false, false, PluginVars),
        Build(ScriptTrigger.PluginUpdated, "Plugin updated", "System", "A plugin is updated.", false, false, PluginVars),
        Build(ScriptTrigger.PluginUninstalled, "Plugin uninstalled", "System", "A plugin is uninstalled.", false, false, PluginVars),

        Build(ScriptTrigger.Schedule, "On a schedule", "Schedule", "Every few minutes, or daily at a set time on the days you pick.", false, false),
    }.ToDictionary(t => t.Trigger);

    /// <summary>Gets every trigger, in editor order.</summary>
    public static IReadOnlyList<TriggerInfo> All { get; } = Triggers.Values.ToList();

    /// <summary>
    /// Gets what a trigger supplies.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <returns>The trigger's description.</returns>
    public static TriggerInfo Get(ScriptTrigger trigger) => Triggers[trigger];

    /// <summary>
    /// Gets the variable a condition field reads.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The variable name without the prefix.</returns>
    public static string VariableFor(ConditionField field) => FieldVariables[field];

    /// <summary>
    /// Gets whether a trigger supplies the value a condition field reads.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="field">The field.</param>
    /// <returns>True when the field applies to the trigger.</returns>
    public static bool Supplies(ScriptTrigger trigger, ConditionField field)
    {
        var name = VariableFor(field);
        return Get(trigger).Variables.Any(v => v.Name == name);
    }

    private static TriggerInfo PlaybackTrigger(ScriptTrigger trigger, string label, string description, params VariableInfo[] extra)
        => Build(trigger, label, "Playback", description, true, true, User, Session, Item, PlaybackVars, Streams, extra);

    private static TriggerInfo Build(ScriptTrigger trigger, string label, string category, string description, bool hasItem, bool hasSession, params IEnumerable<VariableInfo>[] groups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var variables = Common.Concat(groups.SelectMany(g => g)).Where(v => seen.Add(v.Name)).ToList();
        return new TriggerInfo(trigger, label, category, description, hasItem, hasSession, variables);
    }
}
