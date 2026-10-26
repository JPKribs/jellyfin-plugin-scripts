using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Configuration;
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
    private static TriggerIndex? _index;
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
        SweepCooldowns(DateTime.UtcNow);
        return ScriptsFor(context.Trigger).Count(script => DispatchTo(context, script));
    }

    /// <summary>
    /// Gets the enabled scripts that run on a trigger. The scripts are indexed by trigger once per saved
    /// configuration, since busy events such as a library scan ask for every item.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <returns>The scripts.</returns>
    public static IEnumerable<ScriptDefinition> ScriptsFor(ScriptTrigger trigger)
    {
        var config = Plugin.Instance?.ReadConfiguration(c => c);
        if (config is null)
        {
            return Enumerable.Empty<ScriptDefinition>();
        }

        var index = _index;
        if (index is null || !ReferenceEquals(index.Config, config))
        {
            index = new TriggerIndex(config, config.Scripts
                .Where(s => s.Enabled)
                .SelectMany(s => s.Triggers.Distinct().Select(t => (Trigger: t, Script: s)))
                .ToLookup(p => p.Trigger, p => p.Script));
            _index = index;
        }

        return index.ByTrigger[trigger];
    }

    /// <summary>
    /// Queues one script for an event if its filters, conditions, and cooldown allow it. Used for every script
    /// <see cref="Dispatch"/> finds, and directly for events only one script's own threshold made due, such as
    /// a pause that has lasted long enough.
    /// </summary>
    /// <param name="context">The event.</param>
    /// <param name="script">The script.</param>
    /// <returns>True when queued.</returns>
    public bool DispatchTo(EventContext context, ScriptDefinition script)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(script);
        if (!ConditionEvaluator.Matches(script, context))
        {
            return false;
        }

        var claim = ClaimCooldown(script, context, DateTime.UtcNow);
        if (claim is { Allowed: false })
        {
            return false;
        }

        if (_runner.TryEnqueue(new ScriptJob(script, context)))
        {
            return true;
        }

        // The run never happened, so it must not hold off the next event for the same user and item.
        if (claim is { } dropped)
        {
            _cooldowns.TryRemove(new KeyValuePair<string, DateTime>(dropped.Key, dropped.Until));
        }

        return false;
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

    private sealed record TriggerIndex(PluginConfiguration Config, ILookup<ScriptTrigger, ScriptDefinition> ByTrigger);
}
