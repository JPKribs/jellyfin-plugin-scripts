using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// One queued run: a script and the event it runs for.
/// </summary>
/// <param name="Script">The script, as configured when the event arrived.</param>
/// <param name="Context">The event.</param>
public sealed record ScriptJob(ScriptDefinition Script, EventContext Context);
