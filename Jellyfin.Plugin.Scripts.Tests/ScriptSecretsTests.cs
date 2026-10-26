using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class ScriptSecretsTests
{
    // Without a Data Protection provider the protector stores plaintext, which is enough to test which value
    // each incoming secret resolves to.
    private static readonly SecretProtector Protector = new("test", NullLogger.Instance);

    private static ScriptDefinition With(params (string Name, string Value)[] secrets)
    {
        var script = new ScriptDefinition();
        foreach (var (name, value) in secrets)
        {
            script.Secrets.Add(new ScriptSecret { Name = name, Value = value });
        }

        return script;
    }

    [Fact]
    public void KeptSecretsKeepTheStoredValue()
    {
        var incoming = With(("TOKEN", SecretProtector.KeptSentinel));
        ScriptSecrets.Protect(incoming, With(("TOKEN", "stored")), Protector);
        Assert.Equal("stored", incoming.Secrets[0].Value);
    }

    [Fact]
    public void NewValuesReplaceAndEmptyClears()
    {
        var incoming = With(("TOKEN", "new"), ("OTHER", string.Empty));
        ScriptSecrets.Protect(incoming, With(("TOKEN", "stored"), ("OTHER", "old")), Protector);
        Assert.Equal("new", incoming.Secrets[0].Value);
        Assert.Equal(string.Empty, incoming.Secrets[1].Value);
    }

    [Fact]
    public void RedactNeverExposesTheValue()
    {
        var script = With(("TOKEN", "stored"), ("EMPTY", string.Empty));
        ScriptSecrets.Redact(script);
        Assert.Equal(SecretProtector.KeptSentinel, script.Secrets[0].Value);
        Assert.Equal(string.Empty, script.Secrets[1].Value);
    }
}
