using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class ScheduleCalculatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static ScriptDefinition Daily(string time, params DayOfWeek[] days)
        => new() { ScheduleMode = ScheduleMode.Daily, ScheduleTime = time, ScheduleDays = new List<DayOfWeek>(days) };

    private static bool Due(ScriptDefinition script, DateTime now, DateTime? last = null)
        => ScheduleCalculator.IsDue(script, now, Utc, last, out _);

    [Fact]
    public void DailyRunsInTheWindowAfterItsTime()
    {
        var script = Daily("03:00");
        Assert.False(Due(script, new DateTime(2026, 3, 4, 2, 59, 0, DateTimeKind.Utc)));
        Assert.True(Due(script, new DateTime(2026, 3, 4, 3, 0, 30, DateTimeKind.Utc)));
        Assert.True(Due(script, new DateTime(2026, 3, 4, 3, 2, 0, DateTimeKind.Utc)));
        Assert.False(Due(script, new DateTime(2026, 3, 4, 3, 5, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void DailyRunsOnceEvenAcrossARestart()
    {
        var script = Daily("03:00");
        var now = new DateTime(2026, 3, 4, 3, 0, 10, DateTimeKind.Utc);
        Assert.True(ScheduleCalculator.IsDue(script, now, Utc, null, out var run));
        Assert.False(Due(script, now.AddSeconds(30), run));
        Assert.True(Due(script, now.AddDays(1), run));
    }

    [Fact]
    public void DaysOfTheWeekLimitADailySchedule()
    {
        var script = Daily("08:30", DayOfWeek.Saturday, DayOfWeek.Sunday);
        Assert.False(Due(script, new DateTime(2026, 3, 6, 8, 30, 0, DateTimeKind.Utc))); // Friday
        Assert.True(Due(script, new DateTime(2026, 3, 7, 8, 30, 0, DateTimeKind.Utc))); // Saturday
    }

    [Fact]
    public void MonthlyFallsBackToTheLastDayOfShortMonths()
    {
        var script = new ScriptDefinition { ScheduleMode = ScheduleMode.Monthly, ScheduleMonthDay = 31, ScheduleTime = "00:00" };
        Assert.True(Due(script, new DateTime(2026, 2, 28, 0, 1, 0, DateTimeKind.Utc)));
        Assert.False(Due(script, new DateTime(2026, 3, 30, 0, 1, 0, DateTimeKind.Utc)));
        Assert.True(Due(script, new DateTime(2026, 3, 31, 0, 1, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void IntervalWaitsTheWholeIntervalFromTheLastRun()
    {
        var script = new ScriptDefinition { ScheduleMode = ScheduleMode.Interval, ScheduleIntervalMinutes = 15 };
        var last = new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc);
        Assert.False(Due(script, last.AddMinutes(14), last));
        Assert.True(Due(script, last.AddMinutes(15), last));
        Assert.False(Due(script, last.AddMinutes(30), null));
    }

    [Fact]
    public void UsesTheServersTimeZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus-two", "plus-two");
        var script = Daily("03:00");
        Assert.True(ScheduleCalculator.IsDue(script, new DateTime(2026, 3, 4, 1, 0, 30, DateTimeKind.Utc), zone, null, out _));
    }

    [Fact]
    public void ATimeJustBeforeMidnightStillRunsAfterIt()
    {
        var script = Daily("23:59", DayOfWeek.Wednesday);
        Assert.True(Due(script, new DateTime(2026, 3, 5, 0, 0, 30, DateTimeKind.Utc))); // Thursday, just after
    }

    [Fact]
    public void ATimeSkippedByDaylightSavingRunsWhenTheClockJumps()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var script = Daily("02:30");

        // 8 March 2026: 02:00 EST jumps to 03:00 EDT, which is 07:00 UTC. 02:30 never happens.
        Assert.True(ScheduleCalculator.IsDue(script, new DateTime(2026, 3, 8, 7, 0, 30, DateTimeKind.Utc), zone, null, out var run));
        Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc), run);
    }

    [Theory]
    [InlineData("03:00", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("3:00", false)]
    [InlineData("", false)]
    public void ParsesTwentyFourHourTimes(string text, bool valid)
    {
        Assert.Equal(valid, ScheduleTimes.TryParse(text, out _));
    }
}
