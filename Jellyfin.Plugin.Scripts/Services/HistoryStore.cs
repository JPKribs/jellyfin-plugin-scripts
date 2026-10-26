using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Keeps the most recent runs, newest first. They are held in memory and written to a JSON file beside the
/// plugin configuration shortly after they change, so a burst of runs costs one write rather than one each,
/// and busy servers never rewrite their settings on every event.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    /// <summary>The filter value that keeps only failed runs.</summary>
    public const string FailedFilter = "failed";

    /// <summary>The filter value that keeps only test runs.</summary>
    public const string TestFilter = "test";

    /// <summary>The most output kept per step in the history, in characters.</summary>
    public const int MaxStoredOutputChars = 8 * 1024;

    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(2);

    private readonly JsonFileStore<ScriptHistory> _store;
    private readonly object _lock = new();
    private readonly object _saveLock = new();
    private readonly Timer _flushTimer;
    private List<ScriptRun>? _runs;
    private bool _dirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryStore"/> class.
    /// </summary>
    /// <param name="paths">The plugin paths.</param>
    /// <param name="logger">The logger.</param>
    public HistoryStore(ScriptPaths paths, ILogger<HistoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _store = new JsonFileStore<ScriptHistory>(paths.HistoryFile, logger);
        _flushTimer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Records a run, dropping the oldest past <paramref name="limit"/>. Each step's output is shortened to
    /// <see cref="MaxStoredOutputChars"/>.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="limit">How many runs to keep. Zero keeps none.</param>
    public void Add(ScriptRun run, int limit)
    {
        ArgumentNullException.ThrowIfNull(run);
        foreach (var step in run.Operations)
        {
            if (step.Output.Length > MaxStoredOutputChars)
            {
                step.Output = step.Output[..MaxStoredOutputChars] + "\n[output shortened in the history]";
            }
        }

        lock (_lock)
        {
            var runs = Runs();
            if (limit > 0)
            {
                runs.Insert(0, run);
            }

            var keep = Math.Max(limit, 0);
            if (runs.Count > keep)
            {
                runs.RemoveRange(keep, runs.Count - keep);
            }

            _dirty = true;
        }

        _flushTimer.Change(FlushDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Reads a page of runs, newest first.
    /// </summary>
    /// <param name="query">The paging, search, and filter. The filter is <see cref="FailedFilter"/>,
    /// <see cref="TestFilter"/>, or a script ID.</param>
    /// <returns>The page.</returns>
    public PagedResult<ScriptRun> Query(PagedQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEnumerable<ScriptRun> runs;
        lock (_lock)
        {
            runs = Runs().ToList();
        }

        var filter = query.Filter?.Trim();
        if (string.Equals(filter, FailedFilter, StringComparison.OrdinalIgnoreCase))
        {
            runs = runs.Where(r => !r.Succeeded);
        }
        else if (string.Equals(filter, TestFilter, StringComparison.OrdinalIgnoreCase))
        {
            runs = runs.Where(r => r.IsTest);
        }
        else if (Guid.TryParse(filter, out var scriptId))
        {
            runs = runs.Where(r => r.ScriptId == scriptId);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            runs = runs.Where(r =>
                r.ScriptName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.Summary.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.Trigger.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return runs.ToList().ToPagedResult(query);
    }

    /// <summary>
    /// Deletes every run, on disk too.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _runs = new List<ScriptRun>();
            _dirty = true;
        }

        Flush();
    }

    /// <summary>
    /// Writes any unsaved runs now.
    /// </summary>
    public void Flush()
    {
        // The snapshot is taken and written under one save lock, so a timer flush holding an older snapshot can
        // never write after a newer one, such as the empty history Clear just made.
        lock (_saveLock)
        {
            ScriptHistory snapshot;
            lock (_lock)
            {
                if (!_dirty || _runs is null)
                {
                    return;
                }

                snapshot = new ScriptHistory { Runs = _runs.ToList() };
                _dirty = false;
            }

            _store.Save(snapshot);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush();
    }

    // Loaded on first use rather than at startup, so a large history never slows the server starting.
    private List<ScriptRun> Runs() => _runs ??= _store.Load().Runs;
}
