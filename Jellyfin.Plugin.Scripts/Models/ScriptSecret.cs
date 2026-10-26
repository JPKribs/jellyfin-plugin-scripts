using System.Xml.Serialization;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// A named secret a script uses, such as an API token. It reaches steps as <c>{{SECRET_NAME}}</c> and commands
/// as <c>JF_SECRET_NAME</c>. The value itself is kept encrypted in the secret vault, never in the configuration.
/// </summary>
public class ScriptSecret
{
    /// <summary>Gets or sets the name: capital letters, digits, and underscores.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value. Stored configuration holds only the kept sentinel, or an empty string when the
    /// secret has no value. Incoming, it is the sentinel for an untouched secret or a new value to encrypt.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name the secret had when the dashboard loaded it, so a renamed secret keeps its stored
    /// value. Never persisted.
    /// </summary>
    [XmlIgnore]
    public string PreviousName { get; set; } = string.Empty;
}
