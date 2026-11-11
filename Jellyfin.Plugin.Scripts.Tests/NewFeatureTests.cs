using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Configuration;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

// Step conditions, the stop step, saved outputs, webhook variables, running for each, and the new settings'
// validation.
public sealed class NewFeatureTests : IDisposable
{
    private readonly TestServices _services = new();

    private static ScriptDefinition Script(params ScriptOperation[] operations)
    {
        var script = new ScriptDefinition { Id = Guid.NewGuid(), Name = "Feature test" };
        script.Operations.AddRange(operations);
        return script;
    }

    private static ScriptOperation Wait() => new() { Type = OperationType.Wait, WaitSeconds = 0 };

    private static EventContext Context(ScriptTrigger trigger = ScriptTrigger.ServerStarted)
    {
        var context = new EventContext(trigger) { Summary = "summary" };
        context.Set("EVENT", trigger.ToString());
        return context;
    }

    private static PluginConfiguration With(ScriptDefinition script) => new() { Scripts = new List<ScriptDefinition> { script } };

    private static ScriptDefinition Valid() => new()
    {
        Name = "Notify",
        Triggers = new List<ScriptTrigger> { ScriptTrigger.PlaybackStart },
        Operations = new List<ScriptOperation> { new() { Type = OperationType.RunCommand, Command = "/scripts/notify.sh" } }
    };

    private static void Check(ScriptDefinition script)
    {
        var config = With(script);
        ConfigurationValidator.Normalize(config);
        ConfigurationValidator.Validate(config);
    }

    [Fact]
    public async Task AStepWhoseConditionDoesNotMatchIsSkippedWithoutFailing()
    {
        var conditional = Wait();
        conditional.OnlyIfVariable = "EVENT";
        conditional.OnlyIfOperator = ConditionOperator.Is;
        conditional.OnlyIfValue = "PlaybackStart";

        var run = await _services.Runner.RunAsync(Script(conditional, Wait()), Context(), false, CancellationToken.None);

        Assert.True(run.Succeeded);
        Assert.Equal(new[] { OperationStatus.Skipped, OperationStatus.Succeeded }, run.Operations.Select(o => o.Status));
    }

    [Theory]
    [InlineData("EVENT", ConditionOperator.Is, "serverstarted", true)]
    [InlineData("{{EVENT}}", ConditionOperator.IsNot, "PlaybackStart", true)]
    [InlineData("JF_EVENT", ConditionOperator.Contains, "Started", true)]
    [InlineData("MISSING", ConditionOperator.Is, "", true)]
    [InlineData("MISSING", ConditionOperator.IsNot, "", false)]
    [InlineData("EVENT", ConditionOperator.Is, "", false)]
    [InlineData("MISSING", ConditionOperator.AtMost, "5", false)]
    [InlineData("COUNT", ConditionOperator.AtLeast, "3", true)]
    [InlineData("COUNT", ConditionOperator.AtLeast, "{{LIMIT}}", false)]
    public void StepConditionsCompareVariables(string variable, ConditionOperator op, string value, bool expected)
    {
        var variables = new Dictionary<string, string> { ["EVENT"] = "ServerStarted", ["COUNT"] = "4", ["LIMIT"] = "9" };
        var step = new ScriptOperation { OnlyIfVariable = variable, OnlyIfOperator = op, OnlyIfValue = value };

        Assert.Equal(expected, ConditionEvaluator.MatchesStep(step, variables));
    }

