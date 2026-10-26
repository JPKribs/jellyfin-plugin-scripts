using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// The single source for what each trigger supplies: the variables it fills, whether it carries an item or a
/// session, and therefore which condition fields apply to it. The editor, the validator, the condition
/// evaluator, and the test runs all read from here.
/// </summary>
public static class TriggerCatalog
{
    /// <summary>The prefix every variable carries in a script's environment.</summary>
    public const string EnvironmentPrefix = "JF_";

    private static readonly VariableInfo[] Common =
    {
        new("EVENT", "The trigger that ran the script, such as PlaybackStart."),
        new("TIMESTAMP", "When the event happened, in UTC (ISO 8601)."),
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
        new("PROVIDER_IMDB", "The IMDb ID."),
        new("PROVIDER_TMDB", "The TMDb ID."),
        new("PROVIDER_TVDB", "The TVDb ID."),
    };

    private static readonly VariableInfo[] PlaybackVars =
    {
        new("PLAY_SESSION_ID", "The playback session's ID."),
        new("PLAY_METHOD", "DirectPlay, DirectStream, or Transcode."),
        new("POSITION_SECONDS", "The playback position in seconds."),
        new("PROGRESS_PERCENT", "The playback position as a percentage of the runtime."),
        new("IS_PAUSED", "true when playback is paused, otherwise false."),
        new("VIDEO_CODEC", "The video codec sent to the client, when transcoding."),
        new("AUDIO_CODEC", "The audio codec sent to the client, when transcoding."),
        new("TRANSCODE_REASONS", "Why the server is transcoding, comma separated."),
    };

    private static readonly VariableInfo[] Stop =
    {
        new("PLAYED_TO_COMPLETION", "true when playback reached the end, otherwise false."),
    };

    private static readonly VariableInfo[] AuthFailure =
    {
        new("USERNAME_ATTEMPTED", "The username that was typed."),
        new("USER_ID", "The ID of the matching user, when one exists."),
    };

    private static readonly VariableInfo[] TaskVars =
    {
        new("TASK_NAME", "The scheduled task's name."),
        new("TASK_KEY", "The scheduled task's key."),
        new("TASK_STATUS", "Completed, Failed, Cancelled, or Aborted."),
        new("TASK_ERROR", "The error message, when the task failed."),
        new("TASK_DURATION_SECONDS", "How long the task ran, in seconds."),
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
    };

    private static readonly Dictionary<ScriptTrigger, TriggerInfo> Triggers = new[]
    {
        PlaybackTrigger(ScriptTrigger.PlaybackStart, "Playback started"),
        PlaybackTrigger(ScriptTrigger.PlaybackStop, "Playback stopped", Stop),
        PlaybackTrigger(ScriptTrigger.PlaybackPause, "Playback paused"),
        PlaybackTrigger(ScriptTrigger.PlaybackResume, "Playback resumed"),
        Build(ScriptTrigger.ItemAdded, "Item added", "Library", true, false, Item),
        Build(ScriptTrigger.ItemRemoved, "Item removed", "Library", true, false, Item),
        Build(ScriptTrigger.AuthenticationSuccess, "Signed in", "Security", false, false, User, Session),
        Build(ScriptTrigger.AuthenticationFailure, "Sign in failed", "Security", false, false, AuthFailure, Session.Where(v => v.Name != "SESSION_ID")),
        Build(ScriptTrigger.TaskCompleted, "Scheduled task finished", "System", false, false, TaskVars),
    }.ToDictionary(t => t.Trigger);

    /// <summary>Gets every trigger, in editor order.</summary>
    public static IReadOnlyList<TriggerInfo> All { get; } = Enum.GetValues<ScriptTrigger>().Select(Get).ToList();

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

    private static TriggerInfo PlaybackTrigger(ScriptTrigger trigger, string label, params VariableInfo[] extra)
        => Build(trigger, label, "Playback", true, true, User, Session, Item, PlaybackVars, extra);

    private static TriggerInfo Build(ScriptTrigger trigger, string label, string category, bool hasItem, bool hasSession, params IEnumerable<VariableInfo>[] groups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var variables = Common.Concat(groups.SelectMany(g => g)).Where(v => seen.Add(v.Name)).ToList();
        return new TriggerInfo(trigger, label, category, hasItem, hasSession, variables);
    }
}
