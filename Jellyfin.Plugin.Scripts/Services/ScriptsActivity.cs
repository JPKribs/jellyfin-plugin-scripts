using System;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// The plugin's summed activity entries. A script set off by a busy event, such as items being added during
/// a library scan, can fail many times in a row, so failures are summed into one entry rather than one each.
/// Registered as a singleton, so whatever is waiting is written when the server shuts down.
/// </summary>
public sealed class ScriptsActivity : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsActivity"/> class, writing in the server's display language.
    /// </summary>
    /// <param name="activityLogger">The activity log writer.</param>
    /// <param name="config">The server configuration, read for its display language each time an entry is written.</param>
    public ScriptsActivity(ActivityLogger activityLogger, IServerConfigurationManager config)
        : this(new ActivitySummary(activityLogger), () => BaseTranslations.ServerLanguage(config))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsActivity"/> class, writing in US English.
    /// </summary>
    /// <param name="activityLogger">The activity log writer.</param>
    public ScriptsActivity(ActivityLogger activityLogger)
        : this(new ActivitySummary(activityLogger), () => null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsActivity"/> class around a given summary, for tests.
    /// </summary>
    /// <param name="summary">The summary entries are counted into.</param>
    /// <param name="language">Gives the language entries are written in, <c>null</c> for US English.</param>
    public ScriptsActivity(ActivitySummary summary, Func<string?> language)
    {
        Summary = summary;
        RunFailed = RunFailedIn(language);
    }

    /// <summary>Gets the outcome of script runs that ended with a failed step, by script name, with each first failure's message.</summary>
    public ActivityOutcome RunFailed { get; }

    /// <summary>Gets the summary entries are counted into.</summary>
    public ActivitySummary Summary { get; }

    /// <summary>Counts one failed run toward the next summary.</summary>
    /// <param name="scriptName">The script's name.</param>
    /// <param name="message">The first failed step's message.</param>
    public void RunFailedFor(string scriptName, string? message)
    {
        var reason = ActivityText.Sentence(message);
        Summary.Add(RunFailed, scriptName, string.IsNullOrWhiteSpace(reason) ? null : reason);
    }

    /// <summary>
    /// Builds the outcome of failed script runs, whose overview is written in a language.
    /// </summary>
    /// <param name="language">Gives the language, read each time an overview is written. <c>null</c> is US English.</param>
    /// <returns>The outcome.</returns>
    public static ActivityOutcome RunFailedIn(Func<string?> language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return new ActivityOutcome(
            "Scripts.ScriptFailed",
            "Failed",
            tally => tally.Reasons.Count > 0
                ? BaseTranslations.Format(tally.Total == 1 ? TranslationKeys.ActivityRunFailedWithErrors : TranslationKeys.ActivityRunsFailedWithErrors, language(), tally.Total, ActivityText.CountedList(tally.Sources), ActivityText.CountedList(tally.Reasons))
                : BaseTranslations.Format(tally.Total == 1 ? TranslationKeys.ActivityRunFailed : TranslationKeys.ActivityRunsFailed, language(), tally.Total, ActivityText.CountedList(tally.Sources)),
            LogLevel.Error,
            "Script Run");
    }

    /// <inheritdoc />
    public void Dispose() => Summary.Dispose();
}
