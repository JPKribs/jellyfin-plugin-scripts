namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// What a script runs once for when its event carries no item or user of its own, such as a schedule. The names
/// are stored in configuration, so never rename one.
/// </summary>
public enum ForEachMode
{
    /// <summary>The script runs once per event.</summary>
    None,

    /// <summary>The script runs once per library item that passes its item kinds and conditions.</summary>
    Items,

    /// <summary>The script runs once per user that passes its users filter and conditions.</summary>
    Users
}
