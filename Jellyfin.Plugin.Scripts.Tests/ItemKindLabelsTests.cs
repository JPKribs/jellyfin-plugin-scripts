using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class ItemKindLabelsTests
{
    [Theory]
    [InlineData(BaseItemKind.BoxSet, "Collection")]
    [InlineData(BaseItemKind.AggregateFolder, "Aggregate folder")]
    [InlineData(BaseItemKind.MusicVideo, "Music video")]
    [InlineData(BaseItemKind.LiveTvChannel, "Live TV channel")]
    [InlineData(BaseItemKind.Movie, "Movie")]
    public void ReadsLikeWords(BaseItemKind kind, string label)
    {
        Assert.Equal(label, ItemKindLabels.For(kind));
    }
}
