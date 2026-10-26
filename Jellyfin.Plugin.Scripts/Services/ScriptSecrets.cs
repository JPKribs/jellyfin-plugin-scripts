using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Scripts.Configuration;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Keeps script secrets out of the plugin configuration. On save each value is encrypted into the
/// <see cref="SecretVault"/> and the configuration keeps only <see cref="SecretProtector.KeptSentinel"/>, so the
/// configuration the dashboard (or any API client) reads never carries a value, encrypted or not.
/// </summary>
public static class ScriptSecrets
{
    /// <summary>Secrets shorter than this are not masked in run output, since masking would mangle ordinary text.</summary>
    public const int MinMaskedLength = 3;

    /// <summary>
    /// Moves every incoming secret value into a new vault state, leaving the sentinel (or an empty string for a
    /// secret without a value) in the configuration. Secrets of deleted scripts are dropped.
    /// </summary>
    /// <param name="incoming">The configuration about to be saved, changed in place.</param>
    /// <param name="stored">The configuration currently in effect.</param>
    /// <param name="vault">The vault as it stands.</param>
    /// <param name="protector">The protector.</param>
    /// <returns>The vault to save.</returns>
    public static SecretVaultState Store(PluginConfiguration incoming, PluginConfiguration stored, SecretVaultState vault, SecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(protector);

        var next = new SecretVaultState();
        foreach (var script in incoming.Scripts)
        {
            var storedScript = stored.Scripts.FirstOrDefault(s => s.Id == script.Id);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var secret in script.Secrets)
            {
                var resolved = protector.ResolveIncoming(secret.Value, Current(script.Id, LookupName(secret), storedScript, vault));
                secret.PreviousName = string.Empty;
                if (resolved.Length > 0)
                {
                    values[secret.Name] = resolved;
                    secret.Value = SecretProtector.KeptSentinel;
                }
                else
                {
                    secret.Value = string.Empty;
                }
            }

            if (values.Count > 0)
            {
                next.Values[script.Id] = values;
            }
        }

        return next;
    }

    /// <summary>
    /// Resolves an unsaved script's secrets for a test run: each value becomes the protected value to use, taken
    /// from the vault where the dashboard sent the sentinel.
    /// </summary>
    /// <param name="script">The incoming script, changed in place.</param>
    /// <param name="stored">The saved script with the same ID, if any.</param>
    /// <param name="vault">The vault.</param>
    /// <param name="protector">The protector.</param>
    public static void Resolve(ScriptDefinition script, ScriptDefinition? stored, SecretVaultState vault, SecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(protector);

        foreach (var secret in script.Secrets)
        {
            secret.Value = protector.ResolveIncoming(secret.Value, Current(script.Id, LookupName(secret), stored, vault));
            secret.PreviousName = string.Empty;
        }
    }

    /// <summary>
    /// Decrypts a script's secrets for one run.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="vault">The vault.</param>
    /// <param name="protector">The protector.</param>
    /// <returns>Each named secret's plain value.</returns>
    public static Dictionary<string, string> Reveal(ScriptDefinition script, SecretVaultState vault, SecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(protector);

        var revealed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var secret in script.Secrets.Where(s => s.Name.Length > 0))
        {
            // A saved script holds the sentinel and its value lives in the vault. A test run's script, and a
            // configuration saved before the vault existed, hold the protected value itself.
            var value = string.Equals(secret.Value, SecretProtector.KeptSentinel, StringComparison.Ordinal)
                ? Vaulted(vault, script.Id, secret.Name)
                : secret.Value;
            revealed[secret.Name] = protector.Unprotect(value);
        }

        return revealed;
    }

    /// <summary>
    /// Replaces each secret value found in a text with its placeholder name, so step messages and command output
    /// recorded in the history and the activity log never carry one.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="secrets">The run's plain secret values, by name.</param>
    /// <returns>The masked text.</returns>
    public static string Mask(string? text, IReadOnlyDictionary<string, string> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        // Longest first, so a secret that contains another is masked whole.
        foreach (var (name, value) in secrets.Where(s => s.Value.Length >= MinMaskedLength).OrderByDescending(s => s.Value.Length))
        {
            text = text.Replace(value, "[" + TriggerCatalog.SecretPrefix + name + "]", StringComparison.Ordinal);
        }

        return text;
    }

    private static string LookupName(ScriptSecret secret)
        => string.IsNullOrEmpty(secret.PreviousName) ? secret.Name : secret.PreviousName;

    // The protected value a secret holds now: from the vault, or from a configuration saved before the vault
    // existed, which kept the encrypted value in place.
    private static string? Current(Guid scriptId, string name, ScriptDefinition? stored, SecretVaultState vault)
    {
        var vaulted = Vaulted(vault, scriptId, name);
        if (vaulted is not null)
        {
            return vaulted;
        }

        var legacy = stored?.Secrets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal))?.Value;
        return string.IsNullOrEmpty(legacy) || string.Equals(legacy, SecretProtector.KeptSentinel, StringComparison.Ordinal) ? null : legacy;
    }

    private static string? Vaulted(SecretVaultState vault, Guid scriptId, string name)
        => vault.Values.TryGetValue(scriptId, out var values) && values.TryGetValue(name, out var value) ? value : null;
}
