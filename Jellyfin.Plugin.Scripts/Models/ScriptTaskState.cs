using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The scripts that have been given a Jellyfin scheduled task, so the files the server keeps for a task can be
/// removed once its script is deleted or no longer scheduled.
/// </summary>
public class ScriptTaskState
{
    /// <summary>Gets or sets the IDs of the scripts with a scheduled task.</summary>
    public List<Guid> ScriptIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the IDs of scripts whose task is no longer wanted but still exists until the server restarts.
    /// Such a task can still be run through the API, which writes its result file again, so its files are
    /// removed once more at the next start.
    /// </summary>
    public List<Guid> RetiredIds { get; set; } = new();
}
