using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Readable names for item kinds. The stored value stays the <see cref="BaseItemKind"/> name; this only changes
/// what the editor shows, such as "Collection" for BoxSet.
/// </summary>
public static partial class ItemKindLabels
{
    private static readonly Dictionary<BaseItemKind, string> Overrides = new()
    {
        [BaseItemKind.BoxSet] = "Collection",
        [BaseItemKind.AudioBook] = "Audiobook",
        [BaseItemKind.CollectionFolder] = "Library",
        [BaseItemKind.TvChannel] = "TV Channel",
        [BaseItemKind.TvProgram] = "TV Program",
        [BaseItemKind.LiveTvChannel] = "Live TV Channel",
        [BaseItemKind.LiveTvProgram] = "Live TV Program",
        [BaseItemKind.ManualPlaylistsFolder] = "Playlists Folder",
        [BaseItemKind.BasePluginFolder] = "Plugin Folder",
    };

    /// <summary>
    /// Gets the readable name of an item kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name, such as "Music Video" for MusicVideo.</returns>
    public static string For(BaseItemKind kind)
        => Overrides.TryGetValue(kind, out var label) ? label : WordBoundary().Replace(kind.ToString(), " ");

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundary();
}
