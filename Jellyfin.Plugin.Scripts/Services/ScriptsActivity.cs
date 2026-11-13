using System;
using System.Diagnostics.CodeAnalysis;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Logging;

// Base's shared source is compiled into this assembly under its analyzers, and this one method of Base trips
// a performance suggestion that does not apply to it.
[assembly: SuppressMessage("Performance", "CA1867:Use char overload", Scope = "member", Target = "~M:JPKribs.Jellyfin.Base.ActivityText.Plural(System.Int32,System.String)~System.String", Justification = "Base's shared source, not this plugin's code.")]

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// The plugin's summed activity entries. A script set off by a busy event, such as items being added during
/// a library scan, can fail many times in a row, so failures are summed into one entry rather than one each.
/// Registered as a singleton, so whatever is waiting is written when the server shuts down.
/// </summary>
public sealed class ScriptsActivity : IDisposable
{
    /// <summary>Script runs that ended with a failed step, by script name, with each first failure's message.</summary>
    public static readonly ActivityOutcome RunFailed = new(
        "Scripts.ScriptFailed",
        "Failed",
        tally => ActivityText.Count(tally.Total, "script run") + " failed in " + ActivityText.CountedList(tally.Sources)
            + (tally.Reasons.Count > 0 ? ", with errors " + ActivityText.CountedList(tally.Reasons) : string.Empty),
        LogLevel.Error,
        "Script Run");

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsActivity"/> class.
    /// </summary>
    /// <param name="activityLogger">The activity log writer.</param>
    public ScriptsActivity(ActivityLogger activityLogger)
        : this(new ActivitySummary(activityLogger))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsActivity"/> class around a given summary, for tests.
    /// </summary>
    /// <param name="summary">The summary entries are counted into.</param>
    public ScriptsActivity(ActivitySummary summary)
    {
        Summary = summary;
    }

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

    /// <inheritdoc />
    public void Dispose() => Summary.Dispose();
}
