using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// One script's Jellyfin scheduled task. It shows under Scheduled Tasks, in the Scripts category, under the
/// script's own name and description, and Jellyfin decides when it runs. The server tells tasks apart by their
/// class, so <see cref="ScriptScheduledTasks"/> makes one small subclass of this per script.
/// </summary>
public abstract class ScriptScheduledTask : IScheduledTask, IConfigurableScheduledTask
{
    /// <summary>The category every script's task is listed under.</summary>
    public const string TaskCategory = "Scripts";

    private ScriptRunner? _runner;
    private EventContextBuilder? _builder;

    /// <summary>Gets the script this task runs.</summary>
    public Guid ScriptId { get; private set; }

    /// <inheritdoc />
    public string Name => Script()?.Name ?? "Deleted script";

    /// <inheritdoc />
    public string Key => KeyFor(ScriptId);

    /// <inheritdoc />
    public string Description => Script()?.Description ?? string.Empty;

    /// <inheritdoc />
    public string Category => TaskCategory;

    /// <summary>
    /// Gets a value indicating whether the task is left out of the dashboard: its script was deleted or is no
    /// longer scheduled. The server cannot drop a task until it restarts, so until then it is hidden.
    /// </summary>
    public bool IsHidden => Scheduled() is null;

    /// <summary>Gets a value indicating whether the task's own triggers may start it: only while its script is enabled.</summary>
    public bool IsEnabled => Scheduled() is { Enabled: true };

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <summary>
    /// Gets the key of a script's task.
    /// </summary>
    /// <param name="scriptId">The script.</param>
    /// <returns>The key.</returns>
    public static string KeyFor(Guid scriptId) => "Scripts" + scriptId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Ties the task to its script. Called once, right after the task is made.
    /// </summary>
    /// <param name="scriptId">The script.</param>
    /// <param name="runner">Runs the script.</param>
    /// <param name="builder">Builds the schedule's event.</param>
    public void Initialize(Guid scriptId, ScriptRunner runner, EventContextBuilder builder)
    {
        ScriptId = scriptId;
        _runner = runner;
        _builder = builder;
    }

    /// <summary>
    /// Gets the triggers a task starts with, read from a script's starting schedule. Once the administrator
    /// changes them under Scheduled Tasks, Jellyfin keeps its own.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <returns>The triggers.</returns>
    public static IEnumerable<TaskTriggerInfo> DefaultTriggers(ScriptDefinition? script)
    {
        if (script is null)
        {
            yield break;
        }

        if (script.ScheduleMode == ScheduleMode.Interval)
        {
            if (script.ScheduleIntervalMinutes > 0)
            {
                yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromMinutes(script.ScheduleIntervalMinutes).Ticks };
            }

            yield break;
        }

        // Jellyfin has no monthly trigger, so a script that asked for one starts with none.
        if (script.ScheduleMode != ScheduleMode.Daily || !ScheduleTimes.TryParse(script.ScheduleTime, out var time))
        {
            yield break;
        }

        if (script.ScheduleDays.Count == 0)
        {
            yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = time.Ticks };
            yield break;
        }

        foreach (var day in script.ScheduleDays.Distinct())
        {
            yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.WeeklyTrigger, DayOfWeek = day, TimeOfDayTicks = time.Ticks };
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => DefaultTriggers(Script());

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var script = Scheduled();
        if (script is null || !script.Enabled || _runner is null || _builder is null)
        {
            // Run by hand while disabled, or a task whose script is gone: there is nothing to do.
            progress.Report(100);
            return;
        }

        var context = _builder.FromSystem(ScriptTrigger.Schedule);
        if (!TriggerCatalog.Expands(context.Trigger, script.ForEach) && !ConditionEvaluator.Matches(script, context))
        {
            progress.Report(100);
            return;
        }

        var failure = await _runner.RunJobAsync(new ScriptJob(script, context), cancellationToken).ConfigureAwait(false);
        progress.Report(100);
        if (failure is not null)
        {
            // Thrown so Jellyfin shows the task as failed, with the step's own message.
            throw new InvalidOperationException(failure);
        }
    }

    private ScriptDefinition? Script()
        => Plugin.Instance?.ReadConfiguration(c => c.Scripts.FirstOrDefault(s => s.Id == ScriptId));

    private ScriptDefinition? Scheduled()
        => Script() is { } script && script.Triggers.Contains(ScriptTrigger.Schedule) ? script : null;
}
