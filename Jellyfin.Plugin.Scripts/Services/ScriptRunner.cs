using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly OperationExecutor _executor;
    private readonly HistoryStore _history;
    private readonly ActivityLogger _activityLogger;
    private readonly ILogger<ScriptRunner> _logger;
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();
    private int _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptRunner"/> class.
    /// </summary>
    /// <param name="executor">The step executor.</param>
    /// <param name="history">The run history.</param>
    /// <param name="activityLogger">The activity log writer.</param>
    /// <param name="logger">The logger.</param>
    public ScriptRunner(
        OperationExecutor executor,
        HistoryStore history,
        ActivityLogger activityLogger,
        ILogger<ScriptRunner> logger)
    {
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
    public async Task<ScriptRun> RunAsync(ScriptDefinition script, EventContext context, bool isTest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        var settings = Plugin.Instance?.ReadConfiguration(c => (c.DefaultTimeoutSeconds, c.HistorySize)) ?? (60, 200);
        var variables = new Dictionary<string, string>(context.Variables, StringComparer.Ordinal)
        {
            ["SCRIPT_NAME"] = script.Name
        };

        var environment = variables.ToDictionary(v => TriggerCatalog.EnvironmentPrefix + v.Key, v => v.Value, StringComparer.Ordinal);
        environment[TriggerCatalog.EnvironmentPrefix + "EVENT_JSON"] = JsonSerializer.Serialize(variables);

        // Values the event lacks still reach the script, empty, so a script under `set -u` can test them.
        foreach (var variable in TriggerCatalog.Get(context.Trigger).Variables)
        {
            environment.TryAdd(TriggerCatalog.EnvironmentPrefix + variable.Name, string.Empty);
        }

        var state = new RunState(script, context, variables, environment, settings.DefaultTimeoutSeconds);
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
        var stopped = false;
        foreach (var operation in script.Operations)
        {
            if (stopped)
            {
                run.Operations.Add(new OperationResult
                {
                    Type = operation.Type,
                    Status = OperationStatus.Skipped,
                    Message = "Skipped because an earlier step failed."
                });
                continue;
            }

            OperationResult result;
            try
            {
                result = await _executor.ExecuteAsync(operation, state, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new OperationResult { Status = OperationStatus.Failed, Message = ex.Message };
            }

            result.Type = operation.Type;
            run.Operations.Add(result);
            stopped = result.Status == OperationStatus.Failed && !operation.ContinueOnError;
        }

        run.DurationMs = stopwatch.ElapsedMilliseconds;
        run.Succeeded = run.Operations.All(o => o.Status != OperationStatus.Failed);
        _history.Add(run, settings.HistorySize);

        if (!run.Succeeded && !isTest)
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

    private static int MaxConcurrentRuns()
        => Math.Max(1, Plugin.Instance?.ReadConfiguration(c => c.MaxConcurrentRuns) ?? 1);

    private async Task RunTrackedAsync(ScriptJob job, CancellationToken stoppingToken)
    {
        try
        {
            await RunAsync(job.Script, job.Context, false, stoppingToken).ConfigureAwait(false);
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
}
