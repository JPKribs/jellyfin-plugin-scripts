using System.Collections.Generic;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class PlaceholdersTests
{
    private static readonly Dictionary<string, string> Variables = new()
    {
        ["ITEM_NAME"] = "Tom & \"Jerry\" / Friends",
        ["USER_NAME"] = "alice",
    };

    [Fact]
    public void FillsNamesCaseInsensitivelyAndWithThePrefix()
    {
        Assert.Equal("alice alice alice", Placeholders.Fill("{{USER_NAME}} {{ user_name }} {{JF_USER_NAME}}", Variables));
    }

    [Fact]
    public void UnknownNamesBecomeEmpty()
    {
        Assert.Equal("[]", Placeholders.Fill("[{{NOPE}}]", Variables));
    }

    [Fact]
    public void JsonModeEscapesForAJsonString()
    {
        Assert.Equal("{\"t\":\"Tom & \\\"Jerry\\\" / Friends\"}", Placeholders.Fill("{\"t\":\"{{ITEM_NAME:json}}\"}", Variables));
    }

    [Fact]
    public void UrlModePercentEncodes()
    {
        Assert.Equal("?q=Tom%20%26%20%22Jerry%22%20%2F%20Friends", Placeholders.Fill("?q={{ITEM_NAME:url}}", Variables));
    }

    [Fact]
    public void LeavesOtherBracesAlone()
    {
        Assert.Equal("{ \"a\": 1 } {single}", Placeholders.Fill("{ \"a\": 1 } {single}", Variables));
    }
}
