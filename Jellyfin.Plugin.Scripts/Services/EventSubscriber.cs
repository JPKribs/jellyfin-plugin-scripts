using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities.Security;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Lyrics;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Listens to the server's playback, library, collection, user activity, session, task, and subtitle events for
/// as long as the server runs, and hands each one to the dispatcher. A tick every few seconds also releases items
/// waiting for metadata, fires the "for a while" playback triggers, and runs schedules. Sign ins, accounts,
/// plugins, and restarts arrive through <see cref="ServerEventConsumers"/> instead.
/// </summary>
public sealed class EventSubscriber : IHostedService, IDisposable
{
    /// <summary>How often the tick runs.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long a new item may wait for metadata before it is dispatched without it.</summary>
    public static readonly TimeSpan MetadataWaitLimit = TimeSpan.FromSeconds(60);

    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ITaskManager _taskManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ICollectionManager _collectionManager;
    private readonly ISubtitleManager _subtitleManager;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IProviderManager _providerManager;
    private readonly ILyricManager _lyricManager;
    private readonly ILiveTvManager _liveTvManager;
    private readonly IDeviceManager _deviceManager;
    private readonly IActivityManager _activityManager;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly INetworkManager _networkManager;
    private readonly EventContextBuilder _builder;
    private readonly ScriptDispatcher _dispatcher;
    private readonly ILogger<EventSubscriber> _logger;
    private readonly PlaybackTracker _playback = new();
    private readonly FavoriteStates _favorites = new();
    private readonly ConcurrentDictionary<Guid, PendingItem> _pendingItems = new();
    private readonly JsonFileStore<ScheduleState> _scheduleStore;
    private ScheduleState? _scheduleState;
    private Timer? _tickTimer;
    private CancellationTokenRegistration _startedRegistration;
    private volatile bool _stopping;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSubscriber"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="taskManager">The scheduled task manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="collectionManager">The collection manager.</param>
    /// <param name="subtitleManager">The subtitle manager.</param>
    /// <param name="lifetime">The host lifetime.</param>
    /// <param name="providerManager">The metadata provider manager.</param>
    /// <param name="lyricManager">The lyrics manager.</param>
    /// <param name="liveTvManager">The Live TV manager.</param>
    /// <param name="deviceManager">The device manager.</param>
    /// <param name="activityManager">The activity log.</param>
    /// <param name="configurationManager">The server configuration manager.</param>
    /// <param name="networkManager">The network manager.</param>
    /// <param name="paths">The plugin paths.</param>
    /// <param name="builder">The event context builder.</param>
    /// <param name="dispatcher">The script dispatcher.</param>
    /// <param name="logger">The logger.</param>
    public EventSubscriber(
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        ITaskManager taskManager,
        IUserDataManager userDataManager,
        IUserManager userManager,
        ICollectionManager collectionManager,
        ISubtitleManager subtitleManager,
        IHostApplicationLifetime lifetime,
        IProviderManager providerManager,
        ILyricManager lyricManager,
        ILiveTvManager liveTvManager,
        IDeviceManager deviceManager,
        IActivityManager activityManager,
        IServerConfigurationManager configurationManager,
        INetworkManager networkManager,
        ScriptPaths paths,
        EventContextBuilder builder,
        ScriptDispatcher dispatcher,
        ILogger<EventSubscriber> logger)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _taskManager = taskManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _collectionManager = collectionManager;
        _subtitleManager = subtitleManager;
        _lifetime = lifetime;
        _providerManager = providerManager;
        _lyricManager = lyricManager;
        _liveTvManager = liveTvManager;
        _deviceManager = deviceManager;
        _activityManager = activityManager;
        _configurationManager = configurationManager;
        _networkManager = networkManager;
        ArgumentNullException.ThrowIfNull(paths);
        _scheduleStore = new JsonFileStore<ScheduleState>(paths.ScheduleFile, logger);
        _builder = builder;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionStarted += OnSessionStarted;
        _sessionManager.SessionEnded += OnSessionEnded;
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _collectionManager.CollectionCreated += OnCollectionCreated;
        _collectionManager.ItemsAddedToCollection += OnItemsAddedToCollection;
        _collectionManager.ItemsRemovedFromCollection += OnItemsRemovedFromCollection;
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _subtitleManager.SubtitleDownloadFailure += OnSubtitleDownloadFailure;
        _taskManager.TaskExecuting += OnTaskExecuting;
        _taskManager.TaskCompleted += OnTaskCompleted;
        _sessionManager.SessionControllerConnected += OnClientConnected;
        _providerManager.RefreshStarted += OnRefreshStarted;
        _providerManager.RefreshCompleted += OnRefreshCompleted;
        _lyricManager.LyricDownloadFailure += OnLyricDownloadFailure;
        CollectionFolder.LibraryOptionsUpdated += OnLibraryOptionsUpdated;
        _liveTvManager.TimerCreated += OnTimerCreated;
        _liveTvManager.TimerCancelled += OnTimerCancelled;
        _liveTvManager.SeriesTimerCreated += OnSeriesTimerCreated;
        _liveTvManager.SeriesTimerCancelled += OnSeriesTimerCancelled;
        _deviceManager.DeviceOptionsUpdated += OnDeviceOptionsUpdated;
        _activityManager.EntryCreated += OnActivityEntryCreated;
        _configurationManager.ConfigurationUpdated += OnConfigurationUpdated;
        _configurationManager.NamedConfigurationUpdated += OnNamedConfigurationUpdated;
        _networkManager.NetworkChanged += OnNetworkChanged;
        _userManager.OnUserUpdated += OnUserUpdated;
        _startedRegistration = _lifetime.ApplicationStarted.Register(() => Guard("server started", () => DispatchSystem(ScriptTrigger.ServerStarted)));
        _tickTimer = new Timer(_ => Tick(), null, TickInterval, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionManager.SessionEnded -= OnSessionEnded;
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        _collectionManager.CollectionCreated -= OnCollectionCreated;
        _collectionManager.ItemsAddedToCollection -= OnItemsAddedToCollection;
        _collectionManager.ItemsRemovedFromCollection -= OnItemsRemovedFromCollection;
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _subtitleManager.SubtitleDownloadFailure -= OnSubtitleDownloadFailure;
        _taskManager.TaskExecuting -= OnTaskExecuting;
        _taskManager.TaskCompleted -= OnTaskCompleted;
        _sessionManager.SessionControllerConnected -= OnClientConnected;
        _providerManager.RefreshStarted -= OnRefreshStarted;
        _providerManager.RefreshCompleted -= OnRefreshCompleted;
        _lyricManager.LyricDownloadFailure -= OnLyricDownloadFailure;
        CollectionFolder.LibraryOptionsUpdated -= OnLibraryOptionsUpdated;
        _liveTvManager.TimerCreated -= OnTimerCreated;
        _liveTvManager.TimerCancelled -= OnTimerCancelled;
        _liveTvManager.SeriesTimerCreated -= OnSeriesTimerCreated;
        _liveTvManager.SeriesTimerCancelled -= OnSeriesTimerCancelled;
        _deviceManager.DeviceOptionsUpdated -= OnDeviceOptionsUpdated;
        _activityManager.EntryCreated -= OnActivityEntryCreated;
        _configurationManager.ConfigurationUpdated -= OnConfigurationUpdated;
        _configurationManager.NamedConfigurationUpdated -= OnNamedConfigurationUpdated;
        _networkManager.NetworkChanged -= OnNetworkChanged;
        _userManager.OnUserUpdated -= OnUserUpdated;
        await _startedRegistration.DisposeAsync().ConfigureAwait(false);
        _tickTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public void Dispose() => _tickTimer?.Dispose();

    private static string PlayKey(PlaybackProgressEventArgs e)
        => e.PlaySessionId ?? (e.Session?.Id + "|" + e.Item?.Id.ToString("N", CultureInfo.InvariantCulture));

    // ---- Playback ----

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        Guard("playback start", () =>
        {
            var key = PlayKey(e);
            _playback.Started(key, e, DateTime.UtcNow);
            RememberPlayMethod(key, e);
            DispatchPlayback(ScriptTrigger.PlaybackStart, e);
        });
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        Guard("playback progress", () =>
        {
            var key = PlayKey(e);
            var edge = _playback.Progress(key, e.IsPaused, e, DateTime.UtcNow);
            RememberPlayMethod(key, e);
            if (edge is { } paused)
            {
                DispatchPlayback(paused ? ScriptTrigger.PlaybackPause : ScriptTrigger.PlaybackResume, e);
            }
        });
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        Guard("playback stop", () =>
        {
            var playMethod = _playback.Stopped(PlayKey(e));
            DispatchPlayback(ScriptTrigger.PlaybackStop, e, playMethod);
        });
    }

