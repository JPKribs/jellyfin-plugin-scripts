using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Performs one step of a script.
/// </summary>
public partial class OperationExecutor
{
    /// <summary>The longest a single wait step may pause, in seconds.</summary>
    public const int MaxWaitSeconds = 3600;

    /// <summary>The most times an HTTP step may retry.</summary>
    public const int MaxRetries = 5;

    private const int MaxResponseChars = 4096;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionManager _sessionManager;
    private readonly IProviderManager _providerManager;
    private readonly ITaskManager _taskManager;
    private readonly IFileSystem _fileSystem;
    private readonly ActivityLogger _activityLogger;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly ILogger<OperationExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationExecutor"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="providerManager">The metadata provider manager.</param>
    /// <param name="taskManager">The scheduled task manager.</param>
    /// <param name="fileSystem">The file system.</param>
    /// <param name="activityLogger">The activity log writer.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="collectionManager">The collection manager.</param>
    /// <param name="logger">The logger.</param>
    public OperationExecutor(
        IHttpClientFactory httpClientFactory,
        ISessionManager sessionManager,
        IProviderManager providerManager,
        ITaskManager taskManager,
        IFileSystem fileSystem,
        ActivityLogger activityLogger,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        ILogger<OperationExecutor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionManager = sessionManager;
        _providerManager = providerManager;
        _taskManager = taskManager;
        _fileSystem = fileSystem;
        _activityLogger = activityLogger;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _logger = logger;
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
            OperationType.PausePlayback => PausePlaybackAsync(state, cancellationToken),
            OperationType.MarkPlayed => Task.FromResult(SetPlayed(state, true)),
            OperationType.MarkUnplayed => Task.FromResult(SetPlayed(state, false)),
            OperationType.SetFavorite => Task.FromResult(SetFavorite(operation, state, cancellationToken)),
            OperationType.AddTag => ChangeTagAsync(operation, state, true, cancellationToken),
            OperationType.RemoveTag => ChangeTagAsync(operation, state, false, cancellationToken),
            OperationType.AddToCollection => ChangeCollectionAsync(operation, state, true),
            OperationType.RemoveFromCollection => ChangeCollectionAsync(operation, state, false),
            OperationType.SetUserDisabled => SetUserDisabledAsync(operation, state),
            OperationType.SignOutUser => SignOutUserAsync(state),
            OperationType.ScanLibraries => Task.FromResult(ScanLibraries()),
            OperationType.AppendToFile => AppendToFileAsync(operation, state, cancellationToken),
            OperationType.RunScript => Task.FromResult(Result(OperationStatus.Failed, "Run script is handled by the runner.")),
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
            // Event values may fill the command, but never steer it to another program: a device name or a typed
            // username holding a path could otherwise pick what runs.
            var steering = Placeholders.Names(op.Command).FirstOrDefault(name => SteersPath(state.Variables.GetValueOrDefault(name)));
            if (steering is not null)
            {
                return Result(OperationStatus.Failed, "Not run: the value of {{" + steering + "}} holds a path, which would change which program runs. Pass it as an argument instead.");
            }

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

        // JpkHttp never throws, so a response worth retrying (no answer, a 5xx, or a 429) is turned into the
        // HttpRequestException RetryPolicy treats as transient. The last one is caught and reported below.
        // The last answer is kept, so when the retries run out the step still reports its status and body.
        var retries = Math.Clamp(op.Retries, 0, MaxRetries);
        HttpResult? last = null;
        HttpResult response;
        try
        {
            response = await RetryPolicy.ExecuteWithRetryAsync(
                async ct =>
                {
                    var result = await JpkHttp.SendAsync(_httpClientFactory, method, uri.ToString(), ct, headers, body, contentType).ConfigureAwait(false);
                    last = result;
                    if (retries > 0 && (result.Status == 0 || result.Status >= 500 || result.Status == 429))
                    {
                        throw new HttpRequestException(result.Status == 0 ? result.Body : "HTTP " + Number(result.Status), null, result.Status == 0 ? null : (HttpStatusCode)result.Status);
                    }

                    return result;
                },
                retries,
                _logger,
                "Scripts HTTP request to " + uri.Host,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            if (last is null)
            {
                return Result(OperationStatus.Failed, "Failed after " + Number(retries + 1) + " attempts: " + ex.Message);
            }

            response = last;
        }

        var text = response.Body.Length > MaxResponseChars ? response.Body[..MaxResponseChars] + "\n[response truncated]" : response.Body;

        if (response.Status == 0)
        {
            return Result(OperationStatus.Failed, (retries > 0 ? "No response after " + Number(retries + 1) + " attempts: " : "No response: ") + text);
        }

        var message = method + " " + uri.Host + " returned " + Number(response.Status)
            + (!response.Ok && retries > 0 ? " after " + Number(retries + 1) + " attempts." : ".");
        return Result(response.Ok ? OperationStatus.Succeeded : OperationStatus.Failed, message, text);
    }

    private async Task<OperationResult> SendMessageAsync(ScriptOperation op, RunState state, CancellationToken cancellationToken)
    {
        if (op.Target == MessageTarget.EventSession)
        {
            if (string.IsNullOrEmpty(state.Context.SessionId))
            {
                return Result(OperationStatus.Skipped, "This event has no session to send a message to.");
            }

            await _sessionManager.SendMessageCommand(null, state.Context.SessionId, Message(op, state), cancellationToken).ConfigureAwait(false);
            return Result(OperationStatus.Succeeded, "Sent the message to " + Describe(state) + ".");
        }

        if (op.Target == MessageTarget.UserSessions && state.Context.UserIds.Count == 0)
        {
            return Result(OperationStatus.Skipped, "This event has no user whose sessions could get the message.");
        }

        // A test never messages other people: its message for every session or every administrator goes only to
        // the sessions of the administrator running it.
        var tester = state.Context.TesterId;
        var sessions = _sessionManager.Sessions
            .Where(s => s.IsActive && s.SupportsRemoteControl)
            .Where(s => tester is { } testerId
                ? s.UserId == testerId
                : op.Target switch
                {
                    MessageTarget.UserSessions => state.Context.UserIds.Contains(s.UserId),
                    MessageTarget.AdminSessions => IsAdministrator(s.UserId),
                    _ => true
                })
            .ToList();
        if (sessions.Count == 0)
        {
            return Result(OperationStatus.Skipped, tester is null ? "No active session can receive messages." : "A test sends this message only to your own sessions, and none of them can receive messages.");
        }

        var message = Message(op, state);
        var sent = 0;
        foreach (var session in sessions)
        {
            try
            {
                await _sessionManager.SendMessageCommand(null, session.Id, message, cancellationToken).ConfigureAwait(false);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One client refusing the message does not stop the others.
                _logger.LogDebug(ex, "Scripts: could not send a message to session {Session}", session.Id);
            }
        }

        return Result(
            sent > 0 ? OperationStatus.Succeeded : OperationStatus.Failed,
            "Sent the message to " + Number(sent) + " of " + Number(sessions.Count) + (tester is null ? " sessions." : " of your sessions, since this is a test."));
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

        // A task starting or finishing that queues itself would repeat forever, and so would tasks that queue
        // each other. A task a script queued therefore never queues another.
        var isTaskEvent = state.Context.Trigger is ScriptTrigger.TaskStarted or ScriptTrigger.TaskCompleted;
        if (isTaskEvent && string.Equals(state.Context.Get("TASK_KEY"), key, StringComparison.OrdinalIgnoreCase))
        {
            return Result(OperationStatus.Skipped, "Not queued: this run was started by the same task, which would repeat forever.");
        }

        if (state.Context.QueuedByScript)
        {
            return Result(OperationStatus.Skipped, "Not queued: this run was started by a task another script queued, and tasks queuing each other could repeat forever.");
        }

        TaskChains.MarkQueued(worker.ScheduledTask.Key);
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

    private static bool SteersPath(string? value)
        => value is not null && (value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal) || value.Contains("..", StringComparison.Ordinal));

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
