using System;
using System.Linq;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Keeps the most recent runs, newest first, in a JSON file beside the plugin configuration. Runs are kept
/// out of the configuration itself so a busy server does not rewrite its settings on every event.
/// </summary>
public class HistoryStore
{
    /// <summary>The filter value that keeps only failed runs.</summary>
    public const string FailedFilter = "failed";

    /// <summary>The filter value that keeps only test runs.</summary>
    public const string TestFilter = "test";

    private readonly JsonFileStore<ScriptHistory> _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryStore"/> class.
    /// </summary>
    /// <param name="paths">The plugin paths.</param>
    /// <param name="logger">The logger.</param>
    public HistoryStore(ScriptPaths paths, ILogger<HistoryStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _store = new JsonFileStore<ScriptHistory>(paths.HistoryFile, logger);
    }

    /// <summary>
    /// Records a run, dropping the oldest past <paramref name="limit"/>.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="limit">How many runs to keep. Zero keeps none.</param>
    public void Add(ScriptRun run, int limit)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (limit <= 0)
        {
            return;
        }

        _store.Update(history =>
        {
            history.Runs.Insert(0, run);
            if (history.Runs.Count > limit)
            {
                history.Runs.RemoveRange(limit, history.Runs.Count - limit);
            }
        });
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
        var runs = _store.Load().Runs.AsEnumerable();

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
    /// Deletes every run.
    /// </summary>
    public void Clear() => _store.Save(new ScriptHistory());
}
