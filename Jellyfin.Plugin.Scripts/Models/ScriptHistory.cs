using System.Collections.Generic;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The stored run history, newest first.
/// </summary>
public class ScriptHistory
{
    /// <summary>Gets or sets the runs, newest first.</summary>
    public List<ScriptRun> Runs { get; set; } = new();
}
