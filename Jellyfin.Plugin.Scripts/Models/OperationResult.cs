using System.Text.Json.Serialization;

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

    /// <summary>Gets or sets the whole output a later step may use, before it is shortened for display. Never stored.</summary>
    [JsonIgnore]
    public string? Captured { get; set; }

    /// <summary>Gets or sets the exit code or HTTP status that goes with <see cref="Captured"/>. Never stored.</summary>
    [JsonIgnore]
    public string? CapturedStatus { get; set; }
}
