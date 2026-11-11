using System.Collections.Generic;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The devices the server has seen, so New device seen runs once per device.
/// </summary>
public class DeviceState
{
    /// <summary>Gets or sets a value indicating whether the devices the server already knew were recorded.</summary>
    public bool Seeded { get; set; }

    /// <summary>Gets or sets the IDs of the devices seen.</summary>
    public List<string> DeviceIds { get; set; } = new();
}
