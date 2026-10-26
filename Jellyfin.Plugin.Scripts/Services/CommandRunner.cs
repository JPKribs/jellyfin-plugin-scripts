using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Runs one process with a timeout, capturing its output.
/// </summary>
public static class CommandRunner
{
    /// <summary>The most output kept from one process, in characters.</summary>
    public const int MaxOutputChars = 64 * 1024;

    /// <summary>How long output may keep arriving after the process exits.</summary>
    public static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Starts a process and waits for it to exit or time out. On timeout the whole process tree is killed.
    /// </summary>
    /// <param name="startInfo">What to start. Redirection and window settings are applied here.</param>
    /// <param name="timeout">How long the process may run.</param>
    /// <param name="cancellationToken">Cancels the wait and kills the process.</param>
    /// <returns>The outcome.</returns>
    public static async Task<CommandResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        var output = new OutputBuffer();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => output.Append(e.Data);
        process.ErrorDataReceived += (_, e) => output.Append(e.Data);
        process.Exited += (_, _) => exited.TrySetResult();

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return new CommandResult(null, false, string.Empty, ex.Message);
        }

        // Nothing is ever written to the script, so closing stdin keeps one that reads it from hanging.
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Waits for the process itself rather than WaitForExitAsync, which also waits for the output pipes to
        // close. A script that starts a background process (`notify &`) exits at once, but the child keeps the
        // pipes open, so waiting on them would report a finished script as timed out.
        try
        {
            await exited.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Kill(process);
            return new CommandResult(null, true, output.ToString(), null);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }

        // Gives the output that is still in the pipes a moment to arrive, without waiting on a background child.
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drain.CancelAfter(OutputGrace);
        try
        {
            await process.WaitForExitAsync(drain.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A background child still holds the pipes. The script itself has finished.
        }

        return new CommandResult(process.ExitCode, false, output.ToString(), null);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already exited, or the platform refused. Either way there is nothing more to do.
        }
    }

    private sealed class OutputBuffer
    {
        private readonly StringBuilder _text = new();
        private readonly object _lock = new();
        private bool _truncated;

        public void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_lock)
            {
                if (_truncated)
                {
                    return;
                }

                if (_text.Length + line.Length + 1 > MaxOutputChars)
                {
                    _text.Append("[output truncated]");
                    _truncated = true;
                    return;
                }

                _text.Append(line).Append('\n');
            }
        }

        public override string ToString()
        {
            lock (_lock)
            {
                return _text.ToString();
            }
        }
    }
}
