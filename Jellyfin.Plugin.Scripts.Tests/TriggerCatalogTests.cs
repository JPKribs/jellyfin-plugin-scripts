using System;
using System.Linq;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class TriggerCatalogTests
{
    [Fact]
    public void EveryTriggerIsDescribed()
    {
        Assert.Equal(Enum.GetValues<ScriptTrigger>().Length, TriggerCatalog.All.Count);
    }

    [Fact]
    public void EveryFieldAppliesToSomeTrigger()
    {
        foreach (var field in Enum.GetValues<ConditionField>())
        {
            Assert.Contains(TriggerCatalog.All, t => TriggerCatalog.Supplies(t.Trigger, field));
        }
    }

    [Fact]
    public void VariablesAreUniquePerTrigger()
    {
        foreach (var trigger in TriggerCatalog.All)
        {
            Assert.Equal(trigger.Variables.Count, trigger.Variables.Select(v => v.Name).Distinct().Count());
        }
    }

    [Fact]
    public void EveryTriggerHasALabelCategoryAndDescription()
    {
        foreach (var trigger in TriggerCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(trigger.Label));
            Assert.False(string.IsNullOrWhiteSpace(trigger.Category));
            Assert.False(string.IsNullOrWhiteSpace(trigger.Description));
        }
    }

    [Fact]
    public void EveryFieldHasAVariable()
    {
        foreach (var field in Enum.GetValues<ConditionField>())
        {
            Assert.False(string.IsNullOrEmpty(TriggerCatalog.VariableFor(field)));
        }
    }

    [Fact]
    public void PlaybackTriggersCountStreams()
    {
        Assert.True(TriggerCatalog.Supplies(ScriptTrigger.PlaybackStart, ConditionField.ActiveTranscodes));
        Assert.False(TriggerCatalog.Supplies(ScriptTrigger.ItemAdded, ConditionField.ActiveTranscodes));
    }

    [Fact]
    public void OnlyPlaybackStopSuppliesPlayedToCompletion()
    {
        var supplying = TriggerCatalog.All.Where(t => TriggerCatalog.Supplies(t.Trigger, ConditionField.PlayedToCompletion)).Select(t => t.Trigger);
        Assert.Equal(new[] { ScriptTrigger.PlaybackStop }, supplying);
    }
}
