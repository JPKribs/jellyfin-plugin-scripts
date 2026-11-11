using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Turns a webhook request into variables: the body as sent, and each query parameter and JSON field under its
/// own name, so a step can test or use one without parsing anything.
/// </summary>
public static class WebhookVariables
{
    /// <summary>The largest request body accepted, in bytes.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>The most variables read from one request.</summary>
    public const int MaxVariables = 60;

    /// <summary>The longest value kept for one field, in characters.</summary>
    public const int MaxValueChars = 2048;

    /// <summary>The longest body kept as WEBHOOK_BODY, in characters.</summary>
    public const int MaxBodyChars = 16 * 1024;

    private const int MaxNameChars = 64;
    private const int MaxDepth = 4;
    private const int MaxArrayItems = 5;

    /// <summary>
    /// Reads a request's variables.
    /// </summary>
    /// <param name="body">The request body, which may be empty or not JSON.</param>
    /// <param name="query">The query parameters.</param>
    /// <returns>The variables without the JF_ prefix, each named WEBHOOK_ and then the field.</returns>
    public static Dictionary<string, string> Read(string? body, IEnumerable<KeyValuePair<string, string>>? query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = body ?? string.Empty;
        if (text.Length > 0)
        {
            values[TriggerCatalog.WebhookPrefix + "BODY"] = text.Length > MaxBodyChars ? text[..MaxBodyChars] : text;
        }

        foreach (var (name, value) in query ?? Array.Empty<KeyValuePair<string, string>>())
        {
            Add(values, name, value);
        }

        if (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                Flatten(values, string.Empty, document.RootElement, 0);
            }
            catch (JsonException)
            {
                // Not JSON after all. The body is still there as it was sent.
            }
        }

        return values;
    }

    private static void Flatten(Dictionary<string, string> values, string prefix, JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when depth < MaxDepth:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(values, Join(prefix, property.Name), property.Value, depth + 1);
                }

                break;
            case JsonValueKind.Array when depth < MaxDepth:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index >= MaxArrayItems)
                    {
                        break;
                    }

                    Flatten(values, Join(prefix, index.ToString(CultureInfo.InvariantCulture)), item, depth + 1);
                    index++;
                }

                break;
            case JsonValueKind.String:
                Add(values, prefix, element.GetString());
                break;
            case JsonValueKind.Number:
                Add(values, prefix, element.GetRawText());
                break;
            case JsonValueKind.True:
                Add(values, prefix, "true");
                break;
            case JsonValueKind.False:
                Add(values, prefix, "false");
                break;
            default:
                break;
        }
    }

    private static string Join(string prefix, string name) => prefix.Length == 0 ? name : prefix + "_" + name;

    // Names keep only letters, digits, and underscores, so each is a valid placeholder and environment variable.
    private static void Add(Dictionary<string, string> values, string name, string? value)
    {
        if (string.IsNullOrEmpty(value) || values.Count >= MaxVariables)
        {
            return;
        }

        var clean = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (clean.Length >= MaxNameChars)
            {
                break;
            }

            clean.Append(char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }

        if (clean.Length == 0)
        {
            return;
        }

        var key = TriggerCatalog.WebhookPrefix + clean;
        if (key != TriggerCatalog.WebhookPrefix + "BODY")
        {
            values.TryAdd(key, value.Length > MaxValueChars ? value[..MaxValueChars] : value);
        }
    }
}
