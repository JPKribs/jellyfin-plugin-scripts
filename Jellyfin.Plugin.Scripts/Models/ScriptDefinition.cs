using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// A configured script: the events it runs on, the filters that narrow them, and the steps it performs.
/// </summary>
public class ScriptDefinition
{
    /// <summary>Gets or sets the stable ID, assigned on first save.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the script runs on events.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the events the script runs on.</summary>
    public List<ScriptTrigger> Triggers { get; set; } = new();

    /// <summary>
    /// Gets or sets the item kinds the script runs for. Empty runs for every kind. Only events that carry an
    /// item are filtered; sign ins and scheduled tasks ignore it.
    /// </summary>
    public List<BaseItemKind> ItemKinds { get; set; } = new();

    /// <summary>
    /// Gets or sets the users the script runs for. Empty runs for everyone. Only events that carry a user are
    /// filtered (playback and sign ins); library events and scheduled tasks ignore it. A playback event
    /// matches when any of the session's users is listed.
    /// </summary>
    public List<Guid> UserIds { get; set; } = new();

    /// <summary>Gets or sets how the conditions combine.</summary>
    public ConditionMatch Match { get; set; } = ConditionMatch.All;

    /// <summary>Gets or sets the conditions. Empty always matches.</summary>
    public List<ScriptCondition> Conditions { get; set; } = new();

    /// <summary>Gets or sets the steps, run in order.</summary>
    public List<ScriptOperation> Operations { get; set; } = new();

    /// <summary>
    /// Gets or sets the seconds after a run during which the same script does not run again for the same trigger,
    /// user, and item. Zero disables the cooldown.
    /// </summary>
    public int CooldownSeconds { get; set; }
}
