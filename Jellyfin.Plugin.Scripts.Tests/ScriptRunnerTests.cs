using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public sealed class ScriptRunnerTests : IDisposable
{
    private readonly TestServices _services = new();

    private static ScriptDefinition Script(params ScriptOperation[] operations)
    {
        var script = new ScriptDefinition { Id = Guid.NewGuid(), Name = "Runner test" };
        script.Operations.AddRange(operations);
        return script;
    }

    // Fails at once without a network call: the URL is not http or https.
    private static ScriptOperation Failing(bool continueOnError = false, string url = "not a url")
        => new() { Type = OperationType.HttpRequest, Url = url, ContinueOnError = continueOnError };

    private static ScriptOperation Succeeding() => new() { Type = OperationType.Wait, WaitSeconds = 0 };

    private static EventContext Context(ScriptTrigger trigger = ScriptTrigger.ServerStarted)
    {
        var context = new EventContext(trigger) { Summary = "summary" };
        context.Set("EVENT", trigger.ToString());
        return context;
    }

    [Fact]
    public async Task AFailedStepSkipsTheRest()
    {
        var run = await _services.Runner.RunAsync(Script(Succeeding(), Failing(), Succeeding()), Context(), false, CancellationToken.None);

        Assert.False(run.Succeeded);
        Assert.Equal(
            new[] { OperationStatus.Succeeded, OperationStatus.Failed, OperationStatus.Skipped },
            run.Operations.Select(o => o.Status));
        Assert.Equal(OperationType.Wait, run.Operations[2].Type);
    }

    [Fact]
    public async Task ContinueOnErrorRunsTheNextStep()
    {
        var run = await _services.Runner.RunAsync(Script(Failing(continueOnError: true), Succeeding()), Context(), false, CancellationToken.None);

        Assert.False(run.Succeeded);
        Assert.Equal(new[] { OperationStatus.Failed, OperationStatus.Succeeded }, run.Operations.Select(o => o.Status));
    }

    [Fact]
    public async Task RecordsTheRunInTheHistory()
    {
        var script = Script(Succeeding());
        var run = await _services.Runner.RunAsync(script, Context(), true, CancellationToken.None);

        Assert.True(run.Succeeded);
        var recorded = Assert.Single(_services.History.Query(new PagedQuery()).Items);
        Assert.Equal(run.Id, recorded.Id);
        Assert.Equal(script.Id, recorded.ScriptId);
        Assert.True(recorded.IsTest);
        Assert.Equal("summary", recorded.Summary);
        Assert.Equal("Runner test", recorded.Variables["SCRIPT_NAME"]);
    }

    [Fact]
    public async Task SecretsAreMaskedAndLeftOutOfTheRecordedVariables()
    {
        var script = Script(Failing(url: "{{SECRET_TOKEN}}"));
        script.Secrets.Add(new ScriptSecret { Name = "TOKEN", Value = "hunter22" });

        var run = await _services.Runner.RunAsync(script, Context(), true, CancellationToken.None);

        var message = run.Operations[0].Message;
        Assert.DoesNotContain("hunter22", message, StringComparison.Ordinal);
        Assert.Contains("[SECRET_TOKEN]", message, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Variables, v => v.Key.Contains("SECRET", StringComparison.Ordinal) || v.Value == "hunter22");
    }

    [Fact]
    public async Task OnlyAFailedRealRunWritesToTheActivityLog()
    {
        await _services.Runner.RunAsync(Script(Failing()), Context(), true, CancellationToken.None);
        await _services.Runner.RunAsync(Script(Succeeding()), Context(), false, CancellationToken.None);
        _services.Activity.Summary.Flush();
        Assert.Empty(_services.ActivityManager.ReceivedCalls());

        await _services.Runner.RunAsync(Script(Failing()), Context(), false, CancellationToken.None);
        _services.Activity.Summary.Flush();
        Assert.Single(_services.ActivityManager.ReceivedCalls());
    }

    [Fact]
    public async Task FailedRunsAreSummedIntoOneEntry()
    {
        await _services.Runner.RunAsync(Script(Failing()), Context(), false, CancellationToken.None);
        await _services.Runner.RunAsync(Script(Failing()), Context(), false, CancellationToken.None);
        Assert.Empty(_services.ActivityManager.ReceivedCalls());

        _services.Activity.Summary.Flush();
        Assert.Single(_services.ActivityManager.ReceivedCalls());
    }

    [Fact]
    public async Task ACancelledRunIsNotRecorded()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var script = Script(new ScriptOperation { Type = OperationType.Wait, WaitSeconds = 30 });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _services.Runner.RunAsync(script, Context(), false, cancel.Token));
        Assert.Equal(0, _services.History.Query(new PagedQuery()).TotalCount);
    }

    [Fact]
    public async Task ARunScriptStepWithoutATargetFails()
    {
        var run = await _services.Runner.RunAsync(Script(new ScriptOperation { Type = OperationType.RunScript, ScriptId = "nope" }), Context(), false, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, run.Operations[0].Status);
        Assert.Equal(OperationType.RunScript, run.Operations[0].Type);
    }

    [Fact]
    public async Task TheEnvironmentCarriesEveryTriggerVariableAndTheEventJson()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // ITEM_NAME is a variable of Item added that this event lacks: under set -u it must still be defined.
        var context = Context(ScriptTrigger.ItemAdded);
        context.Set("ITEM_ID", "abc");
        var script = Script(new ScriptOperation
        {
            Type = OperationType.RunCommand,
            RunThroughShell = true,
            Command = "set -u; printf '[%s]|%s|%s' \"$JF_ITEM_NAME\" \"$JF_SECRET_KEY\" \"$JF_EVENT_JSON\""
        });
        script.Secrets.Add(new ScriptSecret { Name = "KEY", Value = "s3cret" });

        var run = await _services.Runner.RunAsync(script, context, false, CancellationToken.None);

        var step = Assert.Single(run.Operations);
        Assert.Equal(OperationStatus.Succeeded, step.Status);
        Assert.StartsWith("[]|[SECRET_KEY]|{", step.Output, StringComparison.Ordinal);
        Assert.Contains("\"ITEM_ID\":\"abc\"", step.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", step.Output, StringComparison.Ordinal);
    }

    public void Dispose() => _services.Dispose();
}
