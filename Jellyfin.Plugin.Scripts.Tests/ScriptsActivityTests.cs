using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

/// <summary>
/// Guards the failed run entry: one entry names each script and error, in the language it is given.
/// </summary>
public sealed class ScriptsActivityTests
{
    private static readonly ActivityOutcome RunFailed = ScriptsActivity.RunFailed;

    /// <summary>
    /// Several failed runs read as one entry with a plural name and an overview that lists scripts and errors.
    /// True: the activity log shows "Script runs failed" with each script and error, counted when more than one.
    /// False: the entry would read as a bare key or lose the scripts and errors.
    /// </summary>
    [Fact]
    public void FailedRunsReadAsOneSummary()
    {
        var tally = new ActivityTally(
            3,
            new Dictionary<string, int> { ["Notify"] = 2, ["Backup"] = 1 },
            new Dictionary<string, int> { ["Not an http or https URL: not a url"] = 3 });

        Assert.Equal("Script runs failed", BaseTranslations.Format(RunFailed.NameMany!, "en-us", tally.Total));
        Assert.Equal(
            "3 runs failed in Notify (2), Backup, with errors Not an http or https URL: not a url",
            ActivityText.Sentence(RunFailed.LanguageOverview!(tally, "en-us")));
        Assert.Equal("Scripts.ScriptFailed", RunFailed.Type);
        Assert.Equal(LogLevel.Error, RunFailed.Severity);
    }

    /// <summary>
    /// One failed run reads in the singular and names its script with no count.
    /// True: the activity log shows "Script run failed" reading "Notify failed".
    /// False: a single failure would read as "Script runs failed" or repeat a count of 1.
    /// </summary>
    [Fact]
    public void OneFailedRunReadsInTheSingular()
    {
        var tally = new ActivityTally(1, new Dictionary<string, int> { ["Notify"] = 1 }, new Dictionary<string, int>());

        Assert.Equal("Script run failed", BaseTranslations.Format(RunFailed.NameOne!, "en-us", tally.Total));
        Assert.Equal("Notify failed", ActivityText.Sentence(RunFailed.LanguageOverview!(tally, "en-us")));
    }

    /// <summary>
    /// One failed run with an error names the script and the error once each.
    /// True: the activity log reads "Notify failed with the error The chosen collection no longer exists".
    /// False: the error would be dropped or carry a count of 1.
    /// </summary>
    [Fact]
    public void OneFailedRunNamesItsError()
    {
        var tally = new ActivityTally(
            1,
            new Dictionary<string, int> { ["Notify"] = 1 },
            new Dictionary<string, int> { ["The chosen collection no longer exists"] = 1 });

        Assert.Equal(
            "Notify failed with the error The chosen collection no longer exists",
            ActivityText.Sentence(RunFailed.LanguageOverview!(tally, "en-us")));
    }
}
