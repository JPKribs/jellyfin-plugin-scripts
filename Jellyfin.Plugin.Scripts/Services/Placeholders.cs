using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Fills <c>{{NAME}}</c> placeholders in a step's text from the event's variables. <c>{{NAME:json}}</c>
/// escapes the value for use inside a JSON string and <c>{{NAME:url}}</c> percent-encodes it. An unknown
/// or missing variable becomes an empty string.
/// </summary>
public static partial class Placeholders
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Fills the placeholders in a template.
    /// </summary>
    /// <param name="template">The text to fill.</param>
    /// <param name="variables">The variables, without the <c>JF_</c> prefix.</param>
    /// <returns>The filled text.</returns>
    public static string Fill(string? template, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return Pattern().Replace(template, match =>
        {
            var name = Name(match);
            var value = variables.TryGetValue(name, out var found) ? found : string.Empty;
            return match.Groups[2].Value.ToUpperInvariant() switch
            {
                "JSON" => JsonEscape(value),
                "URL" => Uri.EscapeDataString(value),
                _ => value
            };
        });
    }

    /// <summary>
    /// Lists the variables a template uses, as <see cref="Fill"/> looks them up.
    /// </summary>
    /// <param name="template">The text.</param>
    /// <returns>The variable names, without the <c>JF_</c> prefix.</returns>
    public static IEnumerable<string> Names(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            yield break;
        }

        foreach (Match match in Pattern().Matches(template))
        {
            yield return Name(match);
        }
    }

    private static string Name(Match match)
    {
        var name = match.Groups[1].Value.ToUpperInvariant();
        return name.StartsWith(TriggerCatalog.EnvironmentPrefix, StringComparison.Ordinal) ? name[TriggerCatalog.EnvironmentPrefix.Length..] : name;
    }

    private static string JsonEscape(string value)
    {
        // Serialize as a JSON string and drop the quotes, so the placeholder sits inside the author's own.
        var quoted = JsonSerializer.Serialize(value, JsonOptions);
        return quoted[1..^1];
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*(?::\s*(json|url)\s*)?\}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
