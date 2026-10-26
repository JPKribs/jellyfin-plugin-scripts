using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

/// <summary>
/// Guards the dashboard pages: a page registered in <see cref="Plugin.GetPages"/> whose file is not
/// embedded loads as a blank page with no build error.
/// </summary>
public class EmbeddedResourceTests
{
    [Theory]
    [InlineData("scripts_scripts.html")]
    [InlineData("scripts_scripts.js")]
    [InlineData("scripts_history.html")]
    [InlineData("scripts_history.js")]
    [InlineData("scripts_settings.html")]
    [InlineData("scripts_settings.js")]
    [InlineData("scripts_common.js")]
    [InlineData("scripts_templates.js")]
    [InlineData("scripts_shared.css")]
    public void DashboardFileIsEmbedded(string file)
    {
        var names = typeof(Plugin).Assembly.GetManifestResourceNames();

        Assert.Contains("Jellyfin.Plugin.Scripts.Configuration." + file, names);
    }

    [Fact]
    public void SharedAssetsAreEmbedded()
    {
        var names = typeof(Plugin).Assembly.GetManifestResourceNames();

        Assert.Contains(names, n => n.EndsWith("jpkribs_shared.js", System.StringComparison.Ordinal));
        Assert.Contains(names, n => n.EndsWith("jpkribs_shared.css", System.StringComparison.Ordinal));
    }
}
