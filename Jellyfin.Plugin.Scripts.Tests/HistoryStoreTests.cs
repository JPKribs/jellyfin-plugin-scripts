using System;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "scripts-history-" + Guid.NewGuid().ToString("N"));

    private HistoryStore Create()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.PluginConfigurationsPath.Returns(_folder);
        return new HistoryStore(new ScriptPaths(paths), NullLogger<HistoryStore>.Instance);
    }

    private static ScriptRun Run(string name, bool ok = true, string output = "")
    {
        var run = new ScriptRun { Id = Guid.NewGuid(), ScriptName = name, Succeeded = ok };
        run.Operations.Add(new OperationResult { Output = output });
        return run;
    }

    [Fact]
    public void KeepsTheNewestRunsUpToTheLimit()
    {
        using var store = Create();
        store.Add(Run("a"), 2);
        store.Add(Run("b"), 2);
        store.Add(Run("c"), 2);

        var page = store.Query(new PagedQuery());
        Assert.Equal(new[] { "c", "b" }, new[] { page.Items[0].ScriptName, page.Items[1].ScriptName });
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public void ALimitOfZeroClearsExistingRuns()
    {
        using var store = Create();
        store.Add(Run("a"), 5);
        store.Add(Run("b"), 0);

        Assert.Equal(0, store.Query(new PagedQuery()).TotalCount);
    }

    [Fact]
    public void ShortensLongOutput()
    {
        using var store = Create();
        store.Add(Run("a", output: new string('x', HistoryStore.MaxStoredOutputChars * 2)), 5);

        var output = store.Query(new PagedQuery()).Items[0].Operations[0].Output;
        Assert.True(output.Length < HistoryStore.MaxStoredOutputChars + 100);
    }

    [Fact]
    public void FiltersFailedRuns()
    {
        using var store = Create();
        store.Add(Run("ok"), 5);
        store.Add(Run("bad", ok: false), 5);

        var page = store.Query(new PagedQuery { Filter = HistoryStore.FailedFilter });
        Assert.Equal("bad", Assert.Single(page.Items).ScriptName);
    }

    [Fact]
    public void SurvivesARestart()
    {
        using (var store = Create())
        {
            store.Add(Run("kept"), 5);
        }

        using var reopened = Create();
        Assert.Equal("kept", Assert.Single(reopened.Query(new PagedQuery()).Items).ScriptName);
    }

    [Fact]
    public void FiltersTestRunsAndRunsOfOneScript()
    {
        using var store = Create();
        var scriptId = Guid.NewGuid();
        var mine = Run("mine");
        mine.ScriptId = scriptId;
        var test = Run("test");
        test.IsTest = true;
        store.Add(mine, 5);
        store.Add(test, 5);
        store.Add(Run("other"), 5);

        Assert.Equal("test", Assert.Single(store.Query(new PagedQuery { Filter = HistoryStore.TestFilter }).Items).ScriptName);
        Assert.Equal("mine", Assert.Single(store.Query(new PagedQuery { Filter = scriptId.ToString() }).Items).ScriptName);
        Assert.Equal(3, store.Query(new PagedQuery { Filter = "unknown" }).TotalCount);
    }

    [Fact]
    public void SearchesNamesSummariesAndTriggers()
    {
        using var store = Create();
        var played = Run("Notify");
        played.Summary = "alice · The Matrix";
        played.Trigger = ScriptTrigger.PlaybackStart;
        var added = Run("Tag new");
        added.Trigger = ScriptTrigger.ItemAdded;
        store.Add(played, 5);
        store.Add(added, 5);

        Assert.Equal("Notify", Assert.Single(store.Query(new PagedQuery { Search = "matrix" }).Items).ScriptName);
        Assert.Equal("Tag new", Assert.Single(store.Query(new PagedQuery { Search = " itemadded " }).Items).ScriptName);
        Assert.Equal("Tag new", Assert.Single(store.Query(new PagedQuery { Search = "TAG" }).Items).ScriptName);
    }

    [Fact]
    public void ClearingEmptiesTheFileToo()
    {
        using (var store = Create())
        {
            store.Add(Run("gone"), 5);
            store.Flush();
            store.Clear();
        }

        using var reopened = Create();
        Assert.Equal(0, reopened.Query(new PagedQuery()).TotalCount);
    }

    [Fact]
    public async Task ASteadyStreamOfRunsIsStillWritten()
    {
        // Runs arrive faster than the flush delay, and the history still reaches disk while they keep coming.
        using var store = Create();
        var file = Path.Combine(_folder, "Jellyfin.Plugin.Scripts.History.json");
        var until = DateTime.UtcNow + (HistoryStore.FlushDelay * 4);
        while (DateTime.UtcNow < until && !File.Exists(file))
        {
            store.Add(Run("busy"), 50);
            await Task.Delay(200);
        }

        Assert.True(File.Exists(file));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }
}
