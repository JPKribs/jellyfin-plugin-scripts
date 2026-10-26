using System;
using System.Linq;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class FavoriteStatesTests
{
    [Fact]
    public void OnlyAFlipCountsAsAChange()
    {
        var states = new FavoriteStates();
        var user = Guid.NewGuid();
        var item = Guid.NewGuid();

        Assert.True(states.Record(user, item, false)); // first sight
        Assert.False(states.Record(user, item, false)); // a like or dislike leaves it unchanged
        Assert.True(states.Record(user, item, true));
        Assert.True(states.Record(Guid.NewGuid(), item, true)); // another user's state is separate
    }
}

public class TaskChainsTests
{
    [Fact]
    public void AQueuedTaskIsKnownUntilItFinishes()
    {
        var key = "Task" + Guid.NewGuid().ToString("N");
        Assert.False(TaskChains.WasQueued(key));

        TaskChains.MarkQueued(key);
        Assert.True(TaskChains.WasQueued(key));
        Assert.True(TaskChains.Finished(key));

        Assert.False(TaskChains.WasQueued(key));
        Assert.False(TaskChains.Finished(key));
    }
}

public class PlaceholderNamesTests
{
    [Fact]
    public void ListsNamesAsFillLooksThemUp()
    {
        var names = Placeholders.Names("/scripts/{{ device_name }}/{{JF_ITEM_ID:url}}.sh").ToList();
        Assert.Equal(new[] { "DEVICE_NAME", "ITEM_ID" }, names);
    }
}
