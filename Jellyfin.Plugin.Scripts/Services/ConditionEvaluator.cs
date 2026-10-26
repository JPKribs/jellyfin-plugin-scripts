using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Decides whether a script runs for an event: its item kind filter first, then its conditions.
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>How long one regular expression may run before the condition counts as not matching.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets whether a script runs for an event. The trigger itself is not checked here.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="context">The event.</param>
    /// <returns>True when the item kind filter and the conditions pass.</returns>
    public static bool Matches(ScriptDefinition script, EventContext context)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        if (!MatchesItemKind(script, context) || !MatchesUser(script, context))
        {
            return false;
        }

        if (script.Conditions.Count == 0)
        {
            return true;
        }

        return script.Match == ConditionMatch.Any
            ? script.Conditions.Any(c => Matches(c, context))
            : script.Conditions.All(c => Matches(c, context));
    }

    /// <summary>
    /// Gets whether an event passes a script's item kind filter. An empty filter passes everything, and an
    /// event whose trigger carries no item ignores the filter.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="context">The event.</param>
    /// <returns>True when the filter passes.</returns>
    public static bool MatchesItemKind(ScriptDefinition script, EventContext context)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        if (script.ItemKinds.Count == 0 || !TriggerCatalog.Get(context.Trigger).HasItem)
        {
            return true;
        }

        return context.ItemKind is { } kind && script.ItemKinds.Contains(kind);
    }

    /// <summary>
    /// Gets whether an event passes a script's users filter. An empty filter passes everything, and an event
    /// whose trigger carries no user ignores the filter. A failed sign in for a name that matches no user never
    /// passes a filter.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="context">The event.</param>
    /// <returns>True when the filter passes.</returns>
    public static bool MatchesUser(ScriptDefinition script, EventContext context)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);

        if (script.UserIds.Count == 0 || !TriggerCatalog.Get(context.Trigger).HasUser)
        {
            return true;
        }

        return context.UserIds.Any(script.UserIds.Contains);
    }

    /// <summary>
    /// Gets whether one condition matches an event. A field the trigger does not supply, or a value this event
    /// lacks, never matches, so under <see cref="ConditionMatch.All"/> such a condition keeps the script from
    /// running.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="context">The event.</param>
    /// <returns>True when the condition matches.</returns>
    public static bool Matches(ScriptCondition condition, EventContext context)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(context);

        if (!TriggerCatalog.Supplies(context.Trigger, condition.Field))
        {
            return false;
        }

        var values = condition.Values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToList();
        if (values.Count == 0)
        {
            return false;
        }

        // A value this event does not have never matches, for negative operators too: "Library is not Kids"
        // must not run for a removed item whose library can no longer be resolved.
        var candidates = context.CandidatesFor(condition.Field);
        if (candidates.Count == 0)
        {
            return false;
        }

        var any = candidates.Any(candidate => values.Any(value => Test(condition.Operator, candidate, value)));
        return condition.Operator is ConditionOperator.IsNot or ConditionOperator.DoesNotContain ? !any : any;
    }

    // Both sides must read as numbers; text such as "PG-13" never satisfies a numeric comparison.
    private static bool Numbers(string candidate, string value, out double left, out double right)
    {
        right = 0;
        return double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out left)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out right);
    }

    private static bool Test(ConditionOperator op, string candidate, string value)
    {
        switch (op)
        {
            case ConditionOperator.Is:
            case ConditionOperator.IsNot:
                return string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase);
            case ConditionOperator.Contains:
            case ConditionOperator.DoesNotContain:
                return candidate.Contains(value, StringComparison.OrdinalIgnoreCase);
            case ConditionOperator.StartsWith:
                return candidate.StartsWith(value, StringComparison.OrdinalIgnoreCase);
            case ConditionOperator.AtLeast:
                return Numbers(candidate, value, out var a, out var b) && a >= b;
            case ConditionOperator.AtMost:
                return Numbers(candidate, value, out var c, out var d) && c <= d;
            case ConditionOperator.MatchesRegex:
                try
                {
                    return Regex.IsMatch(candidate, value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                }
                catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
                {
                    return false;
                }

            default:
                return false;
        }
    }
}
