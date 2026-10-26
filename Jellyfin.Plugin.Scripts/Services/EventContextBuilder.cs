using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Turns each server event into an <see cref="EventContext"/>, filling the variables
/// <see cref="TriggerCatalog"/> documents for its trigger.
/// </summary>
public class EventContextBuilder
{
    private const double TicksPerSecond = TimeSpan.TicksPerSecond;

    private readonly ILibraryManager _libraryManager;
    private readonly INetworkManager _networkManager;
    private readonly IServerApplicationHost _host;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<EventContextBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventContextBuilder"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="networkManager">The network manager.</param>
    /// <param name="host">The server host.</param>
    /// <param name="sessionManager">The session manager, for counting streams.</param>
    /// <param name="logger">The logger.</param>
    public EventContextBuilder(
        ILibraryManager libraryManager,
        INetworkManager networkManager,
        IServerApplicationHost host,
        ISessionManager sessionManager,
        ILogger<EventContextBuilder> logger)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _networkManager = networkManager;
        _host = host;
        _logger = logger;
    }

    /// <summary>
    /// Builds the context for a playback event.
    /// </summary>
    /// <param name="trigger">The playback trigger.</param>
    /// <param name="e">The event arguments.</param>
    /// <param name="playMethod">The play method seen earlier in the session, used when the event no longer has one.</param>
    /// <returns>The context.</returns>
    public EventContext FromPlayback(ScriptTrigger trigger, PlaybackProgressEventArgs e, string? playMethod = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        var context = Create(trigger);

        var users = e.Users ?? new List<User>();
        var first = users.FirstOrDefault();
        context.Set("USER_ID", first?.Id.ToString("N", CultureInfo.InvariantCulture));
        context.Set("USER_NAME", first?.Username);
        context.Set("IS_ADMIN", first is null ? null : Bool(first.HasPermission(PermissionKind.IsAdministrator)));
        foreach (var user in users)
        {
            context.UserIds.Add(user.Id);
            context.AddCandidate(ConditionField.User, user.Id.ToString("N", CultureInfo.InvariantCulture));
            context.AddCandidate(ConditionField.User, user.Username);
        }

        var session = e.Session;
        context.SessionId = session?.Id;
        context.Set("SESSION_ID", session?.Id);
        context.Set("CLIENT", e.ClientName ?? session?.Client);
        context.Set("CLIENT_VERSION", session?.ApplicationVersion);
        context.Set("DEVICE_ID", e.DeviceId ?? session?.DeviceId);
        context.Set("DEVICE_NAME", e.DeviceName ?? session?.DeviceName);
        SetRemoteAddress(context, session?.RemoteEndPoint);

        AddItem(context, e.Item);

        context.Set("PLAY_SESSION_ID", e.PlaySessionId);
        context.Set("PLAY_METHOD", session?.PlayState?.PlayMethod?.ToString() ?? playMethod);
        context.Set("IS_PAUSED", Bool(e.IsPaused));
        if (e.PlaybackPositionTicks is { } position)
        {
            context.Set("POSITION_SECONDS", Seconds(position));
            if (e.Item?.RunTimeTicks is { } runtime && runtime > 0)
            {
                var percent = Math.Clamp(position * 100.0 / runtime, 0, 100);
                context.Set("PROGRESS_PERCENT", Math.Round(percent).ToString(CultureInfo.InvariantCulture));
            }
        }

        var transcoding = session?.TranscodingInfo;
        if (transcoding is not null)
        {
            context.Set("VIDEO_CODEC", transcoding.VideoCodec);
            context.Set("AUDIO_CODEC", transcoding.AudioCodec);
            if (transcoding.TranscodeReasons != 0)
            {
                context.Set("TRANSCODE_REASONS", transcoding.TranscodeReasons.ToString());
            }
        }

        SetStreamCounts(context);

        if (e is PlaybackStopEventArgs stop)
        {
            context.Set("PLAYED_TO_COMPLETION", Bool(stop.PlayedToCompletion));
        }

        context.Summary = Join(context.Get("USER_NAME"), DescribeItem(context), context.Get("DEVICE_NAME"));
        return context;
    }

    /// <summary>
    /// Builds the context for a library event.
    /// </summary>
    /// <param name="trigger">The library trigger.</param>
    /// <param name="item">The item added or removed.</param>
    /// <returns>The context.</returns>
    public EventContext FromItem(ScriptTrigger trigger, BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var context = Create(trigger);
        AddItem(context, item);
        context.Summary = Join(DescribeItem(context), context.Get("LIBRARY_NAME"));
        return context;
    }

    /// <summary>
    /// Builds the context for a successful sign in.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    /// <returns>The context.</returns>
    public EventContext FromAuthenticationSuccess(AuthenticationResultEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var context = Create(ScriptTrigger.AuthenticationSuccess);

        var userId = e.User?.Id.ToString("N", CultureInfo.InvariantCulture);
        if (e.User is not null)
        {
            context.UserIds.Add(e.User.Id);
        }

        context.Set("USER_ID", userId);
        context.Set("USER_NAME", e.User?.Name);
        context.Set("IS_ADMIN", e.User?.Policy is { } policy ? Bool(policy.IsAdministrator) : null);
        context.AddCandidate(ConditionField.User, userId);
        context.AddCandidate(ConditionField.User, e.User?.Name);

        var session = e.SessionInfo;
        context.SessionId = session?.Id;
        context.Set("SESSION_ID", session?.Id);
        context.Set("CLIENT", session?.Client);
        context.Set("CLIENT_VERSION", session?.ApplicationVersion);
        context.Set("DEVICE_ID", session?.DeviceId);
        context.Set("DEVICE_NAME", session?.DeviceName);
        SetRemoteAddress(context, session?.RemoteEndPoint);

        context.Summary = Join(context.Get("USER_NAME"), context.Get("DEVICE_NAME"), context.Get("REMOTE_ADDRESS"));
        return context;
    }

    /// <summary>
    /// Builds the context for a failed sign in.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    /// <returns>The context.</returns>
    public EventContext FromAuthenticationFailure(AuthenticationRequestEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var context = Create(ScriptTrigger.AuthenticationFailure);

        var userId = e.UserId?.ToString("N", CultureInfo.InvariantCulture);
        if (e.UserId is { } attempted)
        {
            context.UserIds.Add(attempted);
        }

        context.Set("USERNAME_ATTEMPTED", e.Username);
        context.Set("USER_ID", userId);
        context.AddCandidate(ConditionField.User, userId);
        context.AddCandidate(ConditionField.User, e.Username);

        context.Set("CLIENT", e.App);
        context.Set("CLIENT_VERSION", e.AppVersion);
        context.Set("DEVICE_ID", e.DeviceId);
        context.Set("DEVICE_NAME", e.DeviceName);
        SetRemoteAddress(context, e.RemoteEndPoint);

        context.Summary = Join(context.Get("USERNAME_ATTEMPTED"), context.Get("DEVICE_NAME"), context.Get("REMOTE_ADDRESS"));
        return context;
    }

    /// <summary>
    /// Builds the context for a finished scheduled task.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    /// <returns>The context.</returns>
    public EventContext FromTask(TaskCompletionEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var context = Create(ScriptTrigger.TaskCompleted);
        var result = e.Result;

        context.Set("TASK_NAME", result?.Name ?? e.Task?.Name);
        context.Set("TASK_KEY", result?.Key ?? e.Task?.ScheduledTask?.Key);
        context.Set("TASK_CATEGORY", e.Task?.Category);
        if (result is not null)
        {
            context.Set("TASK_STATUS", result.Status.ToString());
            context.Set("TASK_ERROR", result.ErrorMessage);
            if (result.EndTimeUtc >= result.StartTimeUtc)
            {
                var seconds = (long)(result.EndTimeUtc - result.StartTimeUtc).TotalSeconds;
                context.Set("TASK_DURATION_SECONDS", seconds.ToString(CultureInfo.InvariantCulture));
            }
        }

        context.Summary = Join(context.Get("TASK_NAME"), context.Get("TASK_STATUS"));
        return context;
    }

    /// <summary>
    /// Builds the context for an item whose metadata or images changed.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="reason">Why it changed.</param>
    /// <returns>The context.</returns>
    public EventContext FromItemUpdate(BaseItem item, ItemUpdateType reason)
    {
        var context = FromItem(ScriptTrigger.ItemUpdated, item);

        // The reason is a set of flags, and "None" rides along with the real ones, so each real flag becomes
        // its own value a condition can match.
        var reasons = Enum.GetValues<ItemUpdateType>().Where(r => r != ItemUpdateType.None && reason.HasFlag(r)).Select(r => r.ToString()).ToList();
        context.Set("UPDATE_REASON", string.Join(", ", reasons));
        foreach (var name in reasons)
        {
            context.AddCandidate(ConditionField.UpdateReason, name);
        }

        return context;
    }

    /// <summary>
    /// Builds the context for a collection event. The item is the one added or removed, or the collection itself
    /// when it was just created.
    /// </summary>
    /// <param name="trigger">The collection trigger.</param>
    /// <param name="collection">The collection.</param>
    /// <param name="item">The item added or removed, if any.</param>
    /// <returns>The context.</returns>
    public EventContext FromCollection(ScriptTrigger trigger, BaseItem collection, BaseItem? item)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var context = Create(trigger);
        AddItem(context, item ?? collection);
        context.Set("COLLECTION_ID", collection.Id.ToString("N", CultureInfo.InvariantCulture));
        context.Set("COLLECTION_NAME", collection.Name);
        context.Summary = Join(collection.Name, item is null ? string.Empty : DescribeItem(context));
        return context;
    }

    /// <summary>
    /// Builds the context for a failed subtitle download.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    /// <returns>The context.</returns>
    public EventContext FromSubtitleFailure(SubtitleDownloadFailureEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var context = Create(ScriptTrigger.SubtitleDownloadFailed);
        AddItem(context, e.Item);
        context.Set("PROVIDER", e.Provider);
        context.Set("ERROR_MESSAGE", e.Exception?.Message);
        context.Summary = Join(DescribeItem(context), e.Provider ?? string.Empty);
        return context;
    }

    /// <summary>
    /// Builds the context for a change a user made to an item: played, unplayed, or favorite.
    /// </summary>
    /// <param name="trigger">The activity trigger.</param>
    /// <param name="e">The event arguments.</param>
    /// <param name="user">The user, when they still exist.</param>
    /// <returns>The context.</returns>
    public EventContext FromUserData(ScriptTrigger trigger, UserDataSaveEventArgs e, User? user)
    {
        ArgumentNullException.ThrowIfNull(e);
        var context = Create(trigger);
        AddUser(context, user, e.UserId);
        AddItem(context, e.Item);
        if (e.UserData is { } data)
        {
            context.Set("IS_PLAYED", Bool(data.Played));
            context.Set("IS_FAVORITE", Bool(data.IsFavorite));
            context.Set("PLAY_COUNT", data.PlayCount.ToString(CultureInfo.InvariantCulture));
        }

        context.Summary = Join(context.Get("USER_NAME"), DescribeItem(context));
        return context;
    }

    /// <summary>
    /// Builds the context for a session starting or ending.
    /// </summary>
    /// <param name="trigger">The session trigger.</param>
    /// <param name="session">The session.</param>
    /// <param name="user">The session's user, when it has one.</param>
    /// <returns>The context.</returns>
    public EventContext FromSession(ScriptTrigger trigger, SessionInfo session, User? user)
    {
        ArgumentNullException.ThrowIfNull(session);
        var context = Create(trigger);
        AddUser(context, user, session.UserId);
        if (user is null && !string.IsNullOrEmpty(session.UserName))
        {
            context.Set("USER_NAME", session.UserName);
        }

        context.SessionId = session.Id;
        context.Set("SESSION_ID", session.Id);
        context.Set("CLIENT", session.Client);
        context.Set("CLIENT_VERSION", session.ApplicationVersion);
        context.Set("DEVICE_ID", session.DeviceId);
        context.Set("DEVICE_NAME", session.DeviceName);
        SetRemoteAddress(context, session.RemoteEndPoint);
        context.Summary = Join(context.Get("USER_NAME"), session.Client ?? string.Empty, session.DeviceName ?? string.Empty);
        return context;
    }

    /// <summary>
    /// Builds the context for a user account event.
    /// </summary>
    /// <param name="trigger">The account trigger.</param>
    /// <param name="user">The user.</param>
    /// <returns>The context.</returns>
    public EventContext FromUser(ScriptTrigger trigger, User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var context = Create(trigger);
        AddUser(context, user, user.Id);
        context.Summary = user.Username;
        return context;
    }

    /// <summary>
    /// Builds the context for a scheduled task starting.
    /// </summary>
    /// <param name="worker">The task.</param>
    /// <returns>The context.</returns>
    public EventContext FromTaskStarted(IScheduledTaskWorker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        var context = Create(ScriptTrigger.TaskStarted);
        context.Set("TASK_NAME", worker.Name);
        context.Set("TASK_KEY", worker.ScheduledTask?.Key);
        context.Set("TASK_CATEGORY", worker.Category);
        context.QueuedByScript = TaskChains.WasQueued(context.Get("TASK_KEY"));
        context.Summary = worker.Name ?? string.Empty;
        return context;
    }

    /// <summary>
    /// Builds the context for a plugin being installed, updated, or uninstalled.
    /// </summary>
    /// <param name="trigger">The plugin trigger.</param>
    /// <param name="id">The plugin's ID.</param>
    /// <param name="name">The plugin's name.</param>
    /// <param name="version">The plugin's version.</param>
    /// <returns>The context.</returns>
    public EventContext FromPlugin(ScriptTrigger trigger, Guid id, string? name, Version? version)
    {
        var context = Create(trigger);
        context.Set("PLUGIN_ID", id.ToString("N", CultureInfo.InvariantCulture));
        context.Set("PLUGIN_NAME", name);
        context.Set("PLUGIN_VERSION", version?.ToString());
        context.Summary = Join(name ?? string.Empty, version?.ToString() ?? string.Empty);
        return context;
    }

    /// <summary>
    /// Builds the context for a failed lyrics download.
    /// </summary>
    /// <param name="trigger">The failure trigger.</param>
    /// <param name="item">The item.</param>
    /// <param name="provider">The provider that failed.</param>
    /// <param name="error">Why it failed.</param>
    /// <returns>The context.</returns>
    public EventContext FromProviderFailure(ScriptTrigger trigger, BaseItem? item, string? provider, Exception? error)
    {
        var context = Create(trigger);
        AddItem(context, item);
        context.Set("PROVIDER", provider);
        context.Set("ERROR_MESSAGE", error?.Message);
        context.Summary = Join(DescribeItem(context), provider ?? string.Empty);
        return context;
    }

    /// <summary>
    /// Builds the context for a Live TV recording timer being scheduled or cancelled. The program becomes the
    /// event's item when the server still knows it.
    /// </summary>
    /// <param name="trigger">The recording trigger.</param>
    /// <param name="timerId">The timer's ID.</param>
    /// <param name="programId">The program's ID, if known.</param>
    /// <returns>The context.</returns>
    public EventContext FromRecording(ScriptTrigger trigger, string? timerId, Guid? programId)
    {
        var context = Create(trigger);
        context.Set("TIMER_ID", timerId);
        if (programId is { } id && id != Guid.Empty)
        {
            context.Set("PROGRAM_ID", id.ToString("N", CultureInfo.InvariantCulture));
            AddItem(context, _libraryManager.GetItemById(id));
        }

        context.Summary = Join(TriggerCatalog.Get(trigger).Label, context.Get("ITEM_NAME"));
        return context;
    }

    /// <summary>
    /// Builds the context for an activity log entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The context.</returns>
    public EventContext FromActivity(MediaBrowser.Model.Activity.ActivityLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var context = Create(ScriptTrigger.ActivityLogged);
        context.Set("ACTIVITY_NAME", entry.Name);
        context.Set("ACTIVITY_TYPE", entry.Type);
        context.Set("ACTIVITY_OVERVIEW", entry.Overview ?? entry.ShortOverview);
        context.Set("ACTIVITY_SEVERITY", entry.Severity.ToString());
        if (entry.UserId != Guid.Empty)
        {
            context.UserIds.Add(entry.UserId);
            context.Set("USER_ID", entry.UserId.ToString("N", CultureInfo.InvariantCulture));
            context.AddCandidate(ConditionField.User, context.Get("USER_ID"));
        }

        context.Summary = entry.Name ?? string.Empty;
        return context;
    }

    /// <summary>
    /// Builds the context for an event described by one name and value pair, such as a library's folder or the
    /// key of the settings that changed.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="values">The variables to set.</param>
    /// <returns>The context.</returns>
    public EventContext FromValues(ScriptTrigger trigger, params (string Name, string? Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var context = Create(trigger);
        foreach (var (name, value) in values)
        {
            context.Set(name, value);
        }

        context.Summary = Join(new[] { TriggerCatalog.Get(trigger).Label }.Concat(values.Select(v => v.Value ?? string.Empty)).ToArray());
        return context;
    }

    /// <summary>
    /// Builds the context for an event that carries nothing beyond the common variables: the server starting,
    /// a pending restart, or a schedule coming due.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <returns>The context.</returns>
    public EventContext FromSystem(ScriptTrigger trigger)
    {
        var context = Create(trigger);
        context.Summary = TriggerCatalog.Get(trigger).Label;
        return context;
    }

    /// <summary>
    /// Builds a context with sample values for every variable the trigger supplies, for the test button. It
    /// carries no session or item ID, so steps that act on one report that they skipped.
    /// </summary>
    /// <param name="trigger">The trigger to imitate.</param>
    /// <returns>The context.</returns>
    public EventContext Sample(ScriptTrigger trigger)
    {
        var context = Create(trigger);
        foreach (var variable in TriggerCatalog.Get(trigger).Variables)
        {
            if (!context.Variables.ContainsKey(variable.Name) && SampleValues.TryGetValue(variable.Name, out var value))
            {
                context.Set(variable.Name, value);
            }
        }

        if (TriggerCatalog.Get(trigger).HasItem)
        {
            context.ItemKind = BaseItemKind.Movie;
        }

        if (Guid.TryParse(context.Get("USER_ID"), out var sampleUser))
        {
            context.UserIds.Add(sampleUser);
        }

        context.AddCandidate(ConditionField.User, context.Get("USER_ID"));
        context.AddCandidate(ConditionField.User, context.Get("USER_NAME"));
        context.AddCandidate(ConditionField.User, context.Get("USERNAME_ATTEMPTED"));
        context.AddCandidate(ConditionField.Library, context.Get("LIBRARY_ID"));
        context.AddCandidate(ConditionField.Library, context.Get("LIBRARY_NAME"));
        context.Summary = "Test run with sample values";
        return context;
    }

    private static readonly Dictionary<string, string> SampleValues = new(StringComparer.Ordinal)
    {
        ["USER_ID"] = "00000000000000000000000000000001",
        ["USER_NAME"] = "sample-user",
        ["SESSION_ID"] = "sample-session",
        ["CLIENT"] = "Jellyfin Web",
        ["CLIENT_VERSION"] = "10.11.0",
        ["DEVICE_ID"] = "sample-device",
        ["DEVICE_NAME"] = "Living Room TV",
        ["REMOTE_ADDRESS"] = "192.168.1.50",
        ["IS_LOCAL"] = "true",
        ["ITEM_ID"] = "00000000000000000000000000000002",
        ["ITEM_NAME"] = "Sample Movie",
        ["ITEM_KIND"] = nameof(BaseItemKind.Movie),
        ["ITEM_PATH"] = "/media/movies/Sample Movie (2024)/Sample Movie (2024).mkv",
        ["LIBRARY_ID"] = "00000000000000000000000000000003",
        ["LIBRARY_NAME"] = "Movies",
        ["YEAR"] = "2024",
        ["RUNTIME_SECONDS"] = "7200",
        ["PROVIDER_IMDB"] = "tt0000000",
        ["PROVIDER_TMDB"] = "0",
        ["PLAY_SESSION_ID"] = "sample-play-session",
        ["PLAY_METHOD"] = "DirectPlay",
        ["POSITION_SECONDS"] = "600",
        ["PROGRESS_PERCENT"] = "8",
        ["IS_PAUSED"] = "false",
        ["PLAYED_TO_COMPLETION"] = "false",
        ["USERNAME_ATTEMPTED"] = "sample-user",
        ["IS_ADMIN"] = "false",
        ["GENRES"] = "Action, Science Fiction",
        ["TAGS"] = "sample",
        ["STUDIOS"] = "Sample Studio",
        ["OFFICIAL_RATING"] = "PG-13",
        ["COMMUNITY_RATING"] = "7.5",
        ["VIDEO_WIDTH"] = "1920",
        ["VIDEO_HEIGHT"] = "1080",
        ["DATE_ADDED"] = "2024-01-01T00:00:00Z",
        ["PAUSED_SECONDS"] = "600",
        ["PLAYING_SECONDS"] = "3600",
        ["IS_PLAYED"] = "true",
        ["IS_FAVORITE"] = "true",
        ["PLAY_COUNT"] = "1",
        ["COLLECTION_ID"] = "00000000000000000000000000000004",
        ["COLLECTION_NAME"] = "Sample Collection",
        ["UPDATE_REASON"] = "MetadataDownload",
        ["PROVIDER"] = "Open Subtitles",
        ["ACTIVE_STREAMS"] = "2",
        ["ACTIVE_TRANSCODES"] = "1",
        ["TIMER_ID"] = "sample-timer",
        ["PROGRAM_ID"] = "00000000000000000000000000000006",
        ["ACTIVITY_NAME"] = "sample-user is playing Sample Movie",
        ["ACTIVITY_TYPE"] = "VideoPlayback",
        ["ACTIVITY_OVERVIEW"] = "Sample overview",
        ["ACTIVITY_SEVERITY"] = "Information",
        ["LIBRARY_PATH"] = "/media/movies",
        ["SETTINGS_KEY"] = "encoding",
        ["ERROR_MESSAGE"] = "Sample error",
        ["TASK_CATEGORY"] = "Library",
        ["PLUGIN_ID"] = "00000000000000000000000000000005",
        ["PLUGIN_NAME"] = "Sample Plugin",
        ["PLUGIN_VERSION"] = "1.0.0.0",
        ["TASK_NAME"] = "Scan Media Library",
        ["TASK_KEY"] = "RefreshLibrary",
        ["TASK_STATUS"] = "Completed",
        ["TASK_DURATION_SECONDS"] = "42",
    };

    private EventContext Create(ScriptTrigger trigger)
    {
        var context = new EventContext(trigger);
        context.Set("EVENT", trigger.ToString());
        context.Set("TIMESTAMP", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var local = DateTime.Now;
        context.Set("LOCAL_DATE", local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        context.Set("LOCAL_TIME", local.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        context.Set("HOUR", local.Hour.ToString(CultureInfo.InvariantCulture));
        context.Set("DAY_OF_WEEK", local.DayOfWeek.ToString());
        context.Set("SERVER_NAME", _host.FriendlyName);
        context.Set("SERVER_ID", _host.SystemId);
        context.Set("SERVER_VERSION", _host.ApplicationVersionString);
        return context;
    }

    private void AddItem(EventContext context, BaseItem? item)
    {
        if (item is null)
        {
            return;
        }

        var kind = item.GetBaseItemKind();
        context.ItemKind = kind;
        context.ItemId = item.Id;
        context.Set("ITEM_ID", item.Id.ToString("N", CultureInfo.InvariantCulture));
        context.Set("ITEM_NAME", item.Name);
        context.Set("ITEM_KIND", kind.ToString());
        context.Set("ITEM_PATH", item.Path);
        context.Set("YEAR", item.ProductionYear?.ToString(CultureInfo.InvariantCulture));
        if (item.RunTimeTicks is { } runtime && runtime > 0)
        {
            context.Set("RUNTIME_SECONDS", Seconds(runtime));
        }

        if (item is IHasSeries series)
        {
            context.Set("SERIES_NAME", series.SeriesName);
        }

        switch (item)
        {
            case Episode:
                context.Set("SEASON_NUMBER", item.ParentIndexNumber?.ToString(CultureInfo.InvariantCulture));
                context.Set("EPISODE_NUMBER", item.IndexNumber?.ToString(CultureInfo.InvariantCulture));
                break;
            case Season:
                context.Set("SEASON_NUMBER", item.IndexNumber?.ToString(CultureInfo.InvariantCulture));
                break;
            case Audio audio:
                context.Set("ALBUM", audio.Album);
                context.Set("ARTIST", audio.Artists is { Count: > 0 } artists ? string.Join(", ", artists) : null);
                context.Set("EPISODE_NUMBER", item.IndexNumber?.ToString(CultureInfo.InvariantCulture));
                break;
        }

        context.Set("GENRES", JoinList(item.Genres));
        context.Set("TAGS", JoinList(item.Tags));
        context.Set("STUDIOS", JoinList(item.Studios));
        foreach (var genre in item.Genres ?? Array.Empty<string>())
        {
            context.AddCandidate(ConditionField.Genre, genre);
        }

        foreach (var tag in item.Tags ?? Array.Empty<string>())
        {
            context.AddCandidate(ConditionField.Tag, tag);
        }

        foreach (var studio in item.Studios ?? Array.Empty<string>())
        {
            context.AddCandidate(ConditionField.Studio, studio);
        }

        context.Set("OFFICIAL_RATING", item.OfficialRating);
        context.Set("COMMUNITY_RATING", item.CommunityRating?.ToString("0.#", CultureInfo.InvariantCulture));
        if (item.Width > 0 && item.Height > 0)
        {
            context.Set("VIDEO_WIDTH", item.Width.ToString(CultureInfo.InvariantCulture));
            context.Set("VIDEO_HEIGHT", item.Height.ToString(CultureInfo.InvariantCulture));
        }

        if (item.DateCreated > DateTime.MinValue)
        {
            context.Set("DATE_ADDED", item.DateCreated.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        }

        context.Set("PROVIDER_IMDB", ProviderId(item, "Imdb"));
        context.Set("PROVIDER_TMDB", ProviderId(item, "Tmdb"));
        context.Set("PROVIDER_TVDB", ProviderId(item, "Tvdb"));

        foreach (var library in Libraries(item))
        {
            var id = library.Id.ToString("N", CultureInfo.InvariantCulture);
            if (!context.Variables.ContainsKey("LIBRARY_ID"))
            {
                context.Set("LIBRARY_ID", id);
                context.Set("LIBRARY_NAME", library.Name);
            }

            context.AddCandidate(ConditionField.Library, id);
            context.AddCandidate(ConditionField.Library, library.Name);
        }
    }

    private IEnumerable<Folder> Libraries(BaseItem item)
    {
        try
        {
            return _libraryManager.GetCollectionFolders(item);
        }
        catch (Exception ex)
        {
            // A removed item can no longer be placed in a library. The event still runs without one.
            _logger.LogDebug(ex, "Scripts: could not resolve the libraries of {ItemId}", item.Id);
            return Array.Empty<Folder>();
        }
    }

    // Fills the user variables. A deleted user may be gone already, so the ID alone still filters by user.
    private static void AddUser(EventContext context, User? user, Guid userId)
    {
        var id = user?.Id ?? userId;
        if (id != Guid.Empty)
        {
            context.UserIds.Add(id);
            context.Set("USER_ID", id.ToString("N", CultureInfo.InvariantCulture));
            context.AddCandidate(ConditionField.User, id.ToString("N", CultureInfo.InvariantCulture));
        }

        if (user is not null)
        {
            context.Set("USER_NAME", user.Username);
            context.Set("IS_ADMIN", Bool(user.HasPermission(PermissionKind.IsAdministrator)));
            context.AddCandidate(ConditionField.User, user.Username);
        }
    }

    // Counts what is playing now, so a script can act on the number of streams or transcodes.
    private void SetStreamCounts(EventContext context)
    {
        var playing = _sessionManager.Sessions.Where(s => s.NowPlayingItem is not null).ToList();
        context.Set("ACTIVE_STREAMS", playing.Count.ToString(CultureInfo.InvariantCulture));
        context.Set("ACTIVE_TRANSCODES", playing.Count(s => s.PlayState?.PlayMethod == PlayMethod.Transcode).ToString(CultureInfo.InvariantCulture));
    }

    private static string? JoinList(IReadOnlyCollection<string>? values)
        => values is { Count: > 0 } ? string.Join(", ", values) : null;

    private void SetRemoteAddress(EventContext context, string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return;
        }

        context.Set("REMOTE_ADDRESS", remote);
        if (IPAddress.TryParse(remote, out var address) || (IPEndPoint.TryParse(remote, out var endPoint) && (address = endPoint.Address) is not null))
        {
            context.Set("IS_LOCAL", Bool(_networkManager.IsInLocalNetwork(address)));
        }
    }

    private static string? ProviderId(BaseItem item, string name)
    {
        var ids = item.ProviderIds;
        if (ids is null)
        {
            return null;
        }

        if (ids.TryGetValue(name, out var value))
        {
            return value;
        }

        return ids.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static string DescribeItem(EventContext context)
    {
        var name = context.Get("ITEM_NAME");
        var series = context.Get("SERIES_NAME");
        return series.Length > 0 && series != name ? series + " – " + name : name;
    }

    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static string Seconds(long ticks) => ((long)(ticks / TicksPerSecond)).ToString(CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";
}
