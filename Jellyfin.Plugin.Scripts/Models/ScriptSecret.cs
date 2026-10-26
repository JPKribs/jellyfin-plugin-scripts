namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// A named secret a script uses, such as an API token. It reaches steps as <c>{{SECRET_NAME}}</c> and commands
/// as <c>JF_SECRET_NAME</c>, and is encrypted at rest.
/// </summary>
public class ScriptSecret
{
    /// <summary>Gets or sets the name: capital letters, digits, and underscores.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the value, encrypted at rest.</summary>
    public string Value { get; set; } = string.Empty;
}
