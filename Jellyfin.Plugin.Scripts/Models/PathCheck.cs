namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The result of a path check.
/// </summary>
public class PathCheck
{
    /// <summary>Gets or sets the full path checked.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the path exists.</summary>
    public bool Exists { get; set; }

    /// <summary>Gets or sets a value indicating whether the path is a folder.</summary>
    public bool IsDirectory { get; set; }

    /// <summary>Gets or sets a value indicating whether the file has an execute bit.</summary>
    public bool IsExecutable { get; set; }

    /// <summary>Gets or sets a sentence describing the result.</summary>
    public string Message { get; set; } = string.Empty;
}
