using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public sealed class ScriptDispatcherTests : IDisposable
{
    private readonly TestServices _services = new();

    private ScriptDispatcher Dispatcher => new(_services.Runner);

    private static ScriptDefinition Script(int cooldownSeconds = 0)
        => new() { Id = Guid.NewGuid(), Name = "Dispatch test", Triggers = new List<ScriptTrigger> { ScriptTrigger.PlaybackStart }, CooldownSeconds = cooldownSeconds };

    private static EventContext Event(string user = "u1", string item = "i1", ScriptTrigger trigger = ScriptTrigger.PlaybackStart)
    {
        var context = new EventContext(trigger);
        context.Set("USER_ID", user);
        context.Set("ITEM_ID", item);
        context.AddCandidate(ConditionField.User, user);
        return context;
    }

    [Fact]
    public void ACooldownHoldsOffTheSameUserAndItem()
    {
        var dispatcher = Dispatcher;
        var script = Script(cooldownSeconds: 600);

        Assert.True(dispatcher.DispatchTo(Event(), script));
        Assert.False(dispatcher.DispatchTo(Event(), script));
        Assert.True(dispatcher.DispatchTo(Event(item: "i2"), script));
        Assert.True(dispatcher.DispatchTo(Event(user: "u2"), script));

        // Per trigger, so a held off start never swallows the stop that follows.
        Assert.True(dispatcher.DispatchTo(Event(trigger: ScriptTrigger.PlaybackStop), script));
    }

    [Fact]
    public void WithoutACooldownEveryEventRuns()
    {
        var dispatcher = Dispatcher;
        var script = Script();

        Assert.True(dispatcher.DispatchTo(Event(), script));
        Assert.True(dispatcher.DispatchTo(Event(), script));
    }

    [Fact]
    public void FailingConditionsQueueNothing()
    {
        var script = Script(cooldownSeconds: 600);
        script.Conditions.Add(new ScriptCondition { Field = ConditionField.User, Operator = ConditionOperator.Is, Values = new List<string> { "someone else" } });
        var dispatcher = Dispatcher;

        Assert.False(dispatcher.DispatchTo(Event(), script));

        // Nothing ran, so nothing claimed the cooldown either.
        script.Conditions.Clear();
        Assert.True(dispatcher.DispatchTo(Event(), script));
    }

    [Fact]
    public async Task ARunDroppedByAFullQueueDoesNotHoldTheCooldown()
    {
        var dispatcher = Dispatcher;
        var filler = Script();
        while (_services.Runner.TryEnqueue(new ScriptJob(filler, Event("filler"))))
        {
        }

        var script = Script(cooldownSeconds: 600);
        Assert.False(dispatcher.DispatchTo(Event(), script));

        // Once the queue drains, the same user and item run at once.
        await _services.Runner.StartAsync(CancellationToken.None);
        try
        {
            var watch = Stopwatch.StartNew();
            var queued = false;
            while (!queued && watch.Elapsed < TimeSpan.FromSeconds(30))
            {
                queued = dispatcher.DispatchTo(Event(), script);
                if (!queued)
                {
                    await Task.Delay(50);
                }
            }

            Assert.True(queued);
        }
        finally
        {
            await _services.Runner.StopAsync(CancellationToken.None);
        }
    }

    public void Dispose() => _services.Dispose();
}
