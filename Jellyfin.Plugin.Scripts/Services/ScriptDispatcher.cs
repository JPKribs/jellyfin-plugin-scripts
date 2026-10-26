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
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    // When each script's cooldown for a user and item ends.
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new(StringComparer.Ordinal);
    private readonly ScriptRunner _runner;
    private DateTime _lastSweep = DateTime.MinValue;

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
    /// Queues every enabled script whose trigger, item kind and users filters, conditions, and cooldown allow the
    /// event.
    /// </summary>
    /// <param name="context">The event.</param>
    /// <returns>How many runs were queued.</returns>
    public int Dispatch(EventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var queued = 0;
        var now = DateTime.UtcNow;
        SweepCooldowns(now);

        foreach (var script in ScriptsFor(context.Trigger))
        {
            if (!ConditionEvaluator.Matches(script, context))
            {
                continue;
            }

            var claim = ClaimCooldown(script, context, now);
            if (claim is { Allowed: false })
            {
                continue;
            }

            if (_runner.TryEnqueue(new ScriptJob(script, context)))
            {
                queued++;
            }
            else if (claim is { } dropped)
            {
                // The run never happened, so it must not hold off the next event for the same user and item.
                _cooldowns.TryRemove(new KeyValuePair<string, DateTime>(dropped.Key, dropped.Until));
            }
        }

        return queued;
    }

    private static IEnumerable<ScriptDefinition> ScriptsFor(ScriptTrigger trigger)
    {
        var scripts = Plugin.Instance?.ReadConfiguration(c => c.Scripts.ToList()) ?? new List<ScriptDefinition>();
        return scripts.Where(s => s.Enabled && s.Triggers.Contains(trigger));
    }

    // Claims the cooldown window atomically, so two events arriving together cannot both run. Returns null when
    // the script has no cooldown.
    private (bool Allowed, string Key, DateTime Until)? ClaimCooldown(ScriptDefinition script, EventContext context, DateTime now)
    {
        if (script.CooldownSeconds <= 0)
        {
            return null;
        }

        // Per trigger, so a start held off by its cooldown never swallows the stop that follows it.
        var key = script.Id.ToString("N", CultureInfo.InvariantCulture) + "|" + context.Trigger + "|" + context.Get("USER_ID") + "|" + context.Get("ITEM_ID");
        var until = now + TimeSpan.FromSeconds(script.CooldownSeconds);
        var allowed = true;
        var stored = _cooldowns.AddOrUpdate(
            key,
            until,
            (_, existing) =>
            {
                if (existing > now)
                {
                    allowed = false;
                    return existing;
                }

                return until;
            });
        return (allowed, key, stored);
    }

    // Expired windows are dropped at most once a minute, so a library scan with a cooldown never rescans the
    // whole table on every item.
    private void SweepCooldowns(DateTime now)
    {
        if (now - _lastSweep < SweepInterval || _cooldowns.IsEmpty)
        {
            return;
        }

        _lastSweep = now;
        foreach (var expired in _cooldowns.Where(p => p.Value <= now).ToList())
        {
            _cooldowns.TryRemove(expired);
        }
    }
}
