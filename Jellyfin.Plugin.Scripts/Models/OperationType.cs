namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// A step a script performs. The names are stored in configuration, so never rename one.
/// </summary>
public enum OperationType
{
    /// <summary>Starts a process, directly or through the system shell.</summary>
    RunCommand,

    /// <summary>Sends an HTTP request.</summary>
    HttpRequest,

    /// <summary>Shows a message on one or more sessions.</summary>
    SendMessage,

    /// <summary>Stops playback on the event's session.</summary>
    StopPlayback,

    /// <summary>Queues a metadata refresh of the event's item.</summary>
    RefreshMetadata,

    /// <summary>Queues a scheduled task.</summary>
    RunScheduledTask,

    /// <summary>Writes an entry to the server's activity log.</summary>
    WriteActivityLog,

    /// <summary>Waits before the next step.</summary>
    Wait,

    /// <summary>Pauses playback on the event's session.</summary>
    PausePlayback,

    /// <summary>Marks the event's item played for the event's user.</summary>
    MarkPlayed,

    /// <summary>Marks the event's item unplayed for the event's user.</summary>
    MarkUnplayed,

    /// <summary>Adds or removes the event's item as a favorite for the event's user.</summary>
    SetFavorite,

    /// <summary>Adds a tag to the event's item.</summary>
    AddTag,

    /// <summary>Removes a tag from the event's item.</summary>
    RemoveTag,

    /// <summary>Adds the event's item to a collection.</summary>
    AddToCollection,

    /// <summary>Removes the event's item from a collection.</summary>
    RemoveFromCollection,

    /// <summary>Disables or enables the event's user.</summary>
    SetUserDisabled,

    /// <summary>Signs the event's user out of every device.</summary>
    SignOutUser,

    /// <summary>Queues a scan of every library.</summary>
    ScanLibraries,

    /// <summary>Runs another script with the same event.</summary>
    RunScript,

    /// <summary>Appends a line of text to a file.</summary>
    AppendToFile
}
