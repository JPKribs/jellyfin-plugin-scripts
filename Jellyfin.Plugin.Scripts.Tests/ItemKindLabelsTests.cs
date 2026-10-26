using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class ItemKindLabelsTests
{
    [Theory]
    [InlineData(BaseItemKind.BoxSet, "Collection")]
    [InlineData(BaseItemKind.AggregateFolder, "Aggregate Folder")]
    [InlineData(BaseItemKind.MusicVideo, "Music Video")]
    [InlineData(BaseItemKind.LiveTvChannel, "Live TV Channel")]
    [InlineData(BaseItemKind.Movie, "Movie")]
    public void ReadsLikeWords(BaseItemKind kind, string label)
    {
        Assert.Equal(label, ItemKindLabels.For(kind));
    }
}
