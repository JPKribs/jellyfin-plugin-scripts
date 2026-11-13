using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public sealed class ScriptsActivityTests
{
    [Fact]
    public void FailedRunsReadAsOneSummary()
    {
        var tally = new ActivityTally(
            3,
            new Dictionary<string, int> { ["Notify"] = 2, ["Backup"] = 1 },
            new Dictionary<string, int> { ["Not an http or https URL: not a url"] = 3 });

        Assert.Equal("3 Script Runs Failed", ActivityText.SummaryName(tally.Total, ScriptsActivity.RunFailed.Outcome, ScriptsActivity.RunFailed.Noun));
        Assert.Equal(
            "3 script runs failed in Notify (2), Backup (1), with errors Not an http or https URL: not a url (3)",
            ActivityText.Sentence(ScriptsActivity.RunFailed.Overview(tally)));
        Assert.Equal("Scripts.ScriptFailed", ScriptsActivity.RunFailed.Type);
        Assert.Equal(LogLevel.Error, ScriptsActivity.RunFailed.Severity);
    }

    [Fact]
    public void OneFailedRunReadsInTheSingular()
    {
        var tally = new ActivityTally(1, new Dictionary<string, int> { ["Notify"] = 1 }, new Dictionary<string, int>());

        Assert.Equal("1 Script Run Failed", ActivityText.SummaryName(tally.Total, ScriptsActivity.RunFailed.Outcome, ScriptsActivity.RunFailed.Noun));
        Assert.Equal("1 script run failed in Notify (1)", ActivityText.Sentence(ScriptsActivity.RunFailed.Overview(tally)));
    }
}
