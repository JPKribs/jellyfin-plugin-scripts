namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// An event value a condition compares. Which triggers a field applies to follows from the variables each
/// trigger supplies, see <see cref="Services.TriggerCatalog"/>.
/// </summary>
public enum ConditionField
{
    /// <summary>The user's ID or name. Playback events match any of the session's users.</summary>
    User,

    /// <summary>The ID or name of a library the item belongs to.</summary>
    Library,

    /// <summary>The item's name.</summary>
    ItemName,

    /// <summary>The series an episode belongs to.</summary>
    SeriesName,

    /// <summary>The client application's name.</summary>
    Client,

    /// <summary>The device's name.</summary>
    DeviceName,

    /// <summary>DirectPlay, DirectStream, or Transcode.</summary>
    PlayMethod,

    /// <summary>Whether the remote address is inside the local network, <c>true</c> or <c>false</c>.</summary>
    IsLocal,

    /// <summary>Whether playback reached the end, <c>true</c> or <c>false</c>.</summary>
    PlayedToCompletion,

    /// <summary>The username typed into a failed sign in.</summary>
    UsernameAttempted,

    /// <summary>The scheduled task's key.</summary>
    TaskKey,

    /// <summary>Completed, Failed, Cancelled, or Aborted.</summary>
    TaskStatus
}