    private void DispatchPlayback(ScriptTrigger trigger, PlaybackProgressEventArgs e, string? playMethod = null)
    {
        if (ScriptDispatcher.HasScriptsFor(trigger))
        {
            _dispatcher.Dispatch(_builder.FromPlayback(trigger, e, playMethod));
        }
    }

    // The server clears a session's play state before it reports the stop, so the method seen while playing
    // is kept with the rest of the playback's state for the stop event.
    private void RememberPlayMethod(string key, PlaybackProgressEventArgs e)
    {
        if (e.Session?.PlayState?.PlayMethod is { } method)
        {
            _playback.RememberPlayMethod(key, method.ToString());
        }
    }

    // ---- Sessions ----

    private void OnSessionStarted(object? sender, SessionEventArgs e) => DispatchSession(ScriptTrigger.SessionStarted, e);

    private void OnSessionEnded(object? sender, SessionEventArgs e) => DispatchSession(ScriptTrigger.SessionEnded, e);

    private void DispatchSession(ScriptTrigger trigger, SessionEventArgs e)
    {
        Guard(trigger.ToString(), () =>
        {
            if (e.SessionInfo is not { } session || !ScriptDispatcher.HasScriptsFor(trigger))
            {
                return;
            }

            var user = session.UserId == Guid.Empty ? null : _userManager.GetUserById(session.UserId);
            _dispatcher.Dispatch(_builder.FromSession(trigger, session, user));
        });
    }

