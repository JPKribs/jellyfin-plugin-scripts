using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Configuration;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class ScriptSecretsTests
{
    // Without a Data Protection provider the protector stores plaintext, which is enough to test which value
    // each incoming secret resolves to and where it ends up.
    private static readonly SecretProtector Protector = new("test", NullLogger.Instance);

    private static readonly Guid ScriptId = Guid.NewGuid();

    private static ScriptDefinition With(params (string Name, string Value)[] secrets)
    {
        var script = new ScriptDefinition { Id = ScriptId, Name = "Test" };
        foreach (var (name, value) in secrets)
        {
            script.Secrets.Add(new ScriptSecret { Name = name, Value = value });
        }

        return script;
    }

    private static PluginConfiguration Config(ScriptDefinition script)
    {
        var config = new PluginConfiguration();
        config.Scripts.Add(script);
        return config;
    }

    private static SecretVaultState Vault(params (string Name, string Value)[] secrets)
    {
        var vault = new SecretVaultState();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in secrets)
        {
            values[name] = value;
        }

        vault.Values[ScriptId] = values;
        return vault;
    }

    [Fact]
    public void StoreMovesValuesIntoTheVault()
    {
        var incoming = Config(With(("TOKEN", "new"), ("EMPTY", string.Empty)));
        var vault = ScriptSecrets.Store(incoming, new PluginConfiguration(), new SecretVaultState(), Protector);

        Assert.Equal("new", vault.Values[ScriptId]["TOKEN"]);
        Assert.False(vault.Values[ScriptId].ContainsKey("EMPTY"));
        Assert.Equal(SecretProtector.KeptSentinel, incoming.Scripts[0].Secrets[0].Value);
        Assert.Equal(string.Empty, incoming.Scripts[0].Secrets[1].Value);
    }

    [Fact]
    public void KeptSecretsKeepTheVaultedValue()
    {
        var incoming = Config(With(("TOKEN", SecretProtector.KeptSentinel)));
        var vault = ScriptSecrets.Store(incoming, Config(With(("TOKEN", SecretProtector.KeptSentinel))), Vault(("TOKEN", "stored")), Protector);
        Assert.Equal("stored", vault.Values[ScriptId]["TOKEN"]);
    }

    [Fact]
    public void RenamedSecretsKeepTheirValue()
    {
        var script = With(("API_TOKEN", SecretProtector.KeptSentinel));
        script.Secrets[0].PreviousName = "TOKEN";
        var vault = ScriptSecrets.Store(Config(script), Config(With(("TOKEN", SecretProtector.KeptSentinel))), Vault(("TOKEN", "stored")), Protector);

        Assert.Equal("stored", vault.Values[ScriptId]["API_TOKEN"]);
        Assert.False(vault.Values[ScriptId].ContainsKey("TOKEN"));
        Assert.Equal(string.Empty, script.Secrets[0].PreviousName);
    }

    [Fact]
    public void ValuesKeptInTheOldConfigurationMoveToTheVault()
    {
        var legacy = Config(With(("TOKEN", "enc-value")));
        var vault = ScriptSecrets.Store(legacy, legacy, new SecretVaultState(), Protector);
        Assert.Equal("enc-value", vault.Values[ScriptId]["TOKEN"]);
        Assert.Equal(SecretProtector.KeptSentinel, legacy.Scripts[0].Secrets[0].Value);
    }

    [Fact]
    public void DeletedScriptsLeaveTheVault()
    {
        var vault = ScriptSecrets.Store(new PluginConfiguration(), Config(With(("TOKEN", SecretProtector.KeptSentinel))), Vault(("TOKEN", "stored")), Protector);
        Assert.Empty(vault.Values);
    }

    [Fact]
    public void RevealReadsTheVaultForKeptSecrets()
    {
        var revealed = ScriptSecrets.Reveal(With(("TOKEN", SecretProtector.KeptSentinel), ("TESTED", "inline")), Vault(("TOKEN", "stored")), Protector);
        Assert.Equal("stored", revealed["TOKEN"]);
        Assert.Equal("inline", revealed["TESTED"]);
    }

    [Fact]
    public void ResolveUsesTheVaultForATestRun()
    {
        var script = With(("TOKEN", SecretProtector.KeptSentinel), ("NEW", "typed"));
        ScriptSecrets.Resolve(script, null, Vault(("TOKEN", "stored")), Protector);
        Assert.Equal("stored", script.Secrets[0].Value);
        Assert.Equal("typed", script.Secrets[1].Value);
    }

    [Fact]
    public void MaskHidesSecretValues()
    {
        var secrets = new Dictionary<string, string> { ["TOKEN"] = "abc123", ["SHORT"] = "x" };
        Assert.Equal("POST https://h/[SECRET_TOKEN] x", ScriptSecrets.Mask("POST https://h/abc123 x", secrets));
    }
}
