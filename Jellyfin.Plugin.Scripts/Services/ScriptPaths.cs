using System;
using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Where the plugin keeps its run history and other state files: beside the plugin configuration, outside the
/// plugin's install folder, which Jellyfin replaces on every update.
/// </summary>
public class ScriptPaths
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptPaths"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    public ScriptPaths(IApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        HistoryFile = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Scripts.History.json");
        ScheduleFile = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Scripts.Schedule.json");
        TasksFile = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Scripts.Tasks.json");
        DevicesFile = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Scripts.Devices.json");
    }

    /// <summary>Gets the file that records when each scheduled script last ran.</summary>
    public string ScheduleFile { get; }

    /// <summary>Gets the run history file.</summary>
    public string HistoryFile { get; }

    /// <summary>Gets the file that remembers which devices the server has seen.</summary>
    public string DevicesFile { get; }

    /// <summary>Gets the file that remembers which scripts have a scheduled task.</summary>
    public string TasksFile { get; }
}
