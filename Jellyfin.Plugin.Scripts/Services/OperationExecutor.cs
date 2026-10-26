using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Performs one step of a script.
/// </summary>
public class OperationExecutor
{
    /// <summary>The longest a single wait step may pause, in seconds.</summary>
    public const int MaxWaitSeconds = 3600;

    private const int MaxResponseChars = 4096;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionManager _sessionManager;
    private readonly IProviderManager _providerManager;
    private readonly ITaskManager _taskManager;
    private readonly IFileSystem _fileSystem;
    private readonly ActivityLogger _activityLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationExecutor"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="providerManager">The metadata provider manager.</param>
    /// <param name="taskManager">The scheduled task manager.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="activityLogger">The activity log writer.</param>
    public OperationExecutor(
        IHttpClientFactory httpClientFactory,
        ISessionManager sessionManager,
        IProviderManager providerManager,
        ITaskManager taskManager,
        IFileSystem fileSystem,
        ActivityLogger activityLogger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionManager = sessionManager;
        _providerManager = providerManager;
        _taskManager = taskManager;
        _fileSystem = fileSystem;
        _activityLogger = activityLogger;
    }

    /// <summary>
    /// Performs a step.
    /// </summary>
    /// <param name="operation">The step.</param>
    /// <param name="state">The run it belongs to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    public Task<OperationResult> ExecuteAsync(ScriptOperation operation, RunState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(state);

