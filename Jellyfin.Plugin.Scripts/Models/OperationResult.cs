namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The outcome of one step of a run.
/// </summary>
public class OperationResult
{
    /// <summary>Gets or sets the step's type.</summary>
    public OperationType Type { get; set; }

    /// <summary>Gets or sets the outcome.</summary>
    public OperationStatus Status { get; set; }

    /// <summary>Gets or sets a short description of the outcome.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the captured output, such as a command's stdout and stderr or a response body.</summary>
    public string Output { get; set; } = string.Empty;
}
