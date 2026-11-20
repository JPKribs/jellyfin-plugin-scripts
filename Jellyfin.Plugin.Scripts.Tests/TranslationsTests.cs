using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

/// <summary>
/// Guards the plugin's strings: every key a page or the server asks for has text, so no one reads a bare key.
/// </summary>
public class TranslationsTests
{
    private static readonly string PluginFolder = FindPluginFolder();

    /// <summary>
    /// Every key the pages use, in markup or script, is a local string or a declared Jellyfin string.
    /// True: each page shows text for every key it asks for.
    /// False: a page shows a bare key, such as ButtonAddStep, in place of its text.
    /// </summary>
    [Fact]
    public void EveryKeyThePagesUseExists()
    {
        var known = KnownKeys();
        var used = Directory.GetFiles(Path.Combine(PluginFolder, "Web"))
            .Where(f => f.EndsWith(".html", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal))
            .SelectMany(f => KeysIn(File.ReadAllText(f)).Select(k => (File: Path.GetFileName(f), Key: k)))
            .ToList();

        var missing = used.Where(u => !known.Contains(u.Key)).Select(u => u.File + ": " + u.Key).Distinct().ToList();

        Assert.NotEmpty(used);
        Assert.Empty(missing);
    }

    /// <summary>
    /// Every trigger, variable, and item kind the editor lists has text in US English.
    /// True: the catalog shows words, in every language the plugin has.
    /// False: the trigger list shows keys such as TriggerItemAdded.
    /// </summary>
    [Fact]
    public void EveryCatalogKeyHasText()
    {
        var keys = TriggerCatalog.All.SelectMany(t => new[] { t.LabelKey, t.CategoryKey, t.DescriptionKey }
                .Concat(t.Variables.Select(v => v.DescriptionKey)))
            .Concat(TriggerCatalog.FailureVariables.Select(v => v.DescriptionKey))
            .Distinct()
            .ToList();

        var strings = BaseTranslations.For(null);
        Assert.All(keys, key => Assert.True(strings.ContainsKey(key), key));
        Assert.All(Enum.GetValues<BaseItemKind>(), kind => Assert.False(string.IsNullOrWhiteSpace(ItemKindLabels.For(kind, "de"))));
    }

    /// <summary>
    /// A message the server answers with comes back in the language asked for, and in US English for one it lacks.
    /// True: a page in another language shows the server's messages in that language when they exist.
    /// False: every message would read as its key or in one fixed language.
    /// </summary>
    [Fact]
    public void ServerMessagesFallBackToUsEnglish()
    {
        Assert.Equal("Enter a path.", BaseTranslations.Get("PathEnter", "xx"));
        Assert.Equal("Movie", ItemKindLabels.For(BaseItemKind.Movie));
        Assert.NotEqual("Movie", ItemKindLabels.For(BaseItemKind.Movie, "de"));
    }

    private static IEnumerable<string> KeysIn(string text)
        => Regex.Matches(text, "data-str(?:-[a-z]+)?=\"(\\w+)\"").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(text, "\\b(?:tr|format)\\('(\\w+)'").Select(m => m.Groups[1].Value));

    private static HashSet<string> KnownKeys()
    {
        using var local = JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginFolder, "Localization", "en-us.json")));
        var keys = local.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var project = File.ReadAllText(Path.Combine(PluginFolder, "Jellyfin.Plugin.Scripts.csproj"));
        keys.UnionWith(Regex.Matches(project, "<JellyfinString Include=\"(\\w+)\"").Select(m => m.Groups[1].Value));
        return keys;
    }

    private static string FindPluginFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Jellyfin.Plugin.Scripts");
            if (File.Exists(Path.Combine(candidate, "Jellyfin.Plugin.Scripts.csproj")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The Jellyfin.Plugin.Scripts folder was not found above the test run.");
    }
}
