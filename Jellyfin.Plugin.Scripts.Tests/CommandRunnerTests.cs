using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class CommandRunnerTests
{
    private static ProcessStartInfo Shell(string line)
    {
        var info = new ProcessStartInfo("/bin/sh");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(line);
        return info;
    }

    [Fact]
    public async Task CapturesOutputAndExitCode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var info = Shell("echo \"$JF_ITEM_NAME\"; echo oops 1>&2; exit 3");
        info.Environment["JF_ITEM_NAME"] = "The Matrix";

        var result = await CommandRunner.RunAsync(info, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("The Matrix", result.Output, StringComparison.Ordinal);
        Assert.Contains("oops", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KillsAProcessThatRunsTooLong()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        var result = await CommandRunner.RunAsync(Shell("sleep 30"), TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AScriptThatLeavesABackgroundProcessStillReportsItsExit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        var result = await CommandRunner.RunAsync(Shell("echo started; sleep 8 & exit 0"), TimeSpan.FromSeconds(6), CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("started", result.Output, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "took " + watch.Elapsed);
    }

    [Fact]
    public async Task ReportsAMissingExecutable()
    {
        var result = await CommandRunner.RunAsync(new ProcessStartInfo("/definitely/not/here"), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.NotNull(result.StartError);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task ArgumentsAreNeverSplit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var info = new ProcessStartInfo("/bin/sh");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("echo \"$#:$1\"");
        info.ArgumentList.Add("sh");
        info.ArgumentList.Add("a b; rm -rf /tmp/nothing $(whoami)");

        var result = await CommandRunner.RunAsync(info, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal("1:a b; rm -rf /tmp/nothing $(whoami)\n", result.Output);
    }
}
