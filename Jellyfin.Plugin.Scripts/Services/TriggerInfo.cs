using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// What a trigger supplies to a script.
/// </summary>
/// <param name="Trigger">The trigger.</param>
/// <param name="LabelKey">The translation key of the editor label.</param>
/// <param name="CategoryKey">The translation key of the editor group.</param>
/// <param name="DescriptionKey">The translation key of one sentence on when it fires, for the editor.</param>
/// <param name="HasItem">Whether the event carries an item, so the item kind filter applies.</param>
/// <param name="HasSession">Whether the event carries a playback session, so session steps can act on it.</param>
/// <param name="Variables">The variables the event fills.</param>
public sealed record TriggerInfo(
    ScriptTrigger Trigger,
    string LabelKey,
    string CategoryKey,
    string DescriptionKey,
    bool HasItem,
    bool HasSession,
    IReadOnlyList<VariableInfo> Variables)
{
    /// <summary>Gets the editor label in US English, as a run's summary shows it.</summary>
    public string Label => BaseTranslations.Get(LabelKey);

    /// <summary>Gets the editor group in US English.</summary>
    public string Category => BaseTranslations.Get(CategoryKey);

    /// <summary>Gets when the trigger fires, in US English.</summary>
    public string Description => BaseTranslations.Get(DescriptionKey);

    /// <summary>Gets a value indicating whether the event carries a user, so the users filter applies. Worked out once, since every dispatch asks.</summary>
    public bool HasUser { get; } = Variables.Any(v => v.Name == "USER_ID");

    /// <summary>Gets a value indicating whether a script can choose this trigger. Run now is never chosen.</summary>
    public bool Selectable { get; init; } = true;
}
