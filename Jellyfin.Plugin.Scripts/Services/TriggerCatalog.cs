using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;

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

    /// <summary>The prefix every variable read from a webhook request carries.</summary>
    public const string WebhookPrefix = "WEBHOOK_";

    private static HashSet<string>? _reserved;

    private static readonly VariableInfo[] Common =
    {
        new("EVENT", TranslationKeys.VariableEvent),
        new("TIMESTAMP", TranslationKeys.VariableTimestamp),
        new("LOCAL_DATE", TranslationKeys.VariableLocalDate),
        new("LOCAL_TIME", TranslationKeys.VariableLocalTime),
        new("HOUR", TranslationKeys.VariableHour),
        new("DAY_OF_WEEK", TranslationKeys.VariableDayOfWeek),
        new("SCRIPT_NAME", TranslationKeys.VariableScriptName),
        new("SERVER_NAME", TranslationKeys.VariableServerName),
        new("SERVER_ID", TranslationKeys.VariableServerId),
        new("SERVER_VERSION", TranslationKeys.VariableServerVersion),
        new("EVENT_JSON", TranslationKeys.VariableEventJson),
    };

    private static readonly VariableInfo[] User =
    {
        new("USER_ID", TranslationKeys.VariableUserId),
        new("USER_NAME", TranslationKeys.VariableUserName),
        new("IS_ADMIN", TranslationKeys.VariableIsAdmin),
    };

    private static readonly VariableInfo[] Session =
    {
        new("SESSION_ID", TranslationKeys.VariableSessionId),
        new("CLIENT", TranslationKeys.VariableClient),
        new("CLIENT_VERSION", TranslationKeys.VariableClientVersion),
        new("DEVICE_ID", TranslationKeys.VariableDeviceId),
        new("DEVICE_NAME", TranslationKeys.VariableDeviceName),
        new("REMOTE_ADDRESS", TranslationKeys.VariableRemoteAddress),
        new("IS_LOCAL", TranslationKeys.VariableIsLocal),
    };

    private static readonly VariableInfo[] Item =
    {
        new("ITEM_ID", TranslationKeys.VariableItemId),
        new("ITEM_NAME", TranslationKeys.VariableItemName),
        new("ITEM_KIND", TranslationKeys.VariableItemKind),
        new("ITEM_PATH", TranslationKeys.VariableItemPath),
        new("LIBRARY_ID", TranslationKeys.VariableLibraryId),
        new("LIBRARY_NAME", TranslationKeys.VariableLibraryName),
        new("YEAR", TranslationKeys.VariableYear),
        new("RUNTIME_SECONDS", TranslationKeys.VariableRuntimeSeconds),
        new("SERIES_NAME", TranslationKeys.VariableSeriesName),
        new("SEASON_NUMBER", TranslationKeys.VariableSeasonNumber),
        new("EPISODE_NUMBER", TranslationKeys.VariableEpisodeNumber),
        new("ALBUM", TranslationKeys.VariableAlbum),
        new("ARTIST", TranslationKeys.VariableArtist),
        new("GENRES", TranslationKeys.VariableGenres),
        new("TAGS", TranslationKeys.VariableTags),
        new("STUDIOS", TranslationKeys.VariableStudios),
        new("OFFICIAL_RATING", TranslationKeys.VariableOfficialRating),
        new("COMMUNITY_RATING", TranslationKeys.VariableCommunityRating),
        new("VIDEO_WIDTH", TranslationKeys.VariableVideoWidth),
        new("VIDEO_HEIGHT", TranslationKeys.VariableVideoHeight),
        new("DATE_ADDED", TranslationKeys.VariableDateAdded),
        new("DAYS_SINCE_ADDED", TranslationKeys.VariableDaysSinceAdded),
        new("HAS_SUBTITLES", TranslationKeys.VariableHasSubtitles),
        new("PROVIDER_IMDB", TranslationKeys.VariableProviderImdb),
        new("PROVIDER_TMDB", TranslationKeys.VariableProviderTmdb),
        new("PROVIDER_TVDB", TranslationKeys.VariableProviderTvdb),
    };

    private static readonly VariableInfo[] PlaybackVars =
    {
        new("PLAY_SESSION_ID", TranslationKeys.VariablePlaySessionId),
        new("PLAY_METHOD", TranslationKeys.VariablePlayMethod),
        new("POSITION_SECONDS", TranslationKeys.VariablePositionSeconds),
        new("PROGRESS_PERCENT", TranslationKeys.VariableProgressPercent),
        new("IS_PAUSED", TranslationKeys.VariableIsPaused),
        new("VIDEO_CODEC", TranslationKeys.VariableVideoCodec),
        new("AUDIO_CODEC", TranslationKeys.VariableAudioCodec),
        new("TRANSCODE_REASONS", TranslationKeys.VariableTranscodeReasons),
    };

    private static readonly VariableInfo[] ForEachItem =
    {
        new("PLAYED_BY_ANYONE", TranslationKeys.VariablePlayedByAnyone),
        new("DAYS_SINCE_PLAYED", TranslationKeys.VariableDaysSincePlayed),
    };

    private static readonly VariableInfo[] ForEachUser =
    {
        new("DAYS_SINCE_ACTIVE", TranslationKeys.VariableDaysSinceActive),
        new("IS_DISABLED", TranslationKeys.VariableIsDisabled),
    };

    private static readonly VariableInfo[] WebhookVars =
    {
        new("WEBHOOK_BODY", TranslationKeys.VariableWebhookBody),
        new("REMOTE_ADDRESS", TranslationKeys.VariableRemoteAddressWebhookVars),
        new("IS_LOCAL", TranslationKeys.VariableIsLocalWebhookVars),
    };

    private static readonly VariableInfo[] Stop = { new("PLAYED_TO_COMPLETION", TranslationKeys.VariablePlayedToCompletion) };

    private static readonly VariableInfo[] Paused = { new("PAUSED_SECONDS", TranslationKeys.VariablePausedSeconds) };

    private static readonly VariableInfo[] Playing = { new("PLAYING_SECONDS", TranslationKeys.VariablePlayingSeconds) };

    private static readonly VariableInfo[] UserData =
    {
        new("IS_PLAYED", TranslationKeys.VariableIsPlayed),
        new("IS_FAVORITE", TranslationKeys.VariableIsFavorite),
        new("PLAY_COUNT", TranslationKeys.VariablePlayCount),
    };

    private static readonly VariableInfo[] Collection =
    {
        new("COLLECTION_ID", TranslationKeys.VariableCollectionId),
        new("COLLECTION_NAME", TranslationKeys.VariableCollectionName),
    };

    private static readonly VariableInfo[] Update = { new("UPDATE_REASON", TranslationKeys.VariableUpdateReason) };

    private static readonly VariableInfo[] Failure =
    {
        new("PROVIDER", TranslationKeys.VariableProvider),
        new("ERROR_MESSAGE", TranslationKeys.VariableErrorMessage),
    };

    private static readonly VariableInfo[] Streams =
    {
        new("ACTIVE_STREAMS", TranslationKeys.VariableActiveStreams),
        new("ACTIVE_TRANSCODES", TranslationKeys.VariableActiveTranscodes),
    };

    private static readonly VariableInfo[] Recording =
    {
        new("TIMER_ID", TranslationKeys.VariableTimerId),
        new("PROGRAM_ID", TranslationKeys.VariableProgramId),
    };

    private static readonly VariableInfo[] Activity =
    {
        new("ACTIVITY_NAME", TranslationKeys.VariableActivityName),
        new("ACTIVITY_TYPE", TranslationKeys.VariableActivityType),
        new("ACTIVITY_OVERVIEW", TranslationKeys.VariableActivityOverview),
        new("ACTIVITY_SEVERITY", TranslationKeys.VariableActivitySeverity),
        new("USER_ID", TranslationKeys.VariableUserIdActivity),
    };

    private static readonly VariableInfo[] Device =
    {
        new("DEVICE_ID", TranslationKeys.VariableDeviceId),
        new("DEVICE_NAME", TranslationKeys.VariableDeviceNameDevice),
    };

    private static readonly VariableInfo[] LibrarySettings = { new("LIBRARY_PATH", TranslationKeys.VariableLibraryPath) };

    private static readonly VariableInfo[] Settings = { new("SETTINGS_KEY", TranslationKeys.VariableSettingsKey) };

    private static readonly VariableInfo[] AuthFailure =
    {
        new("USERNAME_ATTEMPTED", TranslationKeys.VariableUsernameAttempted),
        new("USER_ID", TranslationKeys.VariableUserIdAuthFailure),
    };

    private static readonly VariableInfo[] TaskStart =
    {
        new("TASK_NAME", TranslationKeys.VariableTaskName),
        new("TASK_KEY", TranslationKeys.VariableTaskKey),
        new("TASK_CATEGORY", TranslationKeys.VariableTaskCategory),
    };

    private static readonly VariableInfo[] TaskEnd =
    {
        new("TASK_STATUS", TranslationKeys.VariableTaskStatus),
        new("TASK_ERROR", TranslationKeys.VariableTaskError),
        new("TASK_DURATION_SECONDS", TranslationKeys.VariableTaskDurationSeconds),
    };

    private static readonly VariableInfo[] PluginVars =
    {
        new("PLUGIN_ID", TranslationKeys.VariablePluginId),
        new("PLUGIN_NAME", TranslationKeys.VariablePluginName),
        new("PLUGIN_VERSION", TranslationKeys.VariablePluginVersion),
    };

    private static readonly VariableInfo[] ItemAll = Item.Concat(ForEachItem).ToArray();

    private static readonly VariableInfo[] UserAll = User.Concat(ForEachUser).ToArray();

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
        [ConditionField.DaysSinceAdded] = "DAYS_SINCE_ADDED",
        [ConditionField.HasSubtitles] = "HAS_SUBTITLES",
        [ConditionField.PlayedByAnyone] = "PLAYED_BY_ANYONE",
        [ConditionField.DaysSincePlayed] = "DAYS_SINCE_PLAYED",
        [ConditionField.DaysSinceActive] = "DAYS_SINCE_ACTIVE",
        [ConditionField.IsDisabled] = "IS_DISABLED",
    };

    private static readonly Dictionary<ScriptTrigger, TriggerInfo> Triggers = new[]
    {
        PlaybackTrigger(ScriptTrigger.PlaybackStart, TranslationKeys.TriggerPlaybackStart, TranslationKeys.TriggerPlaybackStartDescription),
        PlaybackTrigger(ScriptTrigger.PlaybackStop, TranslationKeys.TriggerPlaybackStop, TranslationKeys.TriggerPlaybackStopDescription, Stop),
        PlaybackTrigger(ScriptTrigger.PlaybackPause, TranslationKeys.TriggerPlaybackPause, TranslationKeys.TriggerPlaybackPauseDescription),
        PlaybackTrigger(ScriptTrigger.PlaybackResume, TranslationKeys.TriggerPlaybackResume, TranslationKeys.TriggerPlaybackResumeDescription),
        PlaybackTrigger(ScriptTrigger.PlaybackPausedFor, TranslationKeys.TriggerPlaybackPausedFor, TranslationKeys.TriggerPlaybackPausedForDescription, Paused),
        PlaybackTrigger(ScriptTrigger.PlaybackPlayingFor, TranslationKeys.TriggerPlaybackPlayingFor, TranslationKeys.TriggerPlaybackPlayingForDescription, Playing),
        PlaybackTrigger(ScriptTrigger.PlaybackProgressReached, TranslationKeys.TriggerPlaybackProgressReached, TranslationKeys.TriggerPlaybackProgressReachedDescription),
        PlaybackTrigger(ScriptTrigger.SwitchedToTranscode, TranslationKeys.TriggerSwitchedToTranscode, TranslationKeys.TriggerSwitchedToTranscodeDescription),
        PlaybackTrigger(ScriptTrigger.StreamCountReached, TranslationKeys.TriggerStreamCountReached, TranslationKeys.TriggerStreamCountReachedDescription),
        PlaybackTrigger(ScriptTrigger.TranscodeCountReached, TranslationKeys.TriggerTranscodeCountReached, TranslationKeys.TriggerTranscodeCountReachedDescription),

        Build(ScriptTrigger.ItemAdded, TranslationKeys.TriggerItemAdded, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerItemAddedDescription, true, false, Item),
        Build(ScriptTrigger.ItemUpdated, TranslationKeys.TriggerItemUpdated, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerItemUpdatedDescription, true, false, Item, Update),
        Build(ScriptTrigger.ItemRemoved, TranslationKeys.TriggerItemRemoved, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerItemRemovedDescription, true, false, Item),
        Build(ScriptTrigger.CollectionCreated, TranslationKeys.TriggerCollectionCreated, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerCollectionCreatedDescription, true, false, Item, Collection),
        Build(ScriptTrigger.ItemAddedToCollection, TranslationKeys.TriggerItemAddedToCollection, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerItemAddedToCollectionDescription, true, false, Item, Collection),
        Build(ScriptTrigger.ItemRemovedFromCollection, TranslationKeys.TriggerItemRemovedFromCollection, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerItemRemovedFromCollectionDescription, true, false, Item, Collection),
        Build(ScriptTrigger.SubtitleDownloadFailed, TranslationKeys.TriggerSubtitleDownloadFailed, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerSubtitleDownloadFailedDescription, true, false, Item, Failure),
        Build(ScriptTrigger.LyricDownloadFailed, TranslationKeys.TriggerLyricDownloadFailed, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerLyricDownloadFailedDescription, true, false, Item, Failure),
        Build(ScriptTrigger.MetadataRefreshStarted, TranslationKeys.TriggerMetadataRefreshStarted, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerMetadataRefreshStartedDescription, true, false, Item),
        Build(ScriptTrigger.MetadataRefreshCompleted, TranslationKeys.TriggerMetadataRefreshCompleted, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerMetadataRefreshCompletedDescription, true, false, Item),
        Build(ScriptTrigger.LibrarySettingsChanged, TranslationKeys.TriggerLibrarySettingsChanged, TranslationKeys.TasksLibraryCategory, TranslationKeys.TriggerLibrarySettingsChangedDescription, false, false, LibrarySettings),

        Build(ScriptTrigger.RecordingScheduled, TranslationKeys.TriggerRecordingScheduled, TranslationKeys.LiveTV, TranslationKeys.TriggerRecordingScheduledDescription, true, false, Recording, Item),
        Build(ScriptTrigger.RecordingCancelled, TranslationKeys.TriggerRecordingCancelled, TranslationKeys.LiveTV, TranslationKeys.TriggerRecordingCancelledDescription, true, false, Recording, Item),
        Build(ScriptTrigger.SeriesRecordingScheduled, TranslationKeys.TriggerSeriesRecordingScheduled, TranslationKeys.LiveTV, TranslationKeys.TriggerSeriesRecordingScheduledDescription, true, false, Recording, Item),
        Build(ScriptTrigger.SeriesRecordingCancelled, TranslationKeys.TriggerSeriesRecordingCancelled, TranslationKeys.LiveTV, TranslationKeys.TriggerSeriesRecordingCancelledDescription, true, false, Recording, Item),

        Build(ScriptTrigger.MarkedPlayed, TranslationKeys.TriggerMarkedPlayed, TranslationKeys.HeaderActivity, TranslationKeys.TriggerMarkedPlayedDescription, true, false, User, Item, UserData),
        Build(ScriptTrigger.MarkedUnplayed, TranslationKeys.TriggerMarkedUnplayed, TranslationKeys.HeaderActivity, TranslationKeys.TriggerMarkedUnplayedDescription, true, false, User, Item, UserData),
        Build(ScriptTrigger.FavoriteChanged, TranslationKeys.TriggerFavoriteChanged, TranslationKeys.HeaderActivity, TranslationKeys.TriggerFavoriteChangedDescription, true, false, User, Item, UserData),

        Build(ScriptTrigger.SessionStarted, TranslationKeys.TriggerSessionStarted, TranslationKeys.TriggerGroupSessions, TranslationKeys.TriggerSessionStartedDescription, false, true, User, Session),
        Build(ScriptTrigger.SessionEnded, TranslationKeys.TriggerSessionEnded, TranslationKeys.TriggerGroupSessions, TranslationKeys.TriggerSessionEndedDescription, false, true, User, Session),
        Build(ScriptTrigger.AuthenticationSuccess, TranslationKeys.TriggerAuthenticationSuccess, TranslationKeys.TriggerGroupSessions, TranslationKeys.TriggerAuthenticationSuccessDescription, false, true, User, Session),
        Build(ScriptTrigger.NewDeviceSeen, TranslationKeys.TriggerNewDeviceSeen, TranslationKeys.TriggerGroupSessions, TranslationKeys.TriggerNewDeviceSeenDescription, false, true, User, Session),
        Build(ScriptTrigger.ClientConnected, TranslationKeys.TriggerClientConnected, TranslationKeys.TriggerGroupSessions, TranslationKeys.TriggerClientConnectedDescription, false, true, User, Session),
        Build(ScriptTrigger.DeviceSettingsChanged, TranslationKeys.TriggerDeviceSettingsChanged, TranslationKeys.TriggerGroupSessions, TranslationKeys.TriggerDeviceSettingsChangedDescription, false, false, Device),
        Build(ScriptTrigger.AuthenticationFailure, TranslationKeys.TriggerAuthenticationFailure, TranslationKeys.TriggerGroupSessions, TranslationKeys.TriggerAuthenticationFailureDescription, false, false, AuthFailure, Session.Where(v => v.Name != "SESSION_ID")),

        Build(ScriptTrigger.UserCreated, TranslationKeys.TriggerUserCreated, TranslationKeys.TriggerGroupAccounts, TranslationKeys.TriggerUserCreatedDescription, false, false, User),
        Build(ScriptTrigger.UserDeleted, TranslationKeys.TriggerUserDeleted, TranslationKeys.TriggerGroupAccounts, TranslationKeys.TriggerUserDeletedDescription, false, false, User),
        Build(ScriptTrigger.UserUpdated, TranslationKeys.TriggerUserUpdated, TranslationKeys.TriggerGroupAccounts, TranslationKeys.TriggerUserUpdatedDescription, false, false, User),
        Build(ScriptTrigger.UserLockedOut, TranslationKeys.NotificationOptionUserLockedOut, TranslationKeys.TriggerGroupAccounts, TranslationKeys.TriggerUserLockedOutDescription, false, false, User),
        Build(ScriptTrigger.UserPasswordChanged, TranslationKeys.TriggerUserPasswordChanged, TranslationKeys.TriggerGroupAccounts, TranslationKeys.TriggerUserPasswordChangedDescription, false, false, User),

        Build(ScriptTrigger.ServerStarted, TranslationKeys.TriggerServerStarted, TranslationKeys.LabelSystem, TranslationKeys.TriggerServerStartedDescription, false, false),
        Build(ScriptTrigger.PendingRestart, TranslationKeys.TriggerPendingRestart, TranslationKeys.LabelSystem, TranslationKeys.TriggerPendingRestartDescription, false, false),
        Build(ScriptTrigger.TaskStarted, TranslationKeys.TriggerTaskStarted, TranslationKeys.LabelSystem, TranslationKeys.TriggerTaskStartedDescription, false, false, TaskStart),
        Build(ScriptTrigger.TaskCompleted, TranslationKeys.TriggerTaskCompleted, TranslationKeys.LabelSystem, TranslationKeys.TriggerTaskCompletedDescription, false, false, TaskStart, TaskEnd),
        Build(ScriptTrigger.ActivityLogged, TranslationKeys.TriggerActivityLogged, TranslationKeys.LabelSystem, TranslationKeys.TriggerActivityLoggedDescription, false, false, Activity),
        Build(ScriptTrigger.ServerSettingsChanged, TranslationKeys.TriggerServerSettingsChanged, TranslationKeys.LabelSystem, TranslationKeys.TriggerServerSettingsChangedDescription, false, false, Settings),
        Build(ScriptTrigger.NetworkChanged, TranslationKeys.TriggerNetworkChanged, TranslationKeys.LabelSystem, TranslationKeys.TriggerNetworkChangedDescription, false, false),
        Build(ScriptTrigger.PluginInstalling, TranslationKeys.TriggerPluginInstalling, TranslationKeys.LabelSystem, TranslationKeys.TriggerPluginInstallingDescription, false, false, PluginVars),
        Build(ScriptTrigger.PluginInstallationCancelled, TranslationKeys.TriggerPluginInstallationCancelled, TranslationKeys.LabelSystem, TranslationKeys.TriggerPluginInstallationCancelledDescription, false, false, PluginVars),
        Build(ScriptTrigger.PluginInstalled, TranslationKeys.NotificationOptionPluginInstalled, TranslationKeys.LabelSystem, TranslationKeys.TriggerPluginInstalledDescription, false, false, PluginVars),
        Build(ScriptTrigger.PluginUpdated, TranslationKeys.TriggerPluginUpdated, TranslationKeys.LabelSystem, TranslationKeys.TriggerPluginUpdatedDescription, false, false, PluginVars),
        Build(ScriptTrigger.PluginUninstalled, TranslationKeys.NotificationOptionPluginUninstalled, TranslationKeys.LabelSystem, TranslationKeys.TriggerPluginUninstalledDescription, false, false, PluginVars),

        Build(ScriptTrigger.Schedule, TranslationKeys.TriggerSchedule, TranslationKeys.Schedule, TranslationKeys.TriggerScheduleDescription, false, false),
        Build(ScriptTrigger.Webhook, TranslationKeys.TriggerWebhook, TranslationKeys.TriggerGroupOnDemand, TranslationKeys.TriggerWebhookDescription, false, false, WebhookVars),
        Build(ScriptTrigger.Manual, TranslationKeys.TriggerManual, TranslationKeys.TriggerGroupOnDemand, TranslationKeys.TriggerManualDescription, false, false) with { Selectable = false },
    }.ToDictionary(t => t.Trigger);

    /// <summary>Gets the variables a run started by another script's failure also receives.</summary>
    public static IReadOnlyList<VariableInfo> FailureVariables { get; } = new VariableInfo[]
    {
        new("FAILED_SCRIPT_NAME", TranslationKeys.VariableFailedScriptName),
        new("FAILED_STEP", TranslationKeys.VariableFailedStep),
        new("FAILED_MESSAGE", TranslationKeys.VariableFailedMessage),
        new("FAILED_COUNT", TranslationKeys.VariableFailedCount),
    };

    /// <summary>Gets every trigger, in editor order.</summary>
    public static IReadOnlyList<TriggerInfo> All { get; } = Triggers.Values.ToList();

    /// <summary>
    /// Gets whether a name is one the plugin fills itself, so a step may not save its output under it.
    /// </summary>
    /// <param name="name">The variable name without the prefix.</param>
    /// <returns>True when the name is taken.</returns>
    public static bool IsReserved(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(SecretPrefix, StringComparison.Ordinal)
            || name.StartsWith(WebhookPrefix, StringComparison.Ordinal)
            || ReservedNames().Contains(name);
    }

    private static HashSet<string> ReservedNames()
        => _reserved ??= All
            .SelectMany(t => t.Variables)
            .Concat(ItemAll)
            .Concat(UserAll)
            .Concat(FailureVariables)
            .Select(v => v.Name)
            .ToHashSet(StringComparer.Ordinal);

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

    /// <summary>
    /// Gets whether a run supplies the value a condition field reads, counting what running for each item or
    /// user adds to the event.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="field">The field.</param>
    /// <param name="forEach">What the run is for, beyond the event itself.</param>
    /// <returns>True when the field applies.</returns>
    public static bool Supplies(ScriptTrigger trigger, ConditionField field, ForEachMode forEach)
    {
        if (Supplies(trigger, field))
        {
            return true;
        }

        var name = VariableFor(field);
        return ForEachVariables(forEach).Any(v => v.Name == name);
    }

    /// <summary>
    /// Gets whether a script set to run for each item or user does so on a trigger: only when the event has no
    /// item, or no user, of its own.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="forEach">The script's setting.</param>
    /// <returns>True when the event is expanded.</returns>
    public static bool Expands(ScriptTrigger trigger, ForEachMode forEach)
        => forEach switch
        {
            ForEachMode.Items => !Get(trigger).HasItem,
            ForEachMode.Users => !Get(trigger).HasUser,
            _ => false
        };

    /// <summary>
    /// Gets the variables running for each item or user adds to an event.
    /// </summary>
    /// <param name="forEach">The mode.</param>
    /// <returns>The variables, empty for <see cref="ForEachMode.None"/>.</returns>
    public static IReadOnlyList<VariableInfo> ForEachVariables(ForEachMode forEach)
        => forEach switch
        {
            ForEachMode.Items => ItemAll,
            ForEachMode.Users => UserAll,
            _ => Array.Empty<VariableInfo>()
        };

    private static TriggerInfo PlaybackTrigger(ScriptTrigger trigger, string label, string description, params VariableInfo[] extra)
        => Build(trigger, label, TranslationKeys.TitlePlayback, description, true, true, User, Session, Item, PlaybackVars, Streams, extra);

    private static TriggerInfo Build(ScriptTrigger trigger, string label, string category, string description, bool hasItem, bool hasSession, params IEnumerable<VariableInfo>[] groups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var variables = Common.Concat(groups.SelectMany(g => g)).Where(v => seen.Add(v.Name)).ToList();
        return new TriggerInfo(trigger, label, category, description, hasItem, hasSession, variables);
    }
}
