using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Scripts.API;

/// <summary>
/// The dashboard's API: what the editor offers, test runs, the run history, and path checks. Every route is
/// for administrators only.
/// </summary>
[ApiController]
[Route("Scripts")]
[Authorize(Policy = "RequiresElevation")]
public class ScriptsController : ControllerBase
{
    // The claim Jellyfin's authentication handler stores the signed-in user's ID under.
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ITaskManager _taskManager;
    private readonly EventContextBuilder _builder;
    private readonly ScriptRunner _runner;
    private readonly HistoryStore _history;
    private readonly SecretProtector _secrets;
    private readonly SecretVault _vault;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsController"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="taskManager">The scheduled task manager.</param>
    /// <param name="builder">The event context builder.</param>
    /// <param name="runner">The script runner.</param>
    /// <param name="history">The run history.</param>
    /// <param name="secrets">Encrypts secrets sent with a test run.</param>
    /// <param name="vault">The stored secret values.</param>
    public ScriptsController(
        ILibraryManager libraryManager,
        IUserManager userManager,
        ITaskManager taskManager,
        EventContextBuilder builder,
        ScriptRunner runner,
        HistoryStore history,
        SecretProtector secrets,
        SecretVault vault)
    {
        _secrets = secrets;
        _vault = vault;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _taskManager = taskManager;
        _builder = builder;
        _runner = runner;
        _history = history;
    }

    /// <summary>
    /// Gets what the editor offers: the triggers and their variables, which condition fields apply to which
    /// triggers, every item kind, and the server's users, libraries, and scheduled tasks.
    /// </summary>
    /// <returns>The catalog.</returns>
    [HttpGet("Catalog")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetCatalog()
    {
        var triggers = TriggerCatalog.All.Select(t => new
        {
            Key = t.Trigger.ToString(),
            t.Label,
            t.Category,
            t.Description,
            t.HasItem,
            t.HasSession,
            t.HasUser,
            Variables = t.Variables.Select(v => new { v.Name, v.Description })
        });

        var fields = Enum.GetValues<ConditionField>().Select(f => new
        {
            Key = f.ToString(),
            Variable = TriggerCatalog.VariableFor(f),
            Triggers = TriggerCatalog.All.Where(t => TriggerCatalog.Supplies(t.Trigger, f)).Select(t => t.Trigger.ToString())
        });

        var libraries = _libraryManager.GetVirtualFolders()
            .Select(f => new { Id = NormalizeId(f.ItemId), f.Name })
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

        var users = _userManager.GetUsers()
            .Select(u => new { Id = u.Id.ToString("N", CultureInfo.InvariantCulture), Name = u.Username })
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase);

        var collections = _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.BoxSet }, Recursive = true })
            .Select(c => new { Id = c.Id.ToString("N", CultureInfo.InvariantCulture), c.Name })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

        var tasks = _taskManager.ScheduledTasks
            .Select(t => new { t.ScheduledTask.Key, t.Name, t.Category })
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase);

        return Ok(new
        {
            Triggers = triggers,
            Fields = fields,
            ItemKinds = Enum.GetValues<BaseItemKind>()
                .Select(k => new { Value = k.ToString(), Label = ItemKindLabels.For(k) })
                .OrderBy(k => k.Label, StringComparer.OrdinalIgnoreCase),
            Collections = collections,
            Libraries = libraries,
            Users = users,
            Tasks = tasks
        });
    }

    /// <summary>
    /// Runs a script's steps now, with sample values for the chosen trigger. The script is taken from the
    /// request, so unsaved edits can be tried. Its filters are not applied, since the values are samples.
    /// </summary>
    /// <param name="request">The script and the trigger to imitate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The run record.</returns>
    [HttpPost("Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ScriptRun>> Test([FromBody] TestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Script is null || !Enum.IsDefined(request.Trigger))
        {
            return BadRequest("A script and a trigger are required.");
        }

        try
        {
            ConfigurationValidator.Normalize(request.Script);
            ConfigurationValidator.Validate(request.Script);

            // Untouched secrets arrive as the kept sentinel, so they resolve against the saved script's vault.
            var stored = Plugin.Instance?.ReadConfiguration(c => c.Scripts.FirstOrDefault(s => s.Id == request.Script.Id));
            ScriptSecrets.Resolve(request.Script, stored, _vault.Load(), _secrets);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        var context = _builder.Sample(request.Trigger);
        context.TesterId = Guid.TryParse(User.FindFirst(UserIdClaim)?.Value, out var tester) ? tester : Guid.Empty;
        return await _runner.RunAsync(request.Script, context, true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of the run history, newest first.
    /// </summary>
    /// <param name="query">The paging, search, and filter: <c>failed</c>, <c>test</c>, or a script ID.</param>
    /// <returns>The page.</returns>
    [HttpGet("History")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PagedResult<ScriptRun>> GetHistory([FromQuery] PagedQuery query)
        => _history.Query(query ?? new PagedQuery());

    /// <summary>
    /// Deletes the run history.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpDelete("History")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ClearHistory()
    {
        _history.Clear();
        return NoContent();
    }

    /// <summary>
    /// Checks a path from the server's side, so the editor can say whether Jellyfin can reach a script before
    /// it runs. In Docker this is the container's view, which differs from the host's.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>Whether it exists, what it is, and whether it is executable.</returns>
    [HttpGet("CheckPath")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Administrators only. Reporting what the server can see at a path the administrator typed is the purpose of this route.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PathCheck> CheckPath([FromQuery] string? path)
    {
        var check = new PathCheck { Path = path ?? string.Empty };
        if (string.IsNullOrWhiteSpace(path))
        {
            check.Message = "Enter a path.";
            return check;
        }

        try
        {
            var full = Path.GetFullPath(path.Trim());
            check.Path = full;
            if (Directory.Exists(full))
            {
                check.Exists = true;
                check.IsDirectory = true;
                check.Message = "This is a folder, not a file.";
                return check;
            }

            if (!System.IO.File.Exists(full))
            {
                check.Message = "The server cannot find this file. In Docker, mount it into the container.";
                return check;
            }

            check.Exists = true;
            if (OperatingSystem.IsWindows())
            {
                check.IsExecutable = true;
                check.Message = "The server can see this file.";
                return check;
            }

            const UnixFileMode ExecuteBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            check.IsExecutable = (System.IO.File.GetUnixFileMode(full) & ExecuteBits) != 0;
            check.Message = check.IsExecutable
                ? "The server can see this file and it is executable."
                : "The server can see this file, but it is not executable. Run chmod +x on it, or call it through an interpreter.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            check.Message = "The server could not check this path: " + ex.Message;
        }

        return check;
    }

    private static string NormalizeId(string? id)
        => Guid.TryParse(id, out var guid) ? guid.ToString("N", CultureInfo.InvariantCulture) : id ?? string.Empty;
}
