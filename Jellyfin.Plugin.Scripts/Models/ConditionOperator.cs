namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// How a condition compares an event value against its values. Text comparisons ignore case.
/// </summary>
public enum ConditionOperator
{
    /// <summary>The event value equals one of the values.</summary>
    Is,

    /// <summary>The event value equals none of the values.</summary>
    IsNot,

    /// <summary>The event value contains one of the values.</summary>
    Contains,

    /// <summary>The event value contains none of the values.</summary>
    DoesNotContain,

    /// <summary>The event value starts with one of the values.</summary>
    StartsWith,

    /// <summary>The event value matches one of the values as a regular expression.</summary>
    MatchesRegex
}
