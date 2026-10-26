using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Configuration;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts;

/// <summary>
/// Main plugin entry point for Scripts.
/// </summary>
public class Plugin : PluginBase<Plugin, PluginConfiguration>
{
    /// <summary>
    /// The name the secret keys are derived from: the key folder, the Data Protection application name, and the
    /// purpose. Changing it makes every stored secret unreadable.
    /// </summary>
    public const string SecretsName = "Jellyfin.Plugin.Scripts";

    private readonly Lazy<SecretProtector> _secrets;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="xmlSerializer">The XML serializer.</param>
    /// <param name="logger">The logger.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _secrets = new Lazy<SecretProtector>(() => StableSecretProtection.CreateProtector(applicationPaths, SecretsName, logger));
        logger.LogInformation("Scripts plugin initialized");
    }

    /// <inheritdoc />
    public override string Name => "Scripts";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("449817c2-7661-46fb-b302-bbd6dd3b0a01");

    /// <inheritdoc />
    public override string Description => "Run your own scripts when server events happen.";

    /// <summary>
    /// Normalizes and validates incoming configuration before persisting it. The dashboard enforces the same
    /// rules in the browser, but configuration can arrive from any API client.
    /// </summary>
    /// <param name="incoming">The incoming configuration.</param>
    /// <param name="stored">The configuration currently in effect.</param>
    protected override void OnConfigurationSaving(PluginConfiguration incoming, PluginConfiguration stored)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        ConfigurationValidator.Normalize(incoming);
        ConfigurationValidator.Validate(incoming);
        ScriptSecrets.Protect(incoming, stored, _secrets.Value);
    }

    /// <inheritdoc />
    public override IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = typeof(Plugin).Namespace;

        // Tab 1: Scripts (the dashboard menu entry).
        yield return new PluginPageInfo
        {
            Name = "scripts_scripts",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_scripts.html",
            MenuSection = "server",
            DisplayName = "Scripts",
            EnableInMainMenu = false
        };

        yield return new PluginPageInfo
        {
            Name = "scripts_scripts.js",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_scripts.js"
        };

        // Tab 2: History.
        yield return new PluginPageInfo
        {
            Name = "scripts_history",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_history.html"
        };

        yield return new PluginPageInfo
        {
            Name = "scripts_history.js",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_history.js"
        };

        // Tab 3: Settings.
        yield return new PluginPageInfo
        {
            Name = "scripts_settings",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_settings.html"
        };

        yield return new PluginPageInfo
        {
            Name = "scripts_settings.js",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_settings.js"
        };

        yield return new PluginPageInfo
        {
            Name = "scripts_common.js",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_common.js"
        };

        yield return new PluginPageInfo
        {
            Name = "scripts_templates.js",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_templates.js"
        };

        yield return new PluginPageInfo
        {
            Name = "scripts_shared.css",
            EmbeddedResourcePath = $"{ns}.Configuration.scripts_shared.css"
        };

        // Shared base CSS and JS compiled in from the JPKribs.Jellyfin.Base package.
        foreach (var page in GetSharedPages("scripts"))
        {
            yield return page;
        }
    }
}
