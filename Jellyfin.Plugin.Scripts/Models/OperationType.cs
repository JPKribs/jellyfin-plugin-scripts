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

    /// <summary>Shows a message on the event's session.</summary>
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
    Wait
}
