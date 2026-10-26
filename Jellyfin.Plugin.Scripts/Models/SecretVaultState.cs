using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The encrypted secret values, by script and secret name, kept in their own file so the plugin configuration
/// the dashboard reads never holds them.
/// </summary>
public class SecretVaultState
{
    /// <summary>Gets or sets each script's encrypted values, by secret name.</summary>
    public Dictionary<Guid, Dictionary<string, string>> Values { get; set; } = new();
}
