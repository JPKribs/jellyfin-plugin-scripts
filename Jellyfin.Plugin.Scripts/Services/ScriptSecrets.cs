using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Scripts.Configuration;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Encrypts script secrets on save. The dashboard posts back <see cref="SecretProtector.KeptSentinel"/> for a
/// secret it left untouched, so the real value never has to reach a browser.
/// </summary>
public static class ScriptSecrets
{
    /// <summary>
    /// Encrypts every incoming secret, keeping the stored value where the dashboard sent the sentinel.
    /// </summary>
    /// <param name="incoming">The configuration about to be saved.</param>
    /// <param name="stored">The configuration currently in effect.</param>
    /// <param name="protector">The protector.</param>
    public static void Protect(PluginConfiguration incoming, PluginConfiguration stored, SecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(protector);

        foreach (var script in incoming.Scripts)
        {
            Protect(script, stored.Scripts.FirstOrDefault(s => s.Id == script.Id), protector);
        }
    }

    /// <summary>
    /// Encrypts one script's secrets, keeping stored values where the dashboard sent the sentinel. Used on save
    /// and for test runs of unsaved edits.
    /// </summary>
    /// <param name="script">The incoming script.</param>
    /// <param name="stored">The saved script with the same ID, if any.</param>
    /// <param name="protector">The protector.</param>
    public static void Protect(ScriptDefinition script, ScriptDefinition? stored, SecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(protector);

        var previous = stored?.Secrets ?? new List<ScriptSecret>();
        foreach (var secret in script.Secrets)
        {
            var match = previous.FirstOrDefault(p => string.Equals(p.Name, secret.Name, StringComparison.Ordinal));
            secret.Value = protector.ResolveIncoming(secret.Value, match?.Value);
        }
    }

    /// <summary>
    /// Tells the dashboard which secrets hold a value without sending the values: each becomes the sentinel or
    /// an empty string.
    /// </summary>
    /// <param name="script">The script, changed in place.</param>
    public static void Redact(ScriptDefinition script)
    {
        ArgumentNullException.ThrowIfNull(script);
        foreach (var secret in script.Secrets)
        {
            secret.Value = SecretProtector.HasSecret(secret.Value) ? SecretProtector.KeptSentinel : string.Empty;
        }
    }
}
