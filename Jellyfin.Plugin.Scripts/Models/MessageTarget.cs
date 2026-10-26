namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// Which sessions a message is shown on.
/// </summary>
public enum MessageTarget
{
    /// <summary>The session that raised the event.</summary>
    EventSession,

    /// <summary>Every active session of the event's users.</summary>
    UserSessions,

    /// <summary>Every active session.</summary>
    AllSessions,

    /// <summary>Every active session of an administrator.</summary>
    AdminSessions
}
