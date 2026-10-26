using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Remembers whether each user last had each item as a favorite. Jellyfin saves a favorite and a like or dislike
/// with the same reason, so Favorite changed fires only when the favorite itself flips.
/// </summary>
public sealed class FavoriteStates
{
    // Every save of user data passes through here, including playback progress, so the table is capped. Losing
    // it only means the next rating of an item not seen since is reported as a favorite change.
    private const int MaxTracked = 20_000;

    private readonly ConcurrentDictionary<(Guid User, Guid Item), bool> _favorites = new();

    /// <summary>
    /// Records a user's current favorite state for an item.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="isFavorite">Whether it is a favorite now.</param>
    /// <returns>True when it changed, or when it was not known before.</returns>
    public bool Record(Guid userId, Guid itemId, bool isFavorite)
    {
        if (_favorites.Count >= MaxTracked)
        {
            _favorites.Clear();
        }

        var changed = true;
        _favorites.AddOrUpdate((userId, itemId), isFavorite, (_, previous) =>
        {
            changed = previous != isFavorite;
            return isFavorite;
        });
        return changed;
    }
}
