using System;
using System.IO;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Reads and writes the encrypted secret values beside the plugin configuration. Both the plugin (on save) and
/// the runner (on each run) open the same file, and every read goes to disk, so they never disagree.
/// </summary>
public class SecretVault
{
    private readonly JsonFileStore<SecretVaultState> _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretVault"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public SecretVault(IApplicationPaths applicationPaths, ILogger<SecretVault> logger)
        : this(FileIn(applicationPaths), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretVault"/> class at a given file.
    /// </summary>
    /// <param name="path">The vault file.</param>
    /// <param name="logger">The logger.</param>
    public SecretVault(string path, ILogger logger)
    {
        _store = new JsonFileStore<SecretVaultState>(path, logger);
    }

    /// <summary>
    /// Gets the vault file for a server.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <returns>The path.</returns>
    public static string FileIn(IApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        return Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Scripts.Secrets.json");
    }

    /// <summary>
    /// Reads the vault.
    /// </summary>
    /// <returns>The stored values.</returns>
    public virtual SecretVaultState Load() => _store.Load();

    /// <summary>
    /// Replaces the vault.
    /// </summary>
    /// <param name="state">The values to keep.</param>
    public virtual void Save(SecretVaultState state) => _store.Save(state);
}
