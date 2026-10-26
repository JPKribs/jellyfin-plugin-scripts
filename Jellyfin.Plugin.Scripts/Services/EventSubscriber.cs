using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Listens to the server's playback, library, and scheduled task events for as long as the server runs, and
/// hands each one to the dispatcher. Sign ins arrive through <see cref="AuthenticationConsumers"/> instead.
/// </summary>
public sealed class EventSubscriber : IHostedService, IDisposable
{
    /// <summary>How often items waiting for metadata are checked.</summary>
    public static readonly TimeSpan PendingInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long a new item may wait for metadata before it is dispatched without it.</summary>
    public static readonly TimeSpan MetadataWaitLimit = TimeSpan.FromSeconds(60);

    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ITaskManager _taskManager;
    private readonly EventContextBuilder _builder;
    private readonly ScriptDispatcher _dispatcher;
    private readonly PauseTracker _pauses = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _pendingItems = new();
    private readonly ILogger<EventSubscriber> _logger;
    private Timer? _pendingTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSubscriber"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="taskManager">The scheduled task manager.</param>
    /// <param name="builder">The event context builder.</param>
    /// <param name="dispatcher">The script dispatcher.</param>
    /// <param name="logger">The logger.</param>
    public EventSubscriber(
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        ITaskManager taskManager,
        EventContextBuilder builder,
        ScriptDispatcher dispatcher,
        ILogger<EventSubscriber> logger)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _taskManager = taskManager;
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
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _taskManager.TaskCompleted += OnTaskCompleted;
        _pendingTimer = new Timer(_ => CheckPendingItems(), null, PendingInterval, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        _taskManager.TaskCompleted -= OnTaskCompleted;
        _pendingTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _pendingTimer?.Dispose();

    private static string PlayKey(PlaybackProgressEventArgs e)
        => e.PlaySessionId ?? (e.Session?.Id + "|" + e.Item?.Id.ToString("N", CultureInfo.InvariantCulture));

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        Guard("playback start", () =>
        {
            _pauses.Started(PlayKey(e));
            Dispatch(ScriptTrigger.PlaybackStart, e);
        });
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        Guard("playback progress", () =>
        {
            var edge = _pauses.Progress(PlayKey(e), e.IsPaused);
            if (edge is { } paused)
            {
                Dispatch(paused ? ScriptTrigger.PlaybackPause : ScriptTrigger.PlaybackResume, e);
            }
        });
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        Guard("playback stop", () =>
        {
            _pauses.Stopped(PlayKey(e));
            Dispatch(ScriptTrigger.PlaybackStop, e);
        });
    }

    private void Dispatch(ScriptTrigger trigger, PlaybackProgressEventArgs e)
    {
        if (ScriptDispatcher.HasScriptsFor(trigger))
        {
            _dispatcher.Dispatch(_builder.FromPlayback(trigger, e));
        }
    }

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

            // The event fires before metadata is fetched, so the item waits until it has provider IDs.
            _pendingItems.TryAdd(item.Id, DateTime.UtcNow);
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

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
    {
        Guard("task completed", () =>
        {
            if (ScriptDispatcher.HasScriptsFor(ScriptTrigger.TaskCompleted))
            {
                _dispatcher.Dispatch(_builder.FromTask(e));
            }
        });
    }

    private void CheckPendingItems()
    {
        try
        {
            var now = DateTime.UtcNow;
            foreach (var (id, added) in _pendingItems.ToArray())
            {
                var item = _libraryManager.GetItemById(id);
                if (item is null)
                {
                    _pendingItems.TryRemove(id, out _);
                    continue;
                }

                var age = now - added;
                var hasMetadata = item.ProviderIds is { Count: > 0 };
                if ((hasMetadata && age >= PendingInterval) || age >= MetadataWaitLimit)
                {
                    if (_pendingItems.TryRemove(id, out _))
                    {
                        _dispatcher.Dispatch(_builder.FromItem(ScriptTrigger.ItemAdded, item));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scripts: could not check items waiting for metadata");
        }
        finally
        {
            // Rescheduled after each pass rather than on a fixed period, so a slow pass never overlaps the next.
            try
            {
                _pendingTimer?.Change(PendingInterval, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Server shutting down.
            }
        }
    }

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
