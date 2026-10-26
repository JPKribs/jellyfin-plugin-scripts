using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// What a trigger supplies to a script.
/// </summary>
/// <param name="Trigger">The trigger.</param>
/// <param name="Label">The editor label.</param>
/// <param name="Category">The editor group.</param>
/// <param name="Description">One sentence on when it fires, for the editor.</param>
/// <param name="HasItem">Whether the event carries an item, so the item kind filter applies.</param>
/// <param name="HasSession">Whether the event carries a playback session, so session steps can act on it.</param>
/// <param name="Variables">The variables the event fills.</param>
public sealed record TriggerInfo(
    ScriptTrigger Trigger,
    string Label,
    string Category,
    string Description,
    bool HasItem,
    bool HasSession,
    IReadOnlyList<VariableInfo> Variables)
{
    /// <summary>Gets a value indicating whether the event carries a user, so the users filter applies.</summary>
    public bool HasUser => Variables.Any(v => v.Name == "USER_ID");
}
