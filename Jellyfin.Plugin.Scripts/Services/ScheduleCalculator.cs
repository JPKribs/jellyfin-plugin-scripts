using System;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Decides when a scheduled script is due. Daily and monthly schedules run once in a short window after their
/// time, so a tick delayed by a busy server still runs them but a server that was off at the time does not run
/// them late. The last run is stored, so a restart neither repeats a run nor resets an interval.
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>How late a daily or monthly schedule may still run.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets whether a schedule is due.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="nowUtc">The current time, UTC.</param>
    /// <param name="localZone">The server's time zone.</param>
    /// <param name="lastRunUtc">When the schedule last ran, or null when it never has.</param>
    /// <param name="runUtc">The time to store as this run when due.</param>
    /// <returns>True when the script should run now.</returns>
    public static bool IsDue(ScriptDefinition script, DateTime nowUtc, TimeZoneInfo localZone, DateTime? lastRunUtc, out DateTime runUtc)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(localZone);
        runUtc = nowUtc;

        if (script.ScheduleMode == ScheduleMode.Interval)
        {
            // The first interval counts from when the schedule was first seen, so enabling a script or starting
            // the server does not set off every interval script at once.
            return lastRunUtc is { } last && nowUtc - last >= TimeSpan.FromMinutes(Math.Max(1, script.ScheduleIntervalMinutes));
        }

        if (!ScheduleTimes.TryParse(script.ScheduleTime, out var timeOfDay))
        {
            return false;
        }

        // Yesterday too, so a time just before midnight still runs when the tick lands just after it.
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, localZone);
        foreach (var date in new[] { local.Date, local.Date.AddDays(-1) })
        {
            if (!RunsOn(script, date))
            {
                continue;
            }

            var dueUtc = DueUtc(date + timeOfDay, localZone);
            if (nowUtc >= dueUtc && nowUtc - dueUtc <= Grace)
            {
                runUtc = dueUtc;
                return lastRunUtc is not { } previous || previous < runUtc;
            }
        }

        return false;
    }

    // A time the clocks skip when daylight saving starts runs at the first minute that exists after it, as cron
    // does. A time that happens twice when it ends runs once, at the second occurrence.
    private static DateTime DueUtc(DateTime localTime, TimeZoneInfo localZone)
    {
        var due = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        for (var guard = 0; guard < 24 * 60 && localZone.IsInvalidTime(due); guard++)
        {
            due = due.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(due, localZone);
    }

    private static bool RunsOn(ScriptDefinition script, DateTime date)
    {
        if (script.ScheduleMode == ScheduleMode.Monthly)
        {
            var day = Math.Min(Math.Clamp(script.ScheduleMonthDay, 1, 31), DateTime.DaysInMonth(date.Year, date.Month));
            return date.Day == day;
        }

        return script.ScheduleDays.Count == 0 || script.ScheduleDays.Contains(date.DayOfWeek);
    }
}
