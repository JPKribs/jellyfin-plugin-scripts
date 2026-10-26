using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Configuration;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class ConfigurationValidatorTests
{
    private static PluginConfiguration With(ScriptDefinition script) => new() { Scripts = new List<ScriptDefinition> { script } };

    private static ScriptDefinition Valid() => new()
    {
        Name = "Notify",
        Triggers = new List<ScriptTrigger> { ScriptTrigger.PlaybackStart },
        Operations = new List<ScriptOperation> { new() { Type = OperationType.RunCommand, Command = "/scripts/notify.sh" } }
    };

    [Fact]
    public void AcceptsAValidScript()
    {
        var config = With(Valid());
        ConfigurationValidator.Normalize(config);
        ConfigurationValidator.Validate(config);
    }

    [Fact]
    public void NormalizeAssignsIdsAndTidies()
    {
        var script = Valid();
        script.Name = "  Notify  ";
        script.Triggers.Add(ScriptTrigger.PlaybackStart);
        script.Conditions.Add(new ScriptCondition { Values = new List<string> { " a ", "", "A" } });

        ConfigurationValidator.Normalize(script);

        Assert.NotEqual(Guid.Empty, script.Id);
        Assert.Equal("Notify", script.Name);
        Assert.Single(script.Triggers);
        Assert.Equal(new[] { "a" }, script.Conditions[0].Values);
    }

    [Fact]
    public void RejectsAMissingName()
    {
        var script = Valid();
        script.Name = string.Empty;
        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(script));
    }

    [Fact]
    public void RejectsAnInvalidRegex()
    {
        var script = Valid();
        script.Conditions.Add(new ScriptCondition { Field = ConditionField.ItemName, Operator = ConditionOperator.MatchesRegex, Values = new List<string> { "(" } });
        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(script));
    }

    [Theory]
    [InlineData(OperationType.RunCommand)]
    [InlineData(OperationType.HttpRequest)]
    [InlineData(OperationType.SendMessage)]
    [InlineData(OperationType.RunScheduledTask)]
    [InlineData(OperationType.Wait)]
    public void RejectsAStepMissingWhatItNeeds(OperationType type)
    {
        var script = Valid();
        script.Operations = new List<ScriptOperation> { new() { Type = type } };
        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(script));
    }

    [Fact]
    public void RejectsANonHttpUrl()
    {
        var script = Valid();
        script.Operations = new List<ScriptOperation> { new() { Type = OperationType.HttpRequest, Url = "file:///etc/passwd" } };
        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(script));
    }

    [Fact]
    public void RejectsOutOfRangeSettings()
    {
        var config = new PluginConfiguration { MaxConcurrentRuns = 0 };
        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(config));
    }

    [Fact]
    public void RejectsDuplicateIds()
    {
        var a = Valid();
        a.Id = Guid.NewGuid();
        var b = Valid();
        b.Id = a.Id;
        var config = new PluginConfiguration { Scripts = new List<ScriptDefinition> { a, b } };
        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(config));
    }
}
