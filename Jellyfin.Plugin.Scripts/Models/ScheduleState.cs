using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// When each scheduled script last ran, kept beside the configuration so a restart neither repeats a run nor
/// resets an interval.
/// </summary>
public class ScheduleState
{
    /// <summary>Gets or sets the last run of each script, UTC, keyed by script ID.</summary>
    public Dictionary<Guid, DateTime> LastRuns { get; set; } = new();
}
