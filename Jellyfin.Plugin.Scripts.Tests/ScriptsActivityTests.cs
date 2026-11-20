using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

/// <summary>
/// Guards the failed run entry: one entry names each script and error with its count, in the language it is given.
/// </summary>
public sealed class ScriptsActivityTests
{
    private static readonly ActivityOutcome RunFailed = ScriptsActivity.RunFailed;

    /// <summary>
    /// Several failed runs read as one entry with a plural name and an overview that lists scripts and errors.
    /// True: the activity log shows "3 script runs failed" with each script and error counted.
    /// False: the entry would read as a bare key or lose the scripts and errors.
    /// </summary>
    [Fact]
    public void FailedRunsReadAsOneSummary()
    {
        var tally = new ActivityTally(
            3,
            new Dictionary<string, int> { ["Notify"] = 2, ["Backup"] = 1 },
            new Dictionary<string, int> { ["Not an http or https URL: not a url"] = 3 });

        Assert.Equal("3 script runs failed", BaseTranslations.Format(RunFailed.NameMany!, "en-us", tally.Total));
        Assert.Equal(
            "3 script runs failed in Notify (2), Backup (1), with errors Not an http or https URL: not a url (3)",
            ActivityText.Sentence(RunFailed.LanguageOverview!(tally, "en-us")));
        Assert.Equal("Scripts.ScriptFailed", RunFailed.Type);
        Assert.Equal(LogLevel.Error, RunFailed.Severity);
    }

    /// <summary>
    /// One failed run reads in the singular.
    /// True: the activity log shows "1 script run failed".
    /// False: a single failure would read as "1 script runs failed".
    /// </summary>
    [Fact]
    public void OneFailedRunReadsInTheSingular()
    {
        var tally = new ActivityTally(1, new Dictionary<string, int> { ["Notify"] = 1 }, new Dictionary<string, int>());

        Assert.Equal("1 script run failed", BaseTranslations.Format(RunFailed.NameOne!, "en-us", tally.Total));
        Assert.Equal("1 script run failed in Notify (1)", ActivityText.Sentence(RunFailed.LanguageOverview!(tally, "en-us")));
    }
}
