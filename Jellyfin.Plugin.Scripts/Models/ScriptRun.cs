using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The record of one run of a script, kept in the history.
/// </summary>
public class ScriptRun
{
    /// <summary>Gets or sets the run's ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the script's ID.</summary>
    public Guid ScriptId { get; set; }

    /// <summary>Gets or sets the script's name when it ran.</summary>
    public string ScriptName { get; set; } = string.Empty;

    /// <summary>Gets or sets the event that ran it.</summary>
    public ScriptTrigger Trigger { get; set; }

    /// <summary>Gets or sets a value indicating whether the run came from the test button.</summary>
    public bool IsTest { get; set; }

    /// <summary>Gets or sets when the run started.</summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>Gets or sets how long the run took, in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Gets or sets a value indicating whether every step succeeded or was skipped.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Gets or sets a one line description of the event, such as the user and item.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Gets or sets the variables the run received, without the <c>JF_</c> prefix.</summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>Gets or sets each step's outcome, in order.</summary>
    public List<OperationResult> Operations { get; set; } = new();
}
