using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// What every step of one run shares.
/// </summary>
/// <param name="Script">The script being run.</param>
/// <param name="Context">The event.</param>
/// <param name="Variables">The variables for placeholders, without the <c>JF_</c> prefix.</param>
/// <param name="Environment">The environment variables a command receives, with the prefix.</param>
/// <param name="DefaultTimeoutSeconds">The timeout for a command that sets none.</param>
public sealed record RunState(
    ScriptDefinition Script,
    EventContext Context,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyDictionary<string, string> Environment,
    int DefaultTimeoutSeconds);
