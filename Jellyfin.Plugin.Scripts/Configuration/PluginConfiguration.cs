using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Scripts.Configuration;

/// <summary>
/// Single configuration object for the plugin. XML-serialized by Jellyfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the configured scripts.</summary>
    public List<ScriptDefinition> Scripts { get; set; } = new();

    /// <summary>Gets or sets how many runs may execute at once. Further runs wait their turn.</summary>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>Gets or sets the timeout for a command that sets none, in seconds.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 60;

    /// <summary>Gets or sets how many runs the history keeps.</summary>
    public int HistorySize { get; set; } = 200;
}
