using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public sealed class OperationExecutorTests : IDisposable
{
    private readonly TestServices _services = new();

    private static RunState State(EventContext? context = null, Dictionary<string, string>? variables = null)
    {
        context ??= new EventContext(ScriptTrigger.ServerStarted);
        var script = new ScriptDefinition { Id = Guid.NewGuid(), Name = "Executor test" };
        return new RunState(script, context, variables ?? context.Variables, new Dictionary<string, string>(), 10);
    }

    private Task<OperationResult> Run(ScriptOperation op, RunState? state = null)
        => _services.Executor.ExecuteAsync(op, state ?? State(), CancellationToken.None);

    [Fact]
    public async Task AnEventValueCannotPickTheProgram()
    {
        var variables = new Dictionary<string, string> { ["DEVICE_NAME"] = "../../bin/evil" };
        var result = await Run(new ScriptOperation { Type = OperationType.RunCommand, Command = "/scripts/{{DEVICE_NAME}}.sh" }, State(variables: variables));

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Contains("DEVICE_NAME", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingScriptFileGetsAPlainMessage()
    {
        var missing = Path.Combine(_services.Folder, "missing.sh");
        var result = await Run(new ScriptOperation { Type = OperationType.RunCommand, Command = missing });

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Contains("cannot find", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingWorkingFolderFails()
    {
        var result = await Run(new ScriptOperation
        {
            Type = OperationType.RunCommand,
            RunThroughShell = true,
            Command = "true",
            WorkingDirectory = Path.Combine(_services.Folder, "nowhere")
        });

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Contains("working folder", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArgumentsAreFilledOnePerLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var variables = new Dictionary<string, string> { ["ITEM_NAME"] = "A b; $(c)" };
        var result = await Run(
            new ScriptOperation { Type = OperationType.RunCommand, Command = "/bin/sh", Arguments = "-c\necho \"$#:$1\"\nsh\n{{ITEM_NAME}}\n\n" },
            State(variables: variables));

        Assert.Equal(OperationStatus.Succeeded, result.Status);
        Assert.Equal("1:A b; $(c)\n", result.Output);
    }

    [Fact]
    public async Task ANonZeroExitFails()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await Run(new ScriptOperation { Type = OperationType.RunCommand, RunThroughShell = true, Command = "echo out; exit 4" });

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Equal("Exited with code 4.", result.Message);
        Assert.Equal("out\n", result.Output);
    }

    [Fact]
    public async Task AnHttpStepNeedsAnHttpUrl()
    {
        var result = await Run(new ScriptOperation { Type = OperationType.HttpRequest, Url = "ftp://example.com/file" });

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.StartsWith("Not an http or https URL", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppendsAFilledLine()
    {
        var path = Path.Combine(_services.Folder, "log.txt");
        var variables = new Dictionary<string, string> { ["ITEM_NAME"] = "The Matrix" };
        var op = new ScriptOperation { Type = OperationType.AppendToFile, FilePath = path, Text = "played {{ITEM_NAME}}" };

        Assert.Equal(OperationStatus.Succeeded, (await Run(op, State(variables: variables))).Status);
        Assert.Equal(OperationStatus.Succeeded, (await Run(op, State(variables: variables))).Status);
        Assert.Equal(new[] { "played The Matrix", "played The Matrix" }, File.ReadAllLines(path));
    }

    [Fact]
    public async Task AppendingNeedsAnExistingFolder()
    {
        var result = await Run(new ScriptOperation { Type = OperationType.AppendToFile, FilePath = Path.Combine(_services.Folder, "nowhere", "log.txt"), Text = "x" });

        Assert.Equal(OperationStatus.Failed, result.Status);
    }

    [Fact]
    public async Task StepsThatNeedASessionOrItemSkipWithoutOne()
    {
        foreach (var type in new[] { OperationType.PausePlayback, OperationType.StopPlayback, OperationType.AddTag, OperationType.RefreshMetadata, OperationType.MarkPlayed, OperationType.SignOutUser })
        {
            var result = await Run(new ScriptOperation { Type = type, Tag = "x" });
            Assert.True(result.Status == OperationStatus.Skipped, type + " gave " + result.Status);
        }
    }

    [Fact]
    public async Task AMessageToTheEventSessionSkipsWithoutOne()
    {
        var result = await Run(new ScriptOperation { Type = OperationType.SendMessage, Text = "hi", Target = MessageTarget.EventSession });

        Assert.Equal(OperationStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task ATestMessageNeverReachesOtherPeople()
    {
        var context = new EventContext(ScriptTrigger.ServerStarted) { TesterId = Guid.NewGuid() };
        var result = await Run(new ScriptOperation { Type = OperationType.SendMessage, Text = "hi", Target = MessageTarget.AllSessions }, State(context));

        // The substitute server has no sessions, so the tester's own sessions are none.
        Assert.Equal(OperationStatus.Skipped, result.Status);
        Assert.Contains("your own sessions", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitIsCancellable()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _services.Executor.ExecuteAsync(new ScriptOperation { Type = OperationType.Wait, WaitSeconds = 30 }, State(), cancel.Token));
    }

    public void Dispose() => _services.Dispose();
}
