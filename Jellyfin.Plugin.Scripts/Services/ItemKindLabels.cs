using System.Collections.Generic;
using Jellyfin.Data.Enums;
using JPKribs.Jellyfin.Base;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Readable names for item kinds. The stored value stays the <see cref="BaseItemKind"/> name, and this only changes
/// what the editor shows, such as "Collection" for BoxSet.
/// </summary>
public static class ItemKindLabels
{
    private static readonly Dictionary<BaseItemKind, string> Keys = new()
    {
        [BaseItemKind.AggregateFolder] = TranslationKeys.ItemKindAggregateFolder,
        [BaseItemKind.Audio] = TranslationKeys.Audio,
        [BaseItemKind.AudioBook] = TranslationKeys.ItemKindAudioBook,
        [BaseItemKind.BasePluginFolder] = TranslationKeys.ItemKindBasePluginFolder,
        [BaseItemKind.Book] = TranslationKeys.ItemKindBook,
        [BaseItemKind.BoxSet] = TranslationKeys.LabelCollection,
        [BaseItemKind.Channel] = TranslationKeys.ItemKindChannel,
        [BaseItemKind.ChannelFolderItem] = TranslationKeys.ItemKindChannelFolderItem,
        [BaseItemKind.CollectionFolder] = TranslationKeys.TasksLibraryCategory,
        [BaseItemKind.Episode] = TranslationKeys.Episode,
        [BaseItemKind.Folder] = TranslationKeys.LabelFolder,
        [BaseItemKind.Genre] = TranslationKeys.Genre,
        [BaseItemKind.ManualPlaylistsFolder] = TranslationKeys.ItemKindManualPlaylistsFolder,
        [BaseItemKind.Movie] = TranslationKeys.Movie,
        [BaseItemKind.LiveTvChannel] = TranslationKeys.ItemKindLiveTvChannel,
        [BaseItemKind.LiveTvProgram] = TranslationKeys.ItemKindLiveTvProgram,
        [BaseItemKind.MusicAlbum] = TranslationKeys.MusicAlbum,
        [BaseItemKind.MusicArtist] = TranslationKeys.MusicArtist,
        [BaseItemKind.MusicGenre] = TranslationKeys.ItemKindMusicGenre,
        [BaseItemKind.MusicVideo] = TranslationKeys.MusicVideo,
        [BaseItemKind.Person] = TranslationKeys.Person,
        [BaseItemKind.Photo] = TranslationKeys.Photo,
        [BaseItemKind.PhotoAlbum] = TranslationKeys.ItemKindPhotoAlbum,
        [BaseItemKind.Playlist] = TranslationKeys.LabelPlaylist,
        [BaseItemKind.PlaylistsFolder] = TranslationKeys.ItemKindManualPlaylistsFolder,
        [BaseItemKind.Program] = TranslationKeys.ItemKindProgram,
        [BaseItemKind.Recording] = TranslationKeys.ItemKindRecording,
        [BaseItemKind.Season] = TranslationKeys.Season,
        [BaseItemKind.Series] = TranslationKeys.Series,
        [BaseItemKind.Studio] = TranslationKeys.Studio,
        [BaseItemKind.Trailer] = TranslationKeys.Trailer,
        [BaseItemKind.TvChannel] = TranslationKeys.ItemKindTvChannel,
        [BaseItemKind.TvProgram] = TranslationKeys.ItemKindTvProgram,
        [BaseItemKind.UserRootFolder] = TranslationKeys.ItemKindUserRootFolder,
        [BaseItemKind.UserView] = TranslationKeys.ItemKindUserView,
        [BaseItemKind.Video] = TranslationKeys.Video,
        [BaseItemKind.Year] = TranslationKeys.LabelYear,
    };

    /// <summary>
    /// Gets the readable name of an item kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The name, such as "Music video" for MusicVideo, or the kind itself when it has no name.</returns>
    public static string For(BaseItemKind kind, string? language = null)
        => Keys.TryGetValue(kind, out var key) ? BaseTranslations.Get(key, language) : kind.ToString();
}
