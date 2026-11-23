namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// How a scheduled script comes due.
/// </summary>
public enum ScheduleMode
{
    /// <summary>Every <see cref="ScriptDefinition.ScheduleIntervalMinutes"/> minutes.</summary>
    Interval,

    /// <summary>At <see cref="ScriptDefinition.ScheduleTime"/> on the chosen days of the week.</summary>
    Daily,

    /// <summary>At <see cref="ScriptDefinition.ScheduleTime"/> on <see cref="ScriptDefinition.ScheduleMonthDay"/> each month.</summary>
    Monthly,

    /// <summary>No schedule of its own. The task starts with no triggers, set under Scheduled Tasks.</summary>
    None
}
