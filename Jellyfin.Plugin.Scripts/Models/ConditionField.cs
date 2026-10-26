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
    TaskStatus,

    /// <summary>The server's local hour, 0 to 23.</summary>
    Hour,

    /// <summary>The server's local day of the week, such as Monday.</summary>
    DayOfWeek,

    /// <summary>The item's file or folder path.</summary>
    ItemPath,

    /// <summary>Any of the item's genres.</summary>
    Genre,

    /// <summary>Any of the item's tags.</summary>
    Tag,

    /// <summary>Any of the item's studios.</summary>
    Studio,

    /// <summary>The item's parental rating, such as PG-13.</summary>
    OfficialRating,

    /// <summary>The item's production year.</summary>
    Year,

    /// <summary>The item's video height in pixels.</summary>
    VideoHeight,

    /// <summary>The item's community rating.</summary>
    CommunityRating,

    /// <summary>How far through the item playback is, as a percentage.</summary>
    ProgressPercent,

    /// <summary>Why the server is transcoding.</summary>
    TranscodeReasons,

    /// <summary>The video codec sent to the client while transcoding.</summary>
    VideoCodec,

    /// <summary>The audio codec sent to the client while transcoding.</summary>
    AudioCodec,

    /// <summary>The client's IP address.</summary>
    RemoteAddress,

    /// <summary>The device's ID.</summary>
    DeviceId,

    /// <summary>The client application's version.</summary>
    ClientVersion,

    /// <summary>Whether the user is an administrator, <c>true</c> or <c>false</c>.</summary>
    IsAdmin,

    /// <summary>The scheduled task's category.</summary>
    TaskCategory,

    /// <summary>The plugin's name.</summary>
    PluginName,

    /// <summary>Whether the item is now a favorite, <c>true</c> or <c>false</c>.</summary>
    IsFavorite,

    /// <summary>Whether the item is now played, <c>true</c> or <c>false</c>.</summary>
    IsPlayed,

    /// <summary>The collection's name.</summary>
    CollectionName,

    /// <summary>The subtitle or lyrics provider that failed.</summary>
    Provider,

    /// <summary>Why an item was updated, such as MetadataDownload or ImageUpdate.</summary>
    UpdateReason,

    /// <summary>How many sessions are transcoding, counting this one.</summary>
    ActiveTranscodes,

    /// <summary>How many sessions are playing something, counting this one.</summary>
    ActiveStreams,

    /// <summary>The activity log entry's type, such as VideoPlayback.</summary>
    ActivityType,

    /// <summary>The activity log entry's severity, such as Information or Warning.</summary>
    ActivitySeverity
}
