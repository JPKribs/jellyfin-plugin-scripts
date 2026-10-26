namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// One step of a script. The properties are flat so the XML configuration stays readable. Each
/// <see cref="OperationType"/> reads only its own, and text fields accept <c>{{NAME}}</c> placeholders.
/// </summary>
public class ScriptOperation
{
    /// <summary>Gets or sets what the step does.</summary>
    public OperationType Type { get; set; }

    /// <summary>Gets or sets a value indicating whether the following steps still run when this one fails.</summary>
    public bool ContinueOnError { get; set; }

    /// <summary>
    /// Gets or sets the executable, or with <see cref="RunThroughShell"/> the whole shell line. A path runs that
    /// file and must be reachable by the server, which in Docker means mounted into the container. A bare
    /// name such as <c>curl</c> is looked up on the server's PATH.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Gets or sets the arguments, one per line. Ignored with <see cref="RunThroughShell"/>.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Command"/> runs through <c>/bin/sh -c</c> (or
    /// <c>cmd /c</c> on Windows). Placeholders are not filled in a shell line, so event values reach the shell
    /// only as environment variables.
    /// </summary>
    public bool RunThroughShell { get; set; }

    /// <summary>
    /// Gets or sets the folder the command runs in. Empty uses the folder holding the command when it is a
    /// path to a file, otherwise the server's own working folder.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the command timeout in seconds. Zero uses the default.</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>Gets or sets the HTTP method.</summary>
    public string Method { get; set; } = "POST";

    /// <summary>Gets or sets the absolute HTTP or HTTPS URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the request headers, one <c>Name: value</c> per line.</summary>
    public string Headers { get; set; } = string.Empty;

    /// <summary>Gets or sets the request body. Empty sends none.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Gets or sets the body's content type.</summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>Gets or sets the message header or activity log entry name.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the message text or activity log overview.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets how long a message shows, in seconds. Zero leaves it to the client.</summary>
    public int DisplaySeconds { get; set; }

    /// <summary>Gets or sets the key of the scheduled task to queue.</summary>
    public string TaskKey { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the refresh is a full refresh rather than a scan for missing data.</summary>
    public bool FullRefresh { get; set; }

    /// <summary>Gets or sets a value indicating whether the refresh replaces existing metadata and images.</summary>
    public bool ReplaceAll { get; set; }

    /// <summary>Gets or sets how long to wait, in seconds.</summary>
    public int WaitSeconds { get; set; }
}
