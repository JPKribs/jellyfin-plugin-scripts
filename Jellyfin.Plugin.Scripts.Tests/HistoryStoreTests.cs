using System;
using System.IO;
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

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }
}
