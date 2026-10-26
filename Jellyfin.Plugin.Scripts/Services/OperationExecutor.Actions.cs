using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// The steps that act on the server itself: sessions, the event's item and user, collections, and libraries.
/// </summary>
public partial class OperationExecutor
{
    private async Task<OperationResult> PausePlaybackAsync(RunState state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(state.Context.SessionId))
        {
            return Result(OperationStatus.Skipped, "This event has no session to pause.");
        }

        var request = new PlaystateRequest { Command = PlaystateCommand.Pause };
        await _sessionManager.SendPlaystateCommand(null, state.Context.SessionId, request, cancellationToken).ConfigureAwait(false);
        return Result(OperationStatus.Succeeded, "Paused playback on " + Describe(state) + ".");
    }

    private OperationResult SetPlayed(RunState state, bool played)
    {
        if (!TryUserAndItem(state, out var user, out var item, out var skipped))
        {
            return skipped;
        }

        if (played)
        {
            item.MarkPlayed(user, DateTime.UtcNow, resetPosition: true);
        }
        else
        {
            item.MarkUnplayed(user);
        }

        return Result(OperationStatus.Succeeded, "Marked " + item.Name + (played ? " played" : " unplayed") + " for " + user.Username + ".");
    }

    private OperationResult SetFavorite(ScriptOperation op, RunState state, CancellationToken cancellationToken)
    {
        if (!TryUserAndItem(state, out var user, out var item, out var skipped))
        {
            return skipped;
        }

        var data = _userDataManager.GetUserData(user, item);
        if (data is null)
        {
            return Result(OperationStatus.Failed, "Could not read " + user.Username + "'s data for " + item.Name + ".");
        }

        if (data.IsFavorite == op.Favorite)
        {
            return Result(OperationStatus.Succeeded, item.Name + " was already " + (op.Favorite ? "a favorite" : "not a favorite") + " for " + user.Username + ".");
        }

        data.IsFavorite = op.Favorite;
        _userDataManager.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, cancellationToken);
        return Result(OperationStatus.Succeeded, (op.Favorite ? "Added " : "Removed ") + item.Name + (op.Favorite ? " to " : " from ") + user.Username + "'s favorites.");
    }

    private async Task<OperationResult> ChangeTagAsync(ScriptOperation op, RunState state, bool add, CancellationToken cancellationToken)
    {
        if (!TryItem(state, out var item, out var skipped))
        {
            return skipped;
        }

        var tag = Placeholders.Fill(op.Tag, state.Variables).Trim();
        if (tag.Length == 0)
        {
            return Result(OperationStatus.Failed, "The tag is empty once its placeholders are filled.");
        }

        var tags = item.Tags ?? Array.Empty<string>();
        var has = tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

        // Saving an unchanged item would still raise Item updated, so a no-op is not saved.
        if (has == add)
        {
            return Result(OperationStatus.Succeeded, item.Name + (add ? " already has" : " does not have") + " the tag " + tag + ".");
        }

        item.Tags = add ? tags.Append(tag).ToArray() : tags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToArray();
        await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        return Result(OperationStatus.Succeeded, (add ? "Added the tag " : "Removed the tag ") + tag + (add ? " to " : " from ") + item.Name + ".");
    }

    private async Task<OperationResult> ChangeCollectionAsync(ScriptOperation op, RunState state, bool add)
    {
        if (!TryItem(state, out var item, out var skipped))
        {
            return skipped;
        }

        if (!Guid.TryParse(op.CollectionId, out var collectionId) || _libraryManager.GetItemById(collectionId) is not { } collection)
        {
            return Result(OperationStatus.Failed, "The chosen collection no longer exists.");
        }

        if (add)
        {
            await _collectionManager.AddToCollectionAsync(collectionId, new[] { item.Id }).ConfigureAwait(false);
        }
        else
        {
            await _collectionManager.RemoveFromCollectionAsync(collectionId, new[] { item.Id }).ConfigureAwait(false);
        }

        return Result(OperationStatus.Succeeded, (add ? "Added " : "Removed ") + item.Name + (add ? " to " : " from ") + collection.Name + ".");
    }

    private async Task<OperationResult> SetUserDisabledAsync(ScriptOperation op, RunState state)
    {
        if (!TryUser(state, out var user, out var skipped))
        {
            return skipped;
        }

        // A script must never be able to lock every administrator out of the server.
        if (op.Disable && user.HasPermission(PermissionKind.IsAdministrator))
        {
            return Result(OperationStatus.Skipped, user.Username + " is an administrator, and scripts never disable administrators.");
        }

        if (user.HasPermission(PermissionKind.IsDisabled) == op.Disable)
        {
            return Result(OperationStatus.Succeeded, user.Username + " was already " + (op.Disable ? "disabled" : "enabled") + ".");
        }

        user.SetPermission(PermissionKind.IsDisabled, op.Disable);
        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
        return Result(OperationStatus.Succeeded, (op.Disable ? "Disabled " : "Enabled ") + user.Username + ".");
    }

    private async Task<OperationResult> SignOutUserAsync(RunState state)
    {
        if (!TryUser(state, out var user, out var skipped))
        {
            return skipped;
        }

        if (user.HasPermission(PermissionKind.IsAdministrator))
        {
            return Result(OperationStatus.Skipped, user.Username + " is an administrator, and scripts never sign administrators out.");
        }

        await _sessionManager.RevokeUserTokens(user.Id, null).ConfigureAwait(false);
        return Result(OperationStatus.Succeeded, "Signed " + user.Username + " out of every device.");
    }

    private OperationResult ScanLibraries()
    {
        _libraryManager.QueueLibraryScan();
        return Result(OperationStatus.Succeeded, "Queued a scan of every library.");
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "The path is one an administrator configured; event values never reach it.")]
    private async Task<OperationResult> AppendToFileAsync(ScriptOperation op, RunState state, CancellationToken cancellationToken)
    {
        var path = (op.FilePath ?? string.Empty).Trim();
        var folder = Path.GetDirectoryName(path);
        if (!Path.IsPathRooted(path) || string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return Result(OperationStatus.Failed, "The server cannot find the folder for " + path + ". In Docker, mount it into the container.");
        }

        var line = Placeholders.Fill(op.Text, state.Variables);
        await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        return Result(OperationStatus.Succeeded, "Appended a line to " + path + ".");
    }

    private bool IsAdministrator(Guid userId)
        => userId != Guid.Empty && _userManager.GetUserById(userId) is { } user && user.HasPermission(PermissionKind.IsAdministrator);

    private bool TryItem(RunState state, out BaseItem item, out OperationResult skipped)
    {
        item = null!;
        skipped = null!;
        if (state.Context.ItemId is not { } itemId)
        {
            skipped = Result(OperationStatus.Skipped, "This event has no item.");
            return false;
        }

        if (_libraryManager.GetItemById(itemId) is not { } found)
        {
            skipped = Result(OperationStatus.Skipped, "The event's item is no longer in the library.");
            return false;
        }

        item = found;
        return true;
    }

    private bool TryUser(RunState state, out User user, out OperationResult skipped)
    {
        user = null!;
        skipped = null!;
        var found = state.Context.UserIds.Select(_userManager.GetUserById).FirstOrDefault(u => u is not null);
        if (found is null)
        {
            skipped = Result(OperationStatus.Skipped, "This event has no user.");
            return false;
        }

        user = found;
        return true;
    }

    private bool TryUserAndItem(RunState state, out User user, out BaseItem item, out OperationResult skipped)
    {
        item = null!;
        return TryUser(state, out user, out skipped) && TryItem(state, out item, out skipped);
    }
}
