namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// The outcome of one process run.
/// </summary>
/// <param name="ExitCode">The exit code, or null when the process did not start or was killed.</param>
/// <param name="TimedOut">Whether the process ran past its timeout and was killed.</param>
/// <param name="Output">The captured stdout and stderr, interleaved and capped.</param>
/// <param name="StartError">Why the process could not start, when it did not.</param>
public sealed record CommandResult(int? ExitCode, bool TimedOut, string Output, string? StartError);
