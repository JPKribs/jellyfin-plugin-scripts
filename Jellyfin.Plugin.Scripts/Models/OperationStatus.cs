namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The outcome of one step of a run.
/// </summary>
public enum OperationStatus
{
    /// <summary>The step did what it was asked.</summary>
    Succeeded,

    /// <summary>The step failed.</summary>
    Failed,

    /// <summary>The step did not run, because the event lacks what it needs or an earlier step failed.</summary>
    Skipped
}
