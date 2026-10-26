using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Library;
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
    private readonly ILogger<EventContextBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventContextBuilder"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="networkManager">The network manager.</param>
    /// <param name="host">The server host.</param>
    /// <param name="logger">The logger.</param>
    public EventContextBuilder(
        ILibraryManager libraryManager,
        INetworkManager networkManager,
        IServerApplicationHost host,
        ILogger<EventContextBuilder> logger)
    {
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

        var users = e.Users ?? new List<Jellyfin.Database.Implementations.Entities.User>();
        var first = users.FirstOrDefault();
        context.Set("USER_ID", first?.Id.ToString("N", CultureInfo.InvariantCulture));
        context.Set("USER_NAME", first?.Username);
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
        context.AddCandidate(ConditionField.User, userId);
        context.AddCandidate(ConditionField.User, e.User?.Name);

        var session = e.SessionInfo;
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
