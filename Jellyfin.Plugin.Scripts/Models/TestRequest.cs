namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// A test run request.
/// </summary>
public class TestRequest
{
    /// <summary>Gets or sets the script to run.</summary>
    public ScriptDefinition? Script { get; set; }

    /// <summary>Gets or sets the trigger whose sample values the run receives.</summary>
    public ScriptTrigger Trigger { get; set; }
}
