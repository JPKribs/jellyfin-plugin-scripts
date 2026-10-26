using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Matches an event against the configured scripts and queues the ones that should run.
/// </summary>
public class ScriptDispatcher
{
    // Entries older than the longest allowed cooldown are useless; past this many they are swept.
    private const int CooldownSweepThreshold = 10_000;

    private readonly ConcurrentDictionary<string, DateTime> _lastRuns = new(StringComparer.Ordinal);
    private readonly ScriptRunner _runner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptDispatcher"/> class.
    /// </summary>
    /// <param name="runner">The runner scripts are queued on.</param>
    public ScriptDispatcher(ScriptRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Gets whether any enabled script runs on a trigger, so a handler can skip building a context nobody reads.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <returns>True when a script listens.</returns>
    public static bool HasScriptsFor(ScriptTrigger trigger) => ScriptsFor(trigger).Any();

    /// <summary>
    /// Gets whether any enabled script runs on a trigger for an item of this kind.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="kind">The item's kind.</param>
    /// <returns>True when a script would accept the item.</returns>
    public static bool WantsItem(ScriptTrigger trigger, BaseItemKind kind)
        => ScriptsFor(trigger).Any(s => s.ItemKinds.Count == 0 || s.ItemKinds.Contains(kind));

    /// <summary>
    /// Queues every enabled script whose trigger, item kind filter, conditions, and cooldown allow the event.
    /// </summary>
    /// <param name="context">The event.</param>
    /// <returns>How many runs were queued.</returns>
    public int Dispatch(EventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var queued = 0;
        var now = DateTime.UtcNow;

        foreach (var script in ScriptsFor(context.Trigger))
        {
            if (!ConditionEvaluator.Matches(script, context) || !PassCooldown(script, context, now))
            {
                continue;
            }

            if (_runner.TryEnqueue(new ScriptJob(script, context)))
            {
                queued++;
            }
        }

        return queued;
    }

    private static IEnumerable<ScriptDefinition> ScriptsFor(ScriptTrigger trigger)
    {
        var scripts = Plugin.Instance?.ReadConfiguration(c => c.Scripts.ToList()) ?? new List<ScriptDefinition>();
        return scripts.Where(s => s.Enabled && s.Triggers.Contains(trigger));
    }

    private bool PassCooldown(ScriptDefinition script, EventContext context, DateTime now)
    {
        if (script.CooldownSeconds <= 0)
        {
            return true;
        }

        if (_lastRuns.Count > CooldownSweepThreshold)
        {
            var cutoff = now - TimeSpan.FromSeconds(ConfigurationValidator.MaxCooldownSeconds);
            foreach (var stale in _lastRuns.Where(p => p.Value < cutoff).Select(p => p.Key).ToList())
            {
                _lastRuns.TryRemove(stale, out _);
            }
        }

        var key = script.Id.ToString("N", CultureInfo.InvariantCulture) + "|" + context.Get("USER_ID") + "|" + context.Get("ITEM_ID");
        var window = TimeSpan.FromSeconds(script.CooldownSeconds);
        var allowed = true;
        _lastRuns.AddOrUpdate(
            key,
            now,
            (_, last) =>
            {
                if (now - last < window)
                {
                    allowed = false;
                    return last;
                }

                return now;
            });
        return allowed;
    }
}
