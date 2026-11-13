using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Scripts.Configuration;
using Jellyfin.Plugin.Scripts.Models;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Normalizes and validates incoming plugin configuration before it is persisted. The dashboard enforces the
/// same rules in the browser, but configuration can arrive from any API client, so they are enforced server
/// side as well.
/// </summary>
public static partial class ConfigurationValidator
{
    /// <summary>The longest a command may run, in seconds.</summary>
    public const int MaxTimeoutSeconds = 3600;

    /// <summary>The longest cooldown, in seconds (one day).</summary>
    public const int MaxCooldownSeconds = 86_400;

    /// <summary>The longest a "for a while" playback trigger may wait, in minutes (one day).</summary>
    public const int MaxDurationMinutes = 1440;

    /// <summary>The longest schedule interval, in minutes (one week).</summary>
    public const int MaxIntervalMinutes = 10_080;

    /// <summary>The most runs that may execute at once.</summary>
    public const int MaxConcurrentRuns = 32;

    /// <summary>The most runs the history may keep.</summary>
    public const int MaxHistorySize = 5000;

    /// <summary>The most items or users one event may run a script for.</summary>
    public const int MaxForEachLimit = 500;

    /// <summary>The most streams a stream or transcode count trigger may wait for.</summary>
    public const int MaxStreamCount = 100;

    /// <summary>How many random bytes a webhook token is made from.</summary>
    public const int WebhookTokenBytes = 24;

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
        script.Description = (script.Description ?? string.Empty).Trim();

        // Run now is how an administrator starts any script, never a trigger one listens for.
        script.Triggers = (script.Triggers ?? new()).Where(t => t != ScriptTrigger.Manual).Distinct().ToList();
        script.OnFailureScriptId = (script.OnFailureScriptId ?? string.Empty).Trim();
        script.WebhookToken = (script.WebhookToken ?? string.Empty).Trim();
        if (script.WebhookToken.Length == 0 && script.Triggers.Contains(ScriptTrigger.Webhook))
        {
            script.WebhookToken = NewWebhookToken();
        }

        script.ItemKinds = (script.ItemKinds ?? new()).Distinct().ToList();
        script.UserIds = (script.UserIds ?? new()).Where(id => id != Guid.Empty).Distinct().ToList();
        script.ScheduleDays = (script.ScheduleDays ?? new()).Distinct().ToList();
        script.ScheduleTime = (script.ScheduleTime ?? string.Empty).Trim();
        script.Secrets ??= new List<ScriptSecret>();
        foreach (var secret in script.Secrets)
        {
            secret.Name = (secret.Name ?? string.Empty).Trim().ToUpperInvariant();
            secret.Value ??= string.Empty;
        }