    // ---- Library ----

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        Guard("item added", () =>
        {
            // A virtual item is a placeholder, such as a missing episode from the series metadata, not media
            // that arrived in the library.
            var item = e.Item;
            if (item is null || item.IsVirtualItem || !ScriptDispatcher.WantsItem(ScriptTrigger.ItemAdded, item.GetBaseItemKind()))
            {
                return;
            }

            // The event fires before metadata is fetched, so the item waits until an update brings provider IDs.
            _pendingItems.TryAdd(item.Id, new PendingItem(DateTime.UtcNow, HasMetadata(item)));
        });
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        Guard("item updated", () =>
        {
            var item = e.Item;
            if (item is null)
            {
                return;
            }

            // A new item waiting for metadata is released once an update gives it some, so the tick never has
            // to look every waiting item up again.
            if (_pendingItems.TryGetValue(item.Id, out var pending) && !pending.Ready && HasMetadata(item))
            {
                _pendingItems.TryUpdate(item.Id, pending with { Ready = true }, pending);
            }

            if (item.IsVirtualItem || !ScriptDispatcher.WantsItem(ScriptTrigger.ItemUpdated, item.GetBaseItemKind()))
            {
                return;
            }

            _dispatcher.Dispatch(_builder.FromItemUpdate(item, e.UpdateReason));
        });
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        Guard("item removed", () =>
        {
            var item = e.Item;
            if (item is null)
            {
                return;
            }

            _pendingItems.TryRemove(item.Id, out _);
            if (!item.IsVirtualItem && ScriptDispatcher.WantsItem(ScriptTrigger.ItemRemoved, item.GetBaseItemKind()))
            {
                _dispatcher.Dispatch(_builder.FromItem(ScriptTrigger.ItemRemoved, item));
            }
        });
    }

    private void OnCollectionCreated(object? sender, CollectionCreatedEventArgs e)
    {
        Guard("collection created", () =>
        {
            if (e.Collection is { } collection && ScriptDispatcher.HasScriptsFor(ScriptTrigger.CollectionCreated))
            {
                _dispatcher.Dispatch(_builder.FromCollection(ScriptTrigger.CollectionCreated, collection, null));
            }
        });
    }

    private void OnItemsAddedToCollection(object? sender, CollectionModifiedEventArgs e)
        => DispatchCollectionItems(ScriptTrigger.ItemAddedToCollection, e);

    private void OnItemsRemovedFromCollection(object? sender, CollectionModifiedEventArgs e)
        => DispatchCollectionItems(ScriptTrigger.ItemRemovedFromCollection, e);

    // One event per item, so a script's item kind filter and variables describe exactly one item.
    private void DispatchCollectionItems(ScriptTrigger trigger, CollectionModifiedEventArgs e)
    {
        Guard(trigger.ToString(), () =>
        {
            if (e.Collection is not { } collection || !ScriptDispatcher.HasScriptsFor(trigger))
            {
                return;
            }

            foreach (var item in e.ItemsChanged ?? Array.Empty<BaseItem>())
            {
                if (ScriptDispatcher.WantsItem(trigger, item.GetBaseItemKind()))
                {
                    _dispatcher.Dispatch(_builder.FromCollection(trigger, collection, item));
                }
            }
        });
    }

    private void OnSubtitleDownloadFailure(object? sender, SubtitleDownloadFailureEventArgs e)
    {
        Guard("subtitle download failure", () =>
        {
            if (ScriptDispatcher.HasScriptsFor(ScriptTrigger.SubtitleDownloadFailed))
            {
                _dispatcher.Dispatch(_builder.FromSubtitleFailure(e));
            }
        });
    }

    // ---- User activity ----

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        Guard("user data saved", () =>
        {
            // Every save shows the item's favorite state, so a like or dislike (saved with the same reason as a
            // favorite) can be told apart from a favorite actually changing.
            var favoriteChanged = e.UserData is { } data && e.Item is { } saved && _favorites.Record(e.UserId, saved.Id, data.IsFavorite);

            // Playback saves user data constantly; only the deliberate changes a user makes are triggers.
            ScriptTrigger trigger;
            switch (e.SaveReason)
            {
                case UserDataSaveReason.TogglePlayed:
                    trigger = e.UserData?.Played == true ? ScriptTrigger.MarkedPlayed : ScriptTrigger.MarkedUnplayed;
                    break;
                case UserDataSaveReason.UpdateUserRating when favoriteChanged:
                    trigger = ScriptTrigger.FavoriteChanged;
                    break;
                default:
                    return;
            }

            if (e.Item is null || !ScriptDispatcher.WantsItem(trigger, e.Item.GetBaseItemKind()))
            {
                return;
            }

            _dispatcher.Dispatch(_builder.FromUserData(trigger, e, _userManager.GetUserById(e.UserId)));
        });
    }

    // ---- Tasks ----

    private void OnTaskExecuting(object? sender, GenericEventArgs<IScheduledTaskWorker> e)
    {
        Guard("task started", () =>
        {
            if (e.Argument is { } worker && ScriptDispatcher.HasScriptsFor(ScriptTrigger.TaskStarted))
            {
                _dispatcher.Dispatch(_builder.FromTaskStarted(worker));
            }
        });
    }

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
    {
        Guard("task completed", () =>
        {
            // Forgotten on every finish, scripts or not, so a later run of the same task is not taken for one a
            // script queued.
            var queuedByScript = TaskChains.Finished(e.Result?.Key ?? e.Task?.ScheduledTask?.Key);
            if (ScriptDispatcher.HasScriptsFor(ScriptTrigger.TaskCompleted))
            {
                var context = _builder.FromTask(e);
                context.QueuedByScript = queuedByScript;
                _dispatcher.Dispatch(context);
            }
        });
    }

    private void DispatchSystem(ScriptTrigger trigger)
    {
        if (ScriptDispatcher.HasScriptsFor(trigger))
        {
            _dispatcher.Dispatch(_builder.FromSystem(trigger));
        }
    }

    // ---- More library, Live TV, devices, and system ----

    private void OnClientConnected(object? sender, SessionEventArgs e) => DispatchSession(ScriptTrigger.ClientConnected, e);

    private void OnRefreshStarted(object? sender, GenericEventArgs<BaseItem> e) => DispatchItem(ScriptTrigger.MetadataRefreshStarted, e.Argument);

    private void OnRefreshCompleted(object? sender, GenericEventArgs<BaseItem> e) => DispatchItem(ScriptTrigger.MetadataRefreshCompleted, e.Argument);

    private void DispatchItem(ScriptTrigger trigger, BaseItem? item)
    {
        Guard(trigger.ToString(), () =>
        {
            if (item is not null && !item.IsVirtualItem && ScriptDispatcher.WantsItem(trigger, item.GetBaseItemKind()))
            {
                _dispatcher.Dispatch(_builder.FromItem(trigger, item));
            }
        });
    }

    private void OnLyricDownloadFailure(object? sender, LyricDownloadFailureEventArgs e)
    {
        Guard("lyrics download failure", () =>
        {
            if (ScriptDispatcher.HasScriptsFor(ScriptTrigger.LyricDownloadFailed))
            {
                _dispatcher.Dispatch(_builder.FromProviderFailure(ScriptTrigger.LyricDownloadFailed, e.Item, e.Provider, e.Exception));
            }
        });
    }

    private void OnLibraryOptionsUpdated(object? sender, LibraryOptionsUpdatedEventArgs e)
        => DispatchValues(ScriptTrigger.LibrarySettingsChanged, ("LIBRARY_PATH", e.LibraryPath));

    private void OnTimerCreated(object? sender, GenericEventArgs<TimerEventInfo> e) => DispatchRecording(ScriptTrigger.RecordingScheduled, e.Argument);

    private void OnTimerCancelled(object? sender, GenericEventArgs<TimerEventInfo> e) => DispatchRecording(ScriptTrigger.RecordingCancelled, e.Argument);

    private void OnSeriesTimerCreated(object? sender, GenericEventArgs<TimerEventInfo> e) => DispatchRecording(ScriptTrigger.SeriesRecordingScheduled, e.Argument);

    private void OnSeriesTimerCancelled(object? sender, GenericEventArgs<TimerEventInfo> e) => DispatchRecording(ScriptTrigger.SeriesRecordingCancelled, e.Argument);

    private void DispatchRecording(ScriptTrigger trigger, TimerEventInfo? timer)
    {
        Guard(trigger.ToString(), () =>
        {
            if (timer is not null && ScriptDispatcher.HasScriptsFor(trigger))
            {
                _dispatcher.Dispatch(_builder.FromRecording(trigger, timer.Id, timer.ProgramId));
            }
        });
    }

    private void OnDeviceOptionsUpdated(object? sender, GenericEventArgs<Tuple<string, DeviceOptions>> e)
        => DispatchValues(ScriptTrigger.DeviceSettingsChanged, ("DEVICE_ID", e.Argument?.Item1), ("DEVICE_NAME", e.Argument?.Item2?.CustomName));

    private void OnActivityEntryCreated(object? sender, GenericEventArgs<ActivityLogEntry> e)
    {
        Guard("activity logged", () =>
        {
            // Entries this plugin writes are left out, so a script that writes to the activity log never
            // triggers itself.
            var entry = e.Argument;
            if (entry is null || (entry.Type ?? string.Empty).StartsWith("Scripts.", StringComparison.Ordinal) || !ScriptDispatcher.HasScriptsFor(ScriptTrigger.ActivityLogged))
            {
                return;
            }

            _dispatcher.Dispatch(_builder.FromActivity(entry));
        });
    }

    private void OnConfigurationUpdated(object? sender, EventArgs e) => DispatchValues(ScriptTrigger.ServerSettingsChanged, ("SETTINGS_KEY", string.Empty));

    private void OnNamedConfigurationUpdated(object? sender, ConfigurationUpdateEventArgs e) => DispatchValues(ScriptTrigger.ServerSettingsChanged, ("SETTINGS_KEY", e.Key));

    private void OnNetworkChanged(object? sender, EventArgs e) => DispatchValues(ScriptTrigger.NetworkChanged);

    // Jellyfin reports an account edit through this event, its event bus, or both, depending on which part of
    // the server made it, so both feed User edited and UserEvents drops the duplicate. Neither reports a change
    // to a user's permissions or policy, which the trigger's description says.
    private void OnUserUpdated(object? sender, GenericEventArgs<Jellyfin.Database.Implementations.Entities.User> e)
    {
        Guard("user updated", () =>
        {
            if (e.Argument is { } user && UserEvents.ShouldDispatch(user.Id) && ScriptDispatcher.HasScriptsFor(ScriptTrigger.UserUpdated))
            {
                _dispatcher.Dispatch(_builder.FromUser(ScriptTrigger.UserUpdated, user));
            }
        });
    }

    private void DispatchValues(ScriptTrigger trigger, params (string Name, string? Value)[] values)
    {
        Guard(trigger.ToString(), () =>
        {
            if (ScriptDispatcher.HasScriptsFor(trigger))
            {
                _dispatcher.Dispatch(_builder.FromValues(trigger, values));
            }
        });
    }

    // ---- Tick ----

    private void Tick()
    {
        if (_stopping)
        {
            return;
        }

        var now = DateTime.UtcNow;
        Guard("items waiting for metadata", () => ReleasePendingItems(now));
        Guard("paused for a while", () => FirePaused(now));
        Guard("playing for a while", () => FirePlaying(now));
        Guard("schedules", () => FireSchedules(now));

        // Rescheduled after each pass rather than on a fixed period, so a slow pass never overlaps the next.
        try
        {
            if (!_stopping)
            {
                _tickTimer?.Change(TickInterval, Timeout.InfiniteTimeSpan);
            }
        }
        catch (ObjectDisposedException)
        {
            // Server shutting down.
        }
    }

    // Only items that are ready, or have waited as long as they may, are looked up, once each.
    private void ReleasePendingItems(DateTime now)
    {
        foreach (var (id, pending) in _pendingItems.ToArray())
        {
            var age = now - pending.Added;
            if (!((pending.Ready && age >= TickInterval) || age >= MetadataWaitLimit) || !_pendingItems.TryRemove(id, out _))
            {
                continue;
            }

            if (_libraryManager.GetItemById(id) is { } item)
            {
                _dispatcher.Dispatch(_builder.FromItem(ScriptTrigger.ItemAdded, item));
            }
        }
    }

    private static bool HasMetadata(BaseItem item) => item.ProviderIds is { Count: > 0 };

    private void FirePaused(DateTime now)
    {
        foreach (var script in ScriptDispatcher.ScriptsFor(ScriptTrigger.PlaybackPausedFor))
        {
            foreach (var (args, paused) in _playback.DuePaused(script.Id, TimeSpan.FromMinutes(script.PausedForMinutes), now))
            {
                var context = _builder.FromPlayback(ScriptTrigger.PlaybackPausedFor, args, _playback.PlayMethodOf(PlayKey(args)));
                context.Set("PAUSED_SECONDS", ((long)paused.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                context.Set("IS_PAUSED", "true");
                _dispatcher.DispatchTo(context, script);
            }
        }
    }

    private void FirePlaying(DateTime now)
    {
        foreach (var script in ScriptDispatcher.ScriptsFor(ScriptTrigger.PlaybackPlayingFor))
        {
            foreach (var (args, played) in _playback.DuePlaying(script.Id, TimeSpan.FromMinutes(script.PlayingForMinutes), now))
            {
                var context = _builder.FromPlayback(ScriptTrigger.PlaybackPlayingFor, args, _playback.PlayMethodOf(PlayKey(args)));
                context.Set("PLAYING_SECONDS", ((long)played.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                _dispatcher.DispatchTo(context, script);
            }
        }
    }

    // The state is read from disk once and kept, since only this class writes it.
    private void FireSchedules(DateTime now)
    {
        var scripts = ScriptDispatcher.ScriptsFor(ScriptTrigger.Schedule).ToList();
        var state = _scheduleState ??= _scheduleStore.Load();

        // A script that is disabled, deleted, or no longer scheduled is forgotten, so an interval script enabled
        // again waits a whole interval rather than running at once for the time it was off.
        var stale = state.LastRuns.Keys.Where(id => !scripts.Any(s => s.Id == id)).ToList();
        var changed = stale.Count > 0;
        foreach (var id in stale)
        {
            state.LastRuns.Remove(id);
        }

        foreach (var script in scripts)
        {
            DateTime? last = state.LastRuns.TryGetValue(script.Id, out var stored) ? stored : null;
            if (last is null && script.ScheduleMode == ScheduleMode.Interval)
            {
                state.LastRuns[script.Id] = now;
                changed = true;
                continue;
            }

            if (ScheduleCalculator.IsDue(script, now, TimeZoneInfo.Local, last, out var runUtc))
            {
                state.LastRuns[script.Id] = runUtc;
                changed = true;
                _dispatcher.DispatchTo(_builder.FromSystem(ScriptTrigger.Schedule), script);
            }
        }

        if (changed)
        {
            _scheduleStore.Save(state);
        }
    }

    private readonly record struct PendingItem(DateTime Added, bool Ready);

    private void Guard(string what, Action action)
    {
        // An exception here would surface inside the server's own event raise, so it is logged and contained.
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scripts: could not handle {Event}", what);
        }
    }
}