        return operation.Type switch
        {
            OperationType.RunCommand => RunCommandAsync(operation, state, cancellationToken),
            OperationType.HttpRequest => HttpRequestAsync(operation, state, cancellationToken),
            OperationType.SendMessage => SendMessageAsync(operation, state, cancellationToken),
            OperationType.StopPlayback => StopPlaybackAsync(operation, state, cancellationToken),
            OperationType.RefreshMetadata => Task.FromResult(RefreshMetadata(operation, state)),
            OperationType.RunScheduledTask => Task.FromResult(RunScheduledTask(operation, state)),
            OperationType.WriteActivityLog => Task.FromResult(WriteActivityLog(operation, state)),
            OperationType.Wait => WaitAsync(operation, cancellationToken),
            _ => Task.FromResult(Result(OperationStatus.Failed, "Unknown step type."))
        };
    }

    [SuppressMessage("Security", "CA3006:Review code for process command injection vulnerabilities", Justification = "Running the command an administrator configured is the purpose of this step. Event values never reach a shell line.")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "The path is the administrator's own script, checked so a missing file gets a clear message.")]
    private async Task<OperationResult> RunCommandAsync(ScriptOperation op, RunState state, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo();
        string? scriptFile = null;
        if (op.RunThroughShell)
        {
            // The shell line is never filled with placeholders: event values such as an item or device name
            // could otherwise carry shell syntax. They reach the shell as environment variables instead.
            // On Windows the line is handed to cmd verbatim: ArgumentList would escape its quotes with
            // backslashes, which cmd does not understand. cmd expands %JF_...% before it parses the line, so
            // there event values can carry cmd syntax; the docs say to read them inside a script instead.
            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = "/d /s /c \"" + op.Command + "\"";
            }
            else
            {
                startInfo.FileName = "/bin/sh";
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(op.Command);
            }
        }
        else
        {
            var command = Placeholders.Fill(op.Command, state.Variables).Trim();
            if (IsPath(command))
            {
                // The script lives wherever the administrator keeps it, so say plainly when the server cannot
                // see it rather than surfacing the platform's start error.
                if (!File.Exists(command))
                {
                    return Result(
                        OperationStatus.Failed,
                        "The server cannot find " + command + ". Check the path, and that Jellyfin can reach it (in Docker, mount it into the container).");
                }

                scriptFile = Path.GetFullPath(command);
                command = scriptFile;
            }

            startInfo.FileName = command;
            foreach (var line in Lines(op.Arguments))
            {
                startInfo.ArgumentList.Add(Placeholders.Fill(line, state.Variables));
            }

            // Windows runs a .bat or .cmd file through cmd, which does not honour the quoting ArgumentList
            // applies, so an argument holding cmd syntax could break out. Such a run is refused rather than risked.
            if (OperatingSystem.IsWindows() && IsBatchFile(command) && startInfo.ArgumentList.Any(a => a.IndexOfAny(CmdSpecialChars) >= 0))
            {
                return Result(
                    OperationStatus.Failed,
                    "Not run: an argument for this batch file contains a character cmd treats as syntax (% \" & | < > ^). Read the value from the environment inside the script instead.");
            }
        }

        var workingDirectory = Placeholders.Fill(op.WorkingDirectory, state.Variables).Trim();
        if (workingDirectory.Length > 0)
        {
            if (!Directory.Exists(workingDirectory))
            {
                return Result(OperationStatus.Failed, "The server cannot find the working folder " + workingDirectory + ".");
            }

            startInfo.WorkingDirectory = workingDirectory;
        }
        else if (scriptFile is not null)
        {
            startInfo.WorkingDirectory = Path.GetDirectoryName(scriptFile) ?? string.Empty;
        }

        foreach (var (name, value) in state.Environment)
        {
            startInfo.Environment[name] = value;
        }

        var seconds = op.TimeoutSeconds > 0 ? op.TimeoutSeconds : state.DefaultTimeoutSeconds;
        var result = await CommandRunner.RunAsync(startInfo, TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

        if (result.StartError is not null)
        {
            return Result(OperationStatus.Failed, "Could not start " + startInfo.FileName + ": " + result.StartError);
        }

        if (result.TimedOut)
        {
            return Result(OperationStatus.Failed, "Timed out after " + Number(seconds) + " seconds and was stopped.", result.Output);
        }

        return result.ExitCode == 0
            ? Result(OperationStatus.Succeeded, "Exited with code 0.", result.Output)
            : Result(OperationStatus.Failed, "Exited with code " + Number(result.ExitCode ?? -1) + ".", result.Output);
    }

    private async Task<OperationResult> HttpRequestAsync(ScriptOperation op, RunState state, CancellationToken cancellationToken)
    {
        var url = Placeholders.Fill(op.Url, state.Variables).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Result(OperationStatus.Failed, "Not an http or https URL: " + url);
        }

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in Lines(op.Headers))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers.Add(new(line[..colon].Trim(), Placeholders.Fill(line[(colon + 1)..].Trim(), state.Variables)));
            }
        }

        var body = string.IsNullOrEmpty(op.Body) ? null : Placeholders.Fill(op.Body, state.Variables);
        // The body is always sent as UTF-8, so parameters such as "; charset=utf-8" are dropped rather than passed
        // to a constructor that accepts only the media type.
        var mediaType = (op.ContentType ?? string.Empty).Split(';')[0].Trim();
        var contentType = mediaType.Length == 0 ? "application/json" : mediaType;
        var method = new HttpMethod(string.IsNullOrWhiteSpace(op.Method) ? "POST" : op.Method.Trim().ToUpperInvariant());

        var response = await JpkHttp.SendAsync(_httpClientFactory, method, uri.ToString(), cancellationToken, headers, body, contentType).ConfigureAwait(false);
        var text = response.Body.Length > MaxResponseChars ? response.Body[..MaxResponseChars] + "\n[response truncated]" : response.Body;

        if (response.Status == 0)
        {
            return Result(OperationStatus.Failed, "No response: " + text);
        }

        var message = method + " " + uri.Host + " returned " + Number(response.Status) + ".";
        return Result(response.Ok ? OperationStatus.Succeeded : OperationStatus.Failed, message, text);
    }

    private async Task<OperationResult> SendMessageAsync(ScriptOperation op, RunState state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state.Context.SessionId))
        {
            return Result(OperationStatus.Skipped, "This event has no session to send a message to.");
        }

        await _sessionManager.SendMessageCommand(null, state.Context.SessionId, Message(op, state), cancellationToken).ConfigureAwait(false);
        return Result(OperationStatus.Succeeded, "Sent the message to " + Describe(state) + ".");
    }

    private async Task<OperationResult> StopPlaybackAsync(ScriptOperation op, RunState state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state.Context.SessionId))
        {
            return Result(OperationStatus.Skipped, "This event has no session to stop.");
        }

        if (!string.IsNullOrWhiteSpace(op.Text))
        {
            await _sessionManager.SendMessageCommand(null, state.Context.SessionId, Message(op, state), cancellationToken).ConfigureAwait(false);
        }

        var request = new PlaystateRequest { Command = PlaystateCommand.Stop };
        await _sessionManager.SendPlaystateCommand(null, state.Context.SessionId, request, cancellationToken).ConfigureAwait(false);
        return Result(OperationStatus.Succeeded, "Stopped playback on " + Describe(state) + ".");
    }

    private OperationResult RefreshMetadata(ScriptOperation op, RunState state)
    {
        if (state.Context.ItemId is not { } itemId)
        {
            return Result(OperationStatus.Skipped, "This event has no item to refresh.");
        }

        var mode = op.FullRefresh ? MetadataRefreshMode.FullRefresh : MetadataRefreshMode.Default;
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = mode,
            ImageRefreshMode = mode,
            ReplaceAllMetadata = op.ReplaceAll,
            ReplaceAllImages = op.ReplaceAll
        };

        _providerManager.QueueRefresh(itemId, options, RefreshPriority.Normal);
        return Result(OperationStatus.Succeeded, "Queued a " + (op.FullRefresh ? "full" : "standard") + " metadata refresh of " + state.Context.Get("ITEM_NAME") + ".");
    }

    private OperationResult RunScheduledTask(ScriptOperation op, RunState state)
    {
        var key = op.TaskKey.Trim();
        var worker = _taskManager.ScheduledTasks.FirstOrDefault(t => string.Equals(t.ScheduledTask.Key, key, StringComparison.OrdinalIgnoreCase));
        if (worker is null)
        {
            return Result(OperationStatus.Failed, "No scheduled task has the key " + key + ".");
        }

        // A script run by this task finishing would queue it again, and again after that.
        if (state.Context.Trigger == ScriptTrigger.TaskCompleted
            && string.Equals(state.Context.Get("TASK_KEY"), key, StringComparison.OrdinalIgnoreCase))
        {
            return Result(OperationStatus.Skipped, "Not queued: this run was started by the same task finishing, which would repeat forever.");
        }

        _taskManager.QueueScheduledTask(worker.ScheduledTask, new TaskOptions());
        return Result(OperationStatus.Succeeded, "Queued " + worker.Name + ".");
    }

    private OperationResult WriteActivityLog(ScriptOperation op, RunState state)
    {
        var name = Placeholders.Fill(op.Title, state.Variables).Trim();
        var overview = Placeholders.Fill(op.Text, state.Variables).Trim();
        _activityLogger.Log(name.Length > 0 ? name : state.Script.Name, "Scripts.Script", overview.Length > 0 ? overview : null);
        return Result(OperationStatus.Succeeded, "Wrote to the activity log.");
    }

    private static async Task<OperationResult> WaitAsync(ScriptOperation op, CancellationToken cancellationToken)
    {
        var seconds = Math.Clamp(op.WaitSeconds, 0, MaxWaitSeconds);
        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
        return Result(OperationStatus.Succeeded, "Waited " + Number(seconds) + " seconds.");
    }

    private static MessageCommand Message(ScriptOperation op, RunState state)
    {
        var title = Placeholders.Fill(op.Title, state.Variables).Trim();
        return new MessageCommand
        {
            Header = title.Length > 0 ? title : null,
            Text = Placeholders.Fill(op.Text, state.Variables),
            TimeoutMs = op.DisplaySeconds > 0 ? op.DisplaySeconds * 1000L : null
        };
    }

    private static readonly char[] CmdSpecialChars = { '%', '"', '&', '|', '<', '>', '^' };

    private static bool IsBatchFile(string command)
        => command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

    private static bool IsPath(string command)
        => Path.IsPathRooted(command) || command.Contains('/', StringComparison.Ordinal) || command.Contains('\\', StringComparison.Ordinal);

    private static string Describe(RunState state)
    {
        var device = state.Context.Get("DEVICE_NAME");
        return device.Length > 0 ? device : "the session";
    }

    private static IEnumerable<string> Lines(string? text)
        => (text ?? string.Empty).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static OperationResult Result(OperationStatus status, string message, string output = "")
        => new() { Status = status, Message = message, Output = output };
}
