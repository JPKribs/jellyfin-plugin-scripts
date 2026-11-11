using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Gives every script on a schedule its own Jellyfin scheduled task, when the server starts and whenever the
/// scripts are saved, and clears away what the server keeps for a task once its script is deleted or no longer
/// scheduled.
/// </summary>
public sealed class ScriptScheduledTasks : IHostedService
{
    /// <summary>The namespace the task classes are made in. A task's ID is derived from its class's full name.</summary>
    public const string TypeNamespace = "Jellyfin.Plugin.Scripts.Tasks";

    private static readonly Lazy<ModuleBuilder> Module = new(() => AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("Jellyfin.Plugin.Scripts.Tasks"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Tasks"));

    private readonly ITaskManager _taskManager;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ScriptRunner _runner;
    private readonly EventContextBuilder _builder;
    private readonly ILogger<ScriptScheduledTasks> _logger;
    private readonly JsonFileStore<ScriptTaskState> _store;
    private readonly string _legacyScheduleFile;
    private readonly object _lock = new();
    private readonly HashSet<Guid> _added = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptScheduledTasks"/> class.
    /// </summary>
    /// <param name="taskManager">The server's task manager.</param>
    /// <param name="applicationPaths">The server's paths.</param>
    /// <param name="paths">The plugin's paths.</param>
    /// <param name="runner">Runs the scripts.</param>
    /// <param name="builder">Builds the schedule's event.</param>
    /// <param name="logger">The logger.</param>
    public ScriptScheduledTasks(
        ITaskManager taskManager,
        IApplicationPaths applicationPaths,
        ScriptPaths paths,
        ScriptRunner runner,
        EventContextBuilder builder,
        ILogger<ScriptScheduledTasks> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _taskManager = taskManager;
        _applicationPaths = applicationPaths;
        _runner = runner;
        _builder = builder;
        _logger = logger;
        _store = new JsonFileStore<ScriptTaskState>(paths.TasksFile, logger);
        _legacyScheduleFile = paths.ScheduleFile;
    }

    /// <summary>
    /// Gets the ID Jellyfin gives a script's task: the MD5 of the task class's full name, as the server computes it.
    /// </summary>
    /// <param name="scriptId">The script.</param>
    /// <returns>The task ID.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "Not security: this reproduces the ID Jellyfin derives for a task, to find the task's files.")]
    public static Guid TaskIdFor(Guid scriptId)
        => new(MD5.HashData(Encoding.Unicode.GetBytes(TypeNameFor(scriptId))));

    /// <summary>
    /// Gets the full name of the class made for a script's task.
    /// </summary>
    /// <param name="scriptId">The script.</param>
    /// <returns>The class name.</returns>
    public static string TypeNameFor(Guid scriptId)
        => TypeNamespace + ".Script_" + scriptId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the files Jellyfin keeps for a script's task: its triggers, once changed, and its last result.
    /// </summary>
    /// <param name="applicationPaths">The server's paths.</param>
    /// <param name="scriptId">The script.</param>
    /// <returns>The two paths.</returns>
    public static IReadOnlyList<string> TaskFiles(IApplicationPaths applicationPaths, Guid scriptId)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        var name = TaskIdFor(scriptId).ToString() + ".js";
        return new[]
        {
            Path.Combine(applicationPaths.ConfigurationDirectoryPath, "ScheduledTasks", name),
            Path.Combine(applicationPaths.DataPath, "ScheduledTasks", name)
        };
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Sync(startup: true);
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += OnConfigurationChanged;
        }

        // The plugin's own schedule clock is gone, and so is the file it kept its last runs in.
        TryDelete(_legacyScheduleFile);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a task for every scheduled script that lacks one, and removes the server's files for every script
    /// that had a task and is now deleted or no longer scheduled. A task itself cannot be taken back from the
    /// server while it runs, so it hides and disables itself until the next restart, when it is simply not added.
    /// </summary>
    /// <param name="startup">Whether the server is starting, so no task exists yet for a script that is gone.</param>
    public void Sync(bool startup = false)
    {
        lock (_lock)
        {
            try
            {
                var scheduled = (Plugin.Instance?.ReadConfiguration(c => c.Scripts
                        .Where(s => s.Triggers.Contains(ScriptTrigger.Schedule))
                        .Select(s => s.Id)
                        .ToList()) ?? new List<Guid>())
                    .ToHashSet();

                var fresh = scheduled.Where(id => _added.Add(id)).Select(Create).ToList();
                if (fresh.Count > 0)
                {
                    _taskManager.AddTasks(fresh);
                }

                // A task that is no longer wanted loses its files now. Its task object lives on, hidden, until the
                // server restarts, and running it through the API would write its result file again, so the script
                // stays on the retired list and its files are removed once more at the next start, when no task
                // for it exists any longer.
                var state = _store.Load();
                var retired = state.ScriptIds.Concat(state.RetiredIds).Where(id => !scheduled.Contains(id)).ToHashSet();
                foreach (var file in retired.SelectMany(id => TaskFiles(_applicationPaths, id)))
                {
                    TryDelete(file);
                }

                if (startup)
                {
                    retired.Clear();
                }

                if (!scheduled.SetEquals(state.ScriptIds) || !retired.SetEquals(state.RetiredIds))
                {
                    _store.Save(new ScriptTaskState
                    {
                        ScriptIds = scheduled.OrderBy(id => id).ToList(),
                        RetiredIds = retired.OrderBy(id => id).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scripts: could not update the scheduled tasks");
            }
        }
    }

    // Jellyfin identifies a task by its class, so each script gets a class of its own: an empty subclass of
    // ScriptScheduledTask named after the script's ID, which keeps the task's ID the same across restarts.
    private ScriptScheduledTask Create(Guid scriptId)
    {
        var name = TypeNameFor(scriptId);
        var type = Module.Value.GetType(name);
        if (type is null)
        {
            var builder = Module.Value.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(ScriptScheduledTask));
            builder.DefineDefaultConstructor(MethodAttributes.Public);
            type = builder.CreateType();
        }

        var task = (ScriptScheduledTask)Activator.CreateInstance(type)!;
        task.Initialize(scriptId, _runner, _builder);
        return task;
    }

    private void OnConfigurationChanged(object? sender, MediaBrowser.Model.Plugins.BasePluginConfiguration e) => Sync();

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Scripts: could not remove {Path}", path);
        }
    }
}