        script.Conditions ??= new List<ScriptCondition>();
        script.Operations ??= new List<ScriptOperation>();
        foreach (var operation in script.Operations)
        {
            operation.OutputName = (operation.OutputName ?? string.Empty).Trim().ToUpperInvariant();
            operation.OutputJsonPath = (operation.OutputJsonPath ?? string.Empty).Trim();
            operation.OnlyIfVariable = ConditionEvaluator.StepVariable(operation.OnlyIfVariable);
            operation.OnlyIfValue ??= string.Empty;
        }

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
    /// Makes a webhook token: random, and long enough that it cannot be guessed.
    /// </summary>
    /// <returns>The token, in lowercase hexadecimal.</returns>
    public static string NewWebhookToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(WebhookTokenBytes)).ToLowerInvariant();

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
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var script in config.Scripts)
        {
            if (!ids.Add(script.Id))
            {
                throw new ArgumentException("Two scripts share the ID " + script.Id.ToString("N", CultureInfo.InvariantCulture) + ".");
            }

            if (script.WebhookToken.Length > 0 && !tokens.Add(script.WebhookToken))
            {
                throw new ArgumentException("Script \"" + script.Name + "\": another script already uses its webhook address. Make a new one.");
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

        if (script.Description.Length > 500)
        {
            throw new ArgumentException(label + "the description is longer than 500 characters.");
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

        if (script.Triggers.Contains(ScriptTrigger.PlaybackPausedFor))
        {
            Range(script.PausedForMinutes, 1, MaxDurationMinutes, label + "paused for minutes");
        }

        if (script.Triggers.Contains(ScriptTrigger.PlaybackPlayingFor))
        {
            Range(script.PlayingForMinutes, 1, MaxDurationMinutes, label + "playing for minutes");
        }

        if (script.Triggers.Contains(ScriptTrigger.Schedule))
        {
            if (!Enum.IsDefined(script.ScheduleMode))
            {
                throw new ArgumentException(label + "has an unknown schedule mode.");
            }

            if (script.ScheduleMode == ScheduleMode.Interval)
            {
                Range(script.ScheduleIntervalMinutes, 1, MaxIntervalMinutes, label + "schedule interval");
            }
            else if (!ScheduleTimes.TryParse(script.ScheduleTime, out _))
            {
                throw new ArgumentException(label + "the schedule time must be HH:mm, such as 03:00.");
            }

            if (script.ScheduleMode == ScheduleMode.Monthly)
            {
                Range(script.ScheduleMonthDay, 1, 31, label + "schedule day of the month");
            }

            if (script.ScheduleDays.Any(d => !Enum.IsDefined(d)))
            {
                throw new ArgumentException(label + "has an unknown schedule day.");
            }
        }

        if (script.Triggers.Contains(ScriptTrigger.StreamCountReached))
        {
            Range(script.StreamCountThreshold, 1, MaxStreamCount, label + "stream count");
        }

        if (script.Triggers.Contains(ScriptTrigger.TranscodeCountReached))
        {
            Range(script.TranscodeCountThreshold, 1, MaxStreamCount, label + "transcode count");
        }

        if (script.Triggers.Contains(ScriptTrigger.PlaybackProgressReached))
        {
            Range(script.ProgressPercentThreshold, 1, 99, label + "playback percentage");
        }

        if (script.Triggers.Contains(ScriptTrigger.Webhook) && !WebhookToken().IsMatch(script.WebhookToken))
        {
            throw new ArgumentException(label + "the webhook token must be 32 to 64 hexadecimal characters. Make a new address.");
        }

        if (!Enum.IsDefined(script.ForEach))
        {
            throw new ArgumentException(label + "has an unknown run-for-each setting.");
        }

        if (script.ForEach != ForEachMode.None)
        {
            Range(script.ForEachLimit, 1, MaxForEachLimit, label + "run-for-each limit");
        }

        // Without a kind, "every item" would also mean every person, genre, studio, and folder.
        if (script.ForEach == ForEachMode.Items && script.ItemKinds.Count == 0)
        {
            throw new ArgumentException(label + "running for each item needs at least one item kind.");
        }

        if (script.OnFailureScriptId.Length > 0)
        {
            if (!Guid.TryParse(script.OnFailureScriptId, out var handler))
            {
                throw new ArgumentException(label + "the script to run on failure is not valid.");
            }

            if (handler == script.Id)
            {
                throw new ArgumentException(label + "cannot be its own script to run on failure.");
            }
        }

        foreach (var condition in script.Conditions)
        {
            ValidateCondition(condition, label);
        }

        var secretNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var secret in script.Secrets)
        {
            if (!SecretName().IsMatch(secret.Name))
            {
                throw new ArgumentException(label + "the secret name \"" + secret.Name + "\" must be capital letters, digits, and underscores, up to 40.");
            }

            if (!secretNames.Add(secret.Name))
            {
                throw new ArgumentException(label + "has two secrets named " + secret.Name + ".");
            }
        }

        for (var i = 0; i < script.Operations.Count; i++)
        {
            ValidateOperation(script.Operations[i], script.Id, label + "step " + (i + 1).ToString(CultureInfo.InvariantCulture) + " ");
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

        if (condition.Operator is ConditionOperator.AtLeast or ConditionOperator.AtMost)
        {
            foreach (var number in condition.Values)
            {
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    throw new ArgumentException(label + "\"" + number + "\" is not a number, which " + condition.Operator + " needs.");
                }
            }

            return;
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

    [SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Administrators only, and every pattern runs with a timeout.")]
    private static void ValidateStepExtras(ScriptOperation op, string label)
    {
        if (op.OutputName.Length > 0)
        {
            if (op.Type is not (OperationType.RunCommand or OperationType.HttpRequest))
            {
                throw new ArgumentException(label + "cannot save an output: only commands and HTTP requests have one.");
            }

            if (!SecretName().IsMatch(op.OutputName))
            {
                throw new ArgumentException(label + "the name its output is saved as must be capital letters, digits, and underscores, up to 40.");
            }

            if (TriggerCatalog.IsReserved(op.OutputName) || TriggerCatalog.IsReserved(op.OutputName + "_STATUS") || op.OutputName is "SCRIPT_NAME" or "EVENT_JSON")
            {
                throw new ArgumentException(label + "cannot save its output as " + op.OutputName + ", which the event already uses.");
            }

            if (!OutputCapture.IsValidPath(op.OutputJsonPath))
            {
                throw new ArgumentException(label + "the JSON field \"" + op.OutputJsonPath + "\" is not a path such as data.items[0].name.");
            }
        }

        if (op.OnlyIfVariable.Length == 0)
        {
            return;
        }

        if (!StepVariableName().IsMatch(op.OnlyIfVariable))
        {
            throw new ArgumentException(label + "its condition needs a variable name, such as ITEM_KIND.");
        }

        if (!Enum.IsDefined(op.OnlyIfOperator))
        {
            throw new ArgumentException(label + "has an unknown condition.");
        }

        // A value holding a placeholder is only known at run time.
        var value = op.OnlyIfValue.Trim();
        if (Placeholders.Names(value).Any())
        {
            return;
        }

        if (op.OnlyIfOperator is ConditionOperator.AtLeast or ConditionOperator.AtMost
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException(label + "its condition needs a number.");
        }

        if (op.OnlyIfOperator == ConditionOperator.MatchesRegex)
        {
            try
            {
                _ = new Regex(value, RegexOptions.None, ConditionEvaluator.RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(label + "its condition's pattern is not a valid regular expression: " + ex.Message, ex);
            }
        }
    }

    private static void ValidateOperation(ScriptOperation op, Guid scriptId, string label)
    {
        ValidateStepExtras(op, label);
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

                if (!string.IsNullOrWhiteSpace(op.ContentType) && !MediaTypeHeaderValue.TryParse(op.ContentType, out _))
                {
                    throw new ArgumentException(label + "has an invalid content type.");
                }

                Range(op.Retries, 0, OperationExecutor.MaxRetries, label + "retries");

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

                if (!Enum.IsDefined(op.Target))
                {
                    throw new ArgumentException(label + "has an unknown message target.");
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
            case OperationType.AddTag:
            case OperationType.RemoveTag:
                if (string.IsNullOrWhiteSpace(op.Tag))
                {
                    throw new ArgumentException(label + "needs a tag.");
                }

                break;
            case OperationType.AddToCollection:
            case OperationType.RemoveFromCollection:
                if (!Guid.TryParse(op.CollectionId, out _))
                {
                    throw new ArgumentException(label + "needs a collection.");
                }

                break;
            case OperationType.RunScript:
                if (!Guid.TryParse(op.ScriptId, out var target))
                {
                    throw new ArgumentException(label + "needs a script to run.");
                }

                if (target == scriptId)
                {
                    throw new ArgumentException(label + "cannot run its own script.");
                }

                break;
            case OperationType.AppendToFile:
                if (string.IsNullOrWhiteSpace(op.FilePath) || !Path.IsPathRooted(op.FilePath.Trim()))
                {
                    throw new ArgumentException(label + "needs a full file path.");
                }

                break;
            case OperationType.RefreshMetadata:
            case OperationType.WriteActivityLog:
            case OperationType.PausePlayback:
            case OperationType.MarkPlayed:
            case OperationType.MarkUnplayed:
            case OperationType.SetFavorite:
            case OperationType.SetUserDisabled:
            case OperationType.SignOutUser:
            case OperationType.ScanLibraries:
            case OperationType.StopScript:
                break;
            default:
                throw new ArgumentException(label + "has an unknown type.");
        }
    }

    [GeneratedRegex("^[A-Z0-9_]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();

    [GeneratedRegex("^[A-Z0-9_]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex StepVariableName();

    [GeneratedRegex("^[A-Fa-f0-9]{32,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookToken();

    private static void Range(int value, int min, int max, string what)
    {
        if (value < min || value > max)
        {
            throw new ArgumentException(
                what + " must be between " + min.ToString(CultureInfo.InvariantCulture) + " and " + max.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }
}
