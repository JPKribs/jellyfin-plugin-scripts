using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Reads the value a step saves from its output: the whole text, or one field of it when it is JSON.
/// </summary>
public static partial class OutputCapture
{
    /// <summary>The longest value a step may save, in characters.</summary>
    public const int MaxChars = 8 * 1024;

    /// <summary>
    /// Reads the value to save.
    /// </summary>
    /// <param name="output">The step's output.</param>
    /// <param name="jsonPath">The field to read, such as <c>data.items[0].name</c>, or empty for the whole output.</param>
    /// <param name="value">The value, trimmed and at most <see cref="MaxChars"/> long.</param>
    /// <returns>False when a field was asked for and the output is not JSON or has no such field.</returns>
    public static bool TryRead(string? output, string? jsonPath, out string value)
    {
        value = string.Empty;
        var text = (output ?? string.Empty).Trim();
        var path = (jsonPath ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            value = Shorten(text);
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var element = document.RootElement;
            foreach (var part in Parts(path))
            {
                if (part.Index is { } index)
                {
                    if (element.ValueKind != JsonValueKind.Array || index >= element.GetArrayLength())
                    {
                        return false;
                    }

                    element = element[index];
                }
                else if (element.ValueKind != JsonValueKind.Object || !TryProperty(element, part.Name, out element))
                {
                    return false;
                }
            }

            value = Shorten(element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => element.GetRawText()
            });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets whether a path is one <see cref="TryRead"/> can follow.
    /// </summary>
    /// <param name="jsonPath">The path.</param>
    /// <returns>True when it is empty or well formed.</returns>
    public static bool IsValidPath(string? jsonPath)
    {
        var path = (jsonPath ?? string.Empty).Trim();
        return path.Length == 0 || PathPattern().IsMatch(path);
    }

    private static string Shorten(string text) => text.Length > MaxChars ? text[..MaxChars] : text;

    // Property names match exactly first, then without regard to case, since services disagree on casing.
    private static bool TryProperty(JsonElement element, string name, out JsonElement found)
    {
        if (element.TryGetProperty(name, out found))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = property.Value;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<(string Name, int? Index)> Parts(string path)
    {
        foreach (Match match in PartPattern().Matches(path))
        {
            if (match.Groups[2].Success)
            {
                yield return (string.Empty, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            }
            else
            {
                yield return (match.Groups[1].Value, null);
            }
        }
    }

    [GeneratedRegex(@"^\$?\.?([^.\[\]\s]+|\[\d{1,6}\])(\.[^.\[\]\s]+|\[\d{1,6}\])*$", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"([^.\[\]\s$]+)|\[(\d{1,6})\]", RegexOptions.CultureInvariant)]
    private static partial Regex PartPattern();
}