    [Fact]
    public async Task StopHereSkipsTheRestAndTheRunSucceeds()
    {
        var run = await _services.Runner.RunAsync(
            Script(Wait(), new ScriptOperation { Type = OperationType.StopScript }, Wait()),
            Context(),
            false,
            CancellationToken.None);

        Assert.True(run.Succeeded);
        Assert.Equal(new[] { OperationStatus.Succeeded, OperationStatus.Succeeded, OperationStatus.Skipped }, run.Operations.Select(o => o.Status));
        Assert.Contains("stopped at step 2", run.Operations[2].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACommandsOutputIsSavedForLaterSteps()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var produce = new ScriptOperation
        {
            Type = OperationType.RunCommand,
            RunThroughShell = true,
            Command = "printf '{\"data\":{\"items\":[{\"name\":\"first\"}]}}'",
            OutputName = "RESULT",
            OutputJsonPath = "data.items[0].name"
        };
        var onlyWhenFirst = Wait();
        onlyWhenFirst.OnlyIfVariable = "RESULT";
        onlyWhenFirst.OnlyIfValue = "first";
        var onlyWhenFailed = Wait();
        onlyWhenFailed.OnlyIfVariable = "RESULT_STATUS";
        onlyWhenFailed.OnlyIfOperator = ConditionOperator.IsNot;
        onlyWhenFailed.OnlyIfValue = "0";
        var echo = new ScriptOperation { Type = OperationType.RunCommand, RunThroughShell = true, Command = "printf '%s' \"$JF_RESULT\"" };

        var run = await _services.Runner.RunAsync(Script(produce, onlyWhenFirst, onlyWhenFailed, echo), Context(), false, CancellationToken.None);

        Assert.True(run.Succeeded);
        Assert.Equal("first", run.Variables["RESULT"]);
        Assert.Equal("0", run.Variables["RESULT_STATUS"]);
        Assert.Equal(OperationStatus.Succeeded, run.Operations[1].Status);
        Assert.Equal(OperationStatus.Skipped, run.Operations[2].Status);
        Assert.Equal("first", run.Operations[3].Output.Trim());
    }

    [Theory]
    [InlineData("{\"a\":{\"b\":[1,2]}}", "a.b[1]", true, "2")]
    [InlineData("{\"Name\":\"x\"}", "name", true, "x")]
    [InlineData("{\"a\":true}", "$.a", true, "true")]
    [InlineData("{\"a\":{\"b\":1}}", "a", true, "{\"b\":1}")]
    [InlineData("{\"a\":1}", "b", false, "")]
    [InlineData("not json", "a", false, "")]
    [InlineData("  plain text \n", "", true, "plain text")]
    public void OutputCaptureReadsAField(string output, string path, bool found, string expected)
    {
        Assert.Equal(found, OutputCapture.TryRead(output, path, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void WebhookVariablesFlattenTheBodyAndQuery()
    {
        var values = WebhookVariables.Read(
            "{\"eventType\":\"Download\",\"series\":{\"title\":\"A Show\",\"id\":7},\"tags\":[\"a\",\"b\"],\"ok\":true}",
            new[] { new KeyValuePair<string, string>("source", "sonarr"), new KeyValuePair<string, string>("body", "ignored") });

        Assert.Equal("Download", values["WEBHOOK_EVENTTYPE"]);
        Assert.Equal("A Show", values["WEBHOOK_SERIES_TITLE"]);
        Assert.Equal("7", values["WEBHOOK_SERIES_ID"]);
        Assert.Equal("b", values["WEBHOOK_TAGS_1"]);
        Assert.Equal("true", values["WEBHOOK_OK"]);
        Assert.Equal("sonarr", values["WEBHOOK_SOURCE"]);
        Assert.StartsWith("{\"eventType\"", values["WEBHOOK_BODY"], StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookVariablesKeepABodyThatIsNotJson()
    {
        var values = WebhookVariables.Read("hello there", null);

        Assert.Equal("hello there", Assert.Single(values).Value);
    }

    [Fact]
    public void AWebhookScriptGetsATokenAndRunNowIsNeverATrigger()
    {
        var script = Valid();
        script.Triggers = new List<ScriptTrigger> { ScriptTrigger.Webhook, ScriptTrigger.Manual };

        Check(script);

        Assert.Equal(new[] { ScriptTrigger.Webhook }, script.Triggers);
        Assert.Equal(ConfigurationValidator.WebhookTokenBytes * 2, script.WebhookToken.Length);
    }

    [Fact]
    public void RejectsAWeakOrSharedWebhookToken()
    {
        var weak = Valid();
        weak.Triggers = new List<ScriptTrigger> { ScriptTrigger.Webhook };
        weak.WebhookToken = "abc123";
        Assert.Throws<ArgumentException>(() => Check(weak));

        var first = Valid();
        first.Triggers = new List<ScriptTrigger> { ScriptTrigger.Webhook };
        var second = Valid();
        second.Triggers = new List<ScriptTrigger> { ScriptTrigger.Webhook };
        second.WebhookToken = first.WebhookToken = ConfigurationValidator.NewWebhookToken();
        var config = new PluginConfiguration { Scripts = new List<ScriptDefinition> { first, second } };
        ConfigurationValidator.Normalize(config);
        Assert.Throws<ArgumentException>(() => ConfigurationValidator.Validate(config));
    }

    [Fact]
    public void ForEachItemNeedsAnItemKind()
    {
        var script = Valid();
        script.ForEach = ForEachMode.Items;
        Assert.Throws<ArgumentException>(() => Check(script));

        script.ItemKinds.Add(Jellyfin.Data.Enums.BaseItemKind.Movie);
        Check(script);
    }

    [Theory]
    [InlineData("ITEM_NAME")]
    [InlineData("SECRET_TOKEN")]
    [InlineData("WEBHOOK_BODY")]
    [InlineData("lower case")]
    public void RejectsAnOutputNameTheEventAlreadyUses(string name)
    {
        var script = Valid();
        script.Operations[0].OutputName = name;
        Assert.Throws<ArgumentException>(() => Check(script));
    }

    [Fact]
    public void RejectsItselfAsItsFailureScriptAndABadStepCondition()
    {
        var self = Valid();
        self.Id = Guid.NewGuid();
        self.OnFailureScriptId = self.Id.ToString("N");
        Assert.Throws<ArgumentException>(() => Check(self));

        var number = Valid();
        number.Operations[0].OnlyIfVariable = "PROGRESS_PERCENT";
        number.Operations[0].OnlyIfOperator = ConditionOperator.AtLeast;
        number.Operations[0].OnlyIfValue = "most";
        Assert.Throws<ArgumentException>(() => Check(number));
    }

    [Fact]
    public void ForEachOnlyExpandsEventsWithoutTheirOwnItemOrUser()
    {
        Assert.True(TriggerCatalog.Expands(ScriptTrigger.Schedule, ForEachMode.Items));
        Assert.True(TriggerCatalog.Expands(ScriptTrigger.Webhook, ForEachMode.Users));
        Assert.False(TriggerCatalog.Expands(ScriptTrigger.PlaybackStart, ForEachMode.Items));
        Assert.False(TriggerCatalog.Expands(ScriptTrigger.Schedule, ForEachMode.None));
        Assert.True(TriggerCatalog.Supplies(ScriptTrigger.Schedule, ConditionField.DaysSincePlayed, ForEachMode.Items));
        Assert.False(TriggerCatalog.Supplies(ScriptTrigger.Schedule, ConditionField.DaysSincePlayed, ForEachMode.None));
    }

    [Fact]
    public void AForEachUserScriptMatchesAgainstEachUsersOwnValues()
    {
        var script = new ScriptDefinition { Name = "Each user", ForEach = ForEachMode.Users };
        script.Conditions.Add(new ScriptCondition { Field = ConditionField.DaysSinceActive, Operator = ConditionOperator.AtLeast, Values = { "30" } });

        var stale = Context(ScriptTrigger.Schedule);
        stale.ForEach = ForEachMode.Users;
        stale.Set("DAYS_SINCE_ACTIVE", "45");
        var recent = stale.Copy();
        recent.Set("DAYS_SINCE_ACTIVE", "2");

        Assert.True(ConditionEvaluator.Matches(script, stale));
        Assert.False(ConditionEvaluator.Matches(script, recent));
        Assert.False(ConditionEvaluator.Matches(script, Context(ScriptTrigger.Schedule)));
    }

    [Fact]
    public void ProgressAndTranscodeEdgesAreReportedOnce()
    {
        var tracker = new PlaybackTracker();
        var script = Guid.NewGuid();
        tracker.Started("play", null, DateTime.UtcNow);

        Assert.Null(tracker.AdvanceProgress("play", 10));
        Assert.Equal(10, tracker.AdvanceProgress("play", 95));
        Assert.True(tracker.ClaimProgress("play", script));
        Assert.False(tracker.ClaimProgress("play", script));

        Assert.False(tracker.RememberPlayMethod("play", "DirectPlay"));
        Assert.True(tracker.RememberPlayMethod("play", "Transcode"));
        Assert.False(tracker.RememberPlayMethod("play", "Transcode"));
    }

    public void Dispose() => _services.Dispose();
}
