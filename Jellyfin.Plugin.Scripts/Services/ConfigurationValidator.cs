using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Scripts.Configuration;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Normalizes and validates incoming plugin configuration before it is persisted. The dashboard enforces the
/// same rules in the browser, but configuration can arrive from any API client, so they are enforced server
/// side as well.
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>The longest a command may run, in seconds.</summary>
    public const int MaxTimeoutSeconds = 3600;

    /// <summary>The longest cooldown, in seconds (one day).</summary>
    public const int MaxCooldownSeconds = 86_400;

    /// <summary>The most runs that may execute at once.</summary>
    public const int MaxConcurrentRuns = 32;

    /// <summary>The most runs the history may keep.</summary>
    public const int MaxHistorySize = 5000;

    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "PATCH", "DELETE" };

    /// <summary>
    /// Fills in what a client may leave out: new scripts get an ID, names and values are trimmed, and
    /// duplicate triggers and kinds are dropped.
    /// </summary>
    /// <param name="config">The incoming configuration.</param>
    public static void Normalize(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Scripts ??= new List<ScriptDefinition>();
        foreach (var script in config.Scripts)
        {
            Normalize(script);
        }
    }

    /// <summary>
    /// Fills in what a client may leave out of one script.
    /// </summary>
    /// <param name="script">The script.</param>
    public static void Normalize(ScriptDefinition script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Id == Guid.Empty)
        {
            script.Id = Guid.NewGuid();
        }

        script.Name = (script.Name ?? string.Empty).Trim();
        script.Triggers = (script.Triggers ?? new()).Distinct().ToList();
        script.ItemKinds = (script.ItemKinds ?? new()).Distinct().ToList();
        script.Conditions ??= new List<ScriptCondition>();
        script.Operations ??= new List<ScriptOperation>();
        foreach (var condition in script.Conditions)
        {
            condition.Values = (condition.Values ?? new())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// Validates a configuration, throwing when it must not be persisted.
    /// </summary>
    /// <param name="config">The incoming configuration.</param>
    /// <exception cref="ArgumentException">When a setting or a script is invalid.</exception>
    public static void Validate(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Range(config.MaxConcurrentRuns, 1, MaxConcurrentRuns, "Concurrent runs");
        Range(config.DefaultTimeoutSeconds, 1, MaxTimeoutSeconds, "Default timeout");
        Range(config.HistorySize, 0, MaxHistorySize, "History size");

        var ids = new HashSet<Guid>();
        foreach (var script in config.Scripts)
        {
            if (!ids.Add(script.Id))
            {
                throw new ArgumentException("Two scripts share the ID " + script.Id.ToString("N", CultureInfo.InvariantCulture) + ".");
            }

            Validate(script);
        }
    }

    /// <summary>
    /// Validates one script, throwing when it must not be saved or run.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <exception cref="ArgumentException">When the script is invalid.</exception>
    public static void Validate(ScriptDefinition script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Name.Length == 0)
        {
            throw new ArgumentException("Every script needs a name.");
        }

        var label = "Script \"" + script.Name + "\": ";
        if (script.Name.Length > 200)
        {
            throw new ArgumentException(label + "the name is longer than 200 characters.");
        }

        if (script.Triggers.Any(t => !Enum.IsDefined(t)))
        {
            throw new ArgumentException(label + "has an unknown trigger.");
        }

        if (script.ItemKinds.Any(k => !Enum.IsDefined(k)))
        {
            throw new ArgumentException(label + "has an unknown item kind.");
        }

        if (!Enum.IsDefined(script.Match))
        {
            throw new ArgumentException(label + "has an unknown condition match.");
        }

        Range(script.CooldownSeconds, 0, MaxCooldownSeconds, label + "cooldown");

        foreach (var condition in script.Conditions)
        {
            ValidateCondition(condition, label);
        }

        for (var i = 0; i < script.Operations.Count; i++)
        {
            ValidateOperation(script.Operations[i], label + "step " + (i + 1).ToString(CultureInfo.InvariantCulture) + " ");
        }
    }

    [SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Administrators only, and every pattern runs with a timeout.")]
    private static void ValidateCondition(ScriptCondition condition, string label)
    {
        if (!Enum.IsDefined(condition.Field) || !Enum.IsDefined(condition.Operator))
        {
            throw new ArgumentException(label + "has an unknown condition.");
        }

        if (condition.Values.Count == 0)
        {
            throw new ArgumentException(label + "the " + condition.Field + " condition has no value.");
        }

        if (condition.Operator != ConditionOperator.MatchesRegex)
        {
            return;
        }

        foreach (var pattern in condition.Values)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.None, ConditionEvaluator.RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(label + "the pattern \"" + pattern + "\" is not a valid regular expression: " + ex.Message, ex);
            }
        }
    }

    private static void ValidateOperation(ScriptOperation op, string label)
    {
        switch (op.Type)
        {
            case OperationType.RunCommand:
                if (string.IsNullOrWhiteSpace(op.Command))
                {
                    throw new ArgumentException(label + "needs a command.");
                }

                Range(op.TimeoutSeconds, 0, MaxTimeoutSeconds, label + "timeout");
                break;
            case OperationType.HttpRequest:
                var url = (op.Url ?? string.Empty).Trim();
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(label + "needs a URL that starts with http:// or https://.");
                }

                if (!HttpMethods.Contains(op.Method ?? string.Empty))
                {
                    throw new ArgumentException(label + "has an unsupported HTTP method.");
                }

                break;
            case OperationType.SendMessage:
                if (string.IsNullOrWhiteSpace(op.Text))
                {
                    throw new ArgumentException(label + "needs message text.");
                }

                Range(op.DisplaySeconds, 0, 300, label + "display time");
                break;
            case OperationType.StopPlayback:
                Range(op.DisplaySeconds, 0, 300, label + "display time");
                break;
            case OperationType.RunScheduledTask:
                if (string.IsNullOrWhiteSpace(op.TaskKey))
                {
                    throw new ArgumentException(label + "needs a scheduled task.");
                }

                break;
            case OperationType.Wait:
                Range(op.WaitSeconds, 1, OperationExecutor.MaxWaitSeconds, label + "wait");
                break;
            case OperationType.RefreshMetadata:
            case OperationType.WriteActivityLog:
                break;
            default:
                throw new ArgumentException(label + "has an unknown type.");
        }
    }

    private static void Range(int value, int min, int max, string what)
    {
        if (value < min || value > max)
        {
            throw new ArgumentException(
                what + " must be between " + min.ToString(CultureInfo.InvariantCulture) + " and " + max.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }
}
