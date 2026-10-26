namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// How a script combines its conditions.
/// </summary>
public enum ConditionMatch
{
    /// <summary>Every condition must match.</summary>
    All,

    /// <summary>At least one condition must match.</summary>
    Any
}
