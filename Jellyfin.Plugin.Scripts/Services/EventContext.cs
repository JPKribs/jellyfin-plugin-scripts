using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Everything one event supplies to the scripts it runs: the variables, plus the typed values the filters
/// and steps act on.
/// </summary>
public sealed class EventContext
{
    private readonly Dictionary<ConditionField, List<string>> _candidates = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="EventContext"/> class.
    /// </summary>
    /// <param name="trigger">The event's trigger.</param>
    public EventContext(ScriptTrigger trigger)
    {
        Trigger = trigger;
    }

    /// <summary>Gets the event's trigger.</summary>
    public ScriptTrigger Trigger { get; }

    /// <summary>Gets the variables without the <c>JF_</c> prefix. A value the event lacks is absent.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the kind of the event's item, when it has one.</summary>
    public BaseItemKind? ItemKind { get; set; }

    /// <summary>Gets or sets the event's item ID, when it has one.</summary>
    public Guid? ItemId { get; set; }

    /// <summary>Gets or sets the event's playback session ID, when it has one.</summary>
    public string? SessionId { get; set; }

    /// <summary>Gets or sets a one line description of the event for the history.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets a variable, or an empty string when the event lacks it.
    /// </summary>
    /// <param name="name">The variable name without the prefix.</param>
    /// <returns>The value.</returns>
    public string Get(string name) => Variables.TryGetValue(name, out var value) ? value : string.Empty;

    /// <summary>
    /// Sets a variable. An empty value is left out.
    /// </summary>
    /// <param name="name">The variable name without the prefix.</param>
    /// <param name="value">The value.</param>
    public void Set(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Variables.Remove(name);
        }
        else
        {
            Variables[name] = value;
        }
    }

    /// <summary>
    /// Adds a value a condition on <paramref name="field"/> may match beyond the field's own variable, such as
    /// a user's name beside their ID, or every library an item sits in.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public void AddCandidate(ConditionField field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (!_candidates.TryGetValue(field, out var list))
        {
            list = new List<string>();
            _candidates[field] = list;
        }

        list.Add(value);
    }

    /// <summary>
    /// Gets the values a condition on <paramref name="field"/> compares against.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The candidate values, empty when the event lacks the field.</returns>
    public IReadOnlyList<string> CandidatesFor(ConditionField field)
    {
        if (_candidates.TryGetValue(field, out var list))
        {
            return list;
        }

        var value = Get(TriggerCatalog.VariableFor(field));
        return value.Length == 0 ? Array.Empty<string>() : new[] { value };
    }
}
