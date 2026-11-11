using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Runs queued scripts in the background, at most <see cref="Configuration.PluginConfiguration.MaxConcurrentRuns"/>
/// at once, so an event handler never waits on a script.
/// </summary>
public class ScriptRunner : BackgroundService
{
    /// <summary>How many runs may wait in the queue before new ones are dropped.</summary>
    public const int QueueCapacity = 500;

    private readonly Channel<ScriptJob> _queue = Channel.CreateBounded<ScriptJob>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true
    });

    private readonly SemaphoreSlim _slotFreed = new(0);
    /// <summary>How deep Run script steps may nest, so two scripts that run each other stop.</summary>
    public const int MaxScriptDepth = 3;

    private readonly OperationExecutor _executor;
    private readonly SecretProtector _secrets;
    private readonly SecretVault _vault;
    private readonly HistoryStore _history;
    private readonly ActivityLogger _activityLogger;
    private readonly ForEachExpander _expander;
    private readonly ILogger<ScriptRunner> _logger;
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();
    private int _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptRunner"/> class.
    /// </summary>
    /// <param name="executor">The step executor.</param>
    /// <param name="history">The run history.</param>
    /// <param name="activityLogger">The activity log writer.</param>
    /// <param name="secrets">Decrypts script secrets at run time.</param>
    /// <param name="vault">The stored secret values.</param>
    /// <param name="expander">Picks the items or users a script runs for each of.</param>
    /// <param name="logger">The logger.</param>
    public ScriptRunner(
        OperationExecutor executor,
        HistoryStore history,
        ActivityLogger activityLogger,
        SecretProtector secrets,
        SecretVault vault,
        ForEachExpander expander,
        ILogger<ScriptRunner> logger)
    {
        _expander = expander;
        _secrets = secrets;
        _vault = vault;
        _executor = executor;
        _history = history;
        _activityLogger = activityLogger;
        _logger = logger;
    }

    /// <summary>
    /// Queues a run. When the queue is full the run is dropped and logged, so a burst of events can never hold
    /// up the server.
    /// </summary>
    /// <param name="job">The run.</param>
    /// <returns>True when queued.</returns>
    public bool TryEnqueue(ScriptJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (_queue.Writer.TryWrite(job))
        {
            return true;
        }

        _logger.LogWarning("Scripts: the run queue is full, so \"{Script}\" was not run for {Trigger}", job.Script.Name, job.Context.Trigger);
        return false;
    }

    /// <summary>
    /// Runs a script now and records it in the history.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="context">The event.</param>
    /// <param name="isTest">Whether this is a run from the test button.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The run record.</returns>
    public Task<ScriptRun> RunAsync(ScriptDefinition script, EventContext context, bool isTest, CancellationToken cancellationToken)
        => RunAsync(script, context, isTest, 0, null, cancellationToken);

    private async Task<ScriptRun> RunAsync(ScriptDefinition script, EventContext context, bool isTest, int depth, IReadOnlyDictionary<string, string>? extra, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        var settings = Plugin.Instance?.ReadConfiguration(c => (c.DefaultTimeoutSeconds, c.HistorySize)) ?? (60, 200);
        var variables = new Dictionary<string, string>(context.Variables, StringComparer.Ordinal)
        {
            ["SCRIPT_NAME"] = script.Name
        };
        foreach (var (name, value) in extra ?? new Dictionary<string, string>())
        {
            variables[name] = value;
        }

        // Secrets are decrypted only for the run: they fill placeholders and the environment, but are left out
        // of EVENT_JSON and of the variables the history records.
        var secrets = script.Secrets.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ScriptSecrets.Reveal(script, _vault.Load(), _secrets);
        var withSecrets = new Dictionary<string, string>(variables, StringComparer.Ordinal);
        foreach (var (name, value) in secrets)
        {
            withSecrets[TriggerCatalog.SecretPrefix + name] = value;
        }

        var environment = withSecrets.ToDictionary(v => TriggerCatalog.EnvironmentPrefix + v.Key, v => v.Value, StringComparer.Ordinal);
        environment[TriggerCatalog.EnvironmentPrefix + "EVENT_JSON"] = JsonSerializer.Serialize(variables);

        // Values the event lacks still reach the script, empty, so a script under `set -u` can test them.
        foreach (var variable in TriggerCatalog.Get(context.Trigger).Variables)
        {
            environment.TryAdd(TriggerCatalog.EnvironmentPrefix + variable.Name, string.Empty);
        }

        var state = new RunState(script, context, withSecrets, environment, settings.DefaultTimeoutSeconds);
        var run = new ScriptRun
        {
            Id = Guid.NewGuid(),
            ScriptId = script.Id,
            ScriptName = script.Name,
            Trigger = context.Trigger,
            IsTest = isTest,
            StartedUtc = DateTime.UtcNow,
            Summary = context.Summary,
            Variables = variables
        };

        var stopwatch = Stopwatch.StartNew();
        string? skipRest = null;
        for (var index = 0; index < script.Operations.Count; index++)
        {
            var operation = script.Operations[index];
            if (skipRest is not null)
            {
                run.Operations.Add(new OperationResult { Type = operation.Type, Status = OperationStatus.Skipped, Message = skipRest });
                continue;
            }

            // A step's own condition sees everything so far, including what earlier steps saved. Not matching is
            // not a failure: the step is passed over and the run carries on.
            if (!ConditionEvaluator.MatchesStep(operation, withSecrets))
            {
                run.Operations.Add(new OperationResult
                {
                    Type = operation.Type,
                    Status = OperationStatus.Skipped,
                    Message = "Skipped: its condition on {{" + ConditionEvaluator.StepVariable(operation.OnlyIfVariable) + "}} did not match."
                });
                continue;
            }

            OperationResult result;
            try
            {
                result = operation.Type == OperationType.RunScript
                    ? await RunNestedAsync(operation, context, isTest, depth, cancellationToken).ConfigureAwait(false)
                    : await _executor.ExecuteAsync(operation, state, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new OperationResult { Status = OperationStatus.Failed, Message = ex.Message };
            }

            Capture(operation, result, variables, withSecrets, environment, secrets);

            // A secret that reached a message or a command's output is masked before the run is recorded.
            result.Type = operation.Type;
            result.Message = ScriptSecrets.Mask(result.Message, secrets);
            result.Output = ScriptSecrets.Mask(result.Output, secrets);
            run.Operations.Add(result);
            var number = (index + 1).ToString(CultureInfo.InvariantCulture);
            if (result.Status == OperationStatus.Failed && !operation.ContinueOnError)
            {
                skipRest = "Skipped because step " + number + " failed.";
            }
            else if (operation.Type == OperationType.StopScript)
            {
                skipRest = "Skipped because the script stopped at step " + number + ".";
            }
        }

        run.DurationMs = stopwatch.ElapsedMilliseconds;
        run.Succeeded = run.Operations.All(o => o.Status != OperationStatus.Failed);
        _history.Add(run, settings.HistorySize);

        // A nested run's failure is reported once, by the script that ran it.
        if (!run.Succeeded && !isTest && depth == 0)
        {
            var failure = run.Operations.First(o => o.Status == OperationStatus.Failed);
            _activityLogger.Log("Script \"" + script.Name + "\" failed", "Scripts.ScriptFailed", failure.Message, LogLevel.Warning);
        }

        return run;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                while (Volatile.Read(ref _running) >= MaxConcurrentRuns())
                {
                    await _slotFreed.WaitAsync(stoppingToken).ConfigureAwait(false);
                }

                Interlocked.Increment(ref _running);
                var run = RunTrackedAsync(job, stoppingToken);
                _inFlight.TryAdd(run, 0);
                _ = run.ContinueWith(done => _inFlight.TryRemove(done, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Server shutting down.
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Runs still finishing (a process being killed, a request timing out) complete before the runner is
        // disposed, so none of them touches it afterwards.
        try
        {
            await Task.WhenAll(_inFlight.Keys).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host stopped waiting.
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _slotFreed.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    // Saves a command's output or an HTTP response under the step's name, for the steps after it. The history
    // records the masked value; the steps and the environment get the real one.
    private static void Capture(
        ScriptOperation operation,
        OperationResult result,
        Dictionary<string, string> recorded,
        Dictionary<string, string> variables,
        Dictionary<string, string> environment,
        Dictionary<string, string> secrets)
    {
        var name = (operation.OutputName ?? string.Empty).Trim().ToUpperInvariant();
        if (name.Length == 0 || result.Captured is null)
        {
            return;
        }

        var found = OutputCapture.TryRead(result.Captured, operation.OutputJsonPath, out var value);
        Save(name, value);
        Save(name + "_STATUS", result.CapturedStatus ?? string.Empty);
        result.Message += found
            ? " Saved as {{" + name + "}}."
            : " Nothing was saved as {{" + name + "}}: the output has no " + operation.OutputJsonPath.Trim() + ".";

        void Save(string key, string text)
        {
            variables[key] = text;
            environment[TriggerCatalog.EnvironmentPrefix + key] = text;
            recorded[key] = ScriptSecrets.Mask(text, secrets);
        }
    }

    // Runs another script with the same event, in this run's slot, and reports its outcome as one step.
    private async Task<OperationResult> RunNestedAsync(ScriptOperation op, EventContext context, bool isTest, int depth, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(op.ScriptId, out var id))
        {
            return new OperationResult { Status = OperationStatus.Failed, Message = "No script is chosen." };
        }

        var target = Plugin.Instance?.ReadConfiguration(c => c.Scripts.FirstOrDefault(s => s.Id == id));
        if (target is null)
        {
            return new OperationResult { Status = OperationStatus.Failed, Message = "The chosen script no longer exists." };
        }

        if (!target.Enabled)
        {
            return new OperationResult { Status = OperationStatus.Skipped, Message = "Not run: \"" + target.Name + "\" is disabled. A script only other scripts run can be enabled with no triggers." };
        }

        if (depth + 1 >= MaxScriptDepth)
        {
            return new OperationResult { Status = OperationStatus.Failed, Message = "Not run: scripts may only run each other " + MaxScriptDepth.ToString(CultureInfo.InvariantCulture) + " levels deep." };
        }

        var nested = await RunAsync(target, context, isTest, depth + 1, null, cancellationToken).ConfigureAwait(false);
        var failed = nested.Operations.FirstOrDefault(o => o.Status == OperationStatus.Failed);
        return new OperationResult
        {
            Status = nested.Succeeded ? OperationStatus.Succeeded : OperationStatus.Failed,
            Message = "Ran \"" + target.Name + "\"" + (failed is null ? "." : ": " + failed.Message)
        };
    }

    private static int MaxConcurrentRuns()
        => Math.Max(1, Plugin.Instance?.ReadConfiguration(c => c.MaxConcurrentRuns) ?? 1);

    /// <summary>
    /// Runs a script for an event to the end: once, or once for each item or user it picks, then its failure
    /// script if any run failed. Every run is recorded in the history.
    /// </summary>
    /// <param name="job">The script and the event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Why the first failed run failed, or null when every run succeeded.</returns>
    public async Task<string?> RunJobAsync(ScriptJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        // Picking the items or users can take a while, so it never holds up the caller's own thread.
        await Task.Yield();
        var failures = new List<(ScriptRun Run, EventContext Context)>();
        var each = _expander.Expand(job.Script, job.Context);
        if (each is null)
        {
            Note(await RunAsync(job.Script, job.Context, false, cancellationToken).ConfigureAwait(false), job.Context);
        }
        else if (each.Count == 0)
        {
            RecordNothingMatched(job);
        }
        else
        {
            foreach (var context in each)
            {
                Note(await RunAsync(job.Script, context, false, cancellationToken).ConfigureAwait(false), context);
            }
        }

        if (failures.Count == 0)
        {
            return null;
        }

        await RunFailureScriptAsync(job, failures, cancellationToken).ConfigureAwait(false);
        var failed = failures[0].Run.Operations.FirstOrDefault(o => o.Status == OperationStatus.Failed);
        return "\"" + job.Script.Name + "\" failed" + (failed is null ? "." : ": " + failed.Message);

        void Note(ScriptRun run, EventContext context)
        {
            if (!run.Succeeded)
            {
                failures.Add((run, context));
            }
        }
    }

    private async Task RunTrackedAsync(ScriptJob job, CancellationToken stoppingToken)
    {
        try
        {
            await RunJobAsync(job, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Server shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scripts: \"{Script}\" could not run for {Trigger}", job.Script.Name, job.Context.Trigger);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
            _slotFreed.Release();
        }
    }

    // A run for each item or user that found none is still recorded, so the history shows the script came due.
    private void RecordNothingMatched(ScriptJob job)
    {
        var what = job.Script.ForEach == ForEachMode.Items ? "item" : "user";
        var run = new ScriptRun
        {
            Id = Guid.NewGuid(),
            ScriptId = job.Script.Id,
            ScriptName = job.Script.Name,
            Trigger = job.Context.Trigger,
            StartedUtc = DateTime.UtcNow,
            Succeeded = true,
            Summary = job.Context.Summary + " · no " + what + " matched",
            Variables = new Dictionary<string, string>(job.Context.Variables, StringComparer.Ordinal) { ["SCRIPT_NAME"] = job.Script.Name }
        };
        foreach (var operation in job.Script.Operations)
        {
            run.Operations.Add(new OperationResult { Type = operation.Type, Status = OperationStatus.Skipped, Message = "Not run: no " + what + " matched." });
        }

        var historySize = Plugin.Instance?.ReadConfiguration(c => c.HistorySize) ?? 200;
        _history.Add(run, historySize);
    }

    // Runs the script chosen to handle this one's failure, once per event however many of its runs failed. That
    // script's own failure is only logged, so two scripts naming each other can never loop.
    private async Task RunFailureScriptAsync(ScriptJob job, List<(ScriptRun Run, EventContext Context)> failures, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(job.Script.OnFailureScriptId, out var id) || id == job.Script.Id)
        {
            return;
        }

        var handler = Plugin.Instance?.ReadConfiguration(c => c.Scripts.FirstOrDefault(s => s.Id == id));
        if (handler is null || !handler.Enabled)
        {
            _logger.LogWarning("Scripts: \"{Script}\" failed, and the script chosen to handle that is missing or disabled", job.Script.Name);
            return;
        }

        var (first, failed) = failures[0];
        var step = first.Operations.FindIndex(o => o.Status == OperationStatus.Failed);
        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FAILED_SCRIPT_NAME"] = job.Script.Name,
            ["FAILED_STEP"] = (step + 1).ToString(CultureInfo.InvariantCulture),
            ["FAILED_MESSAGE"] = step >= 0 ? first.Operations[step].Message : string.Empty,
            ["FAILED_COUNT"] = failures.Count.ToString(CultureInfo.InvariantCulture)
        };

        // The first failed run's own event, so a failure for one item hands that item to the handler.
        var context = failed.Copy();
        context.Summary = "\"" + job.Script.Name + "\" failed · " + first.Summary;
        await RunAsync(handler, context, false, 1, extra, cancellationToken).ConfigureAwait(false);
    }
}
