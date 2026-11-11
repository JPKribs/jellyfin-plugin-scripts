using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Turns one event into one event per library item or per user, for a script set to run for each. The script's
/// own item kinds, users, and conditions pick which ones.
/// </summary>
public class ForEachExpander
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly EventContextBuilder _builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ForEachExpander"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="builder">The event context builder.</param>
    public ForEachExpander(ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager, EventContextBuilder builder)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _builder = builder;
    }

    /// <summary>
    /// Expands an event for a script.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="context">The event.</param>
    /// <returns>
    /// One context per matching item or user, at most <see cref="ScriptDefinition.ForEachLimit"/>, or null when the
    /// script runs once for this event.
    /// </returns>
    public virtual IReadOnlyList<EventContext>? Expand(ScriptDefinition script, EventContext context)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(context);
        if (context.ForEach != ForEachMode.None || !TriggerCatalog.Expands(context.Trigger, script.ForEach))
        {
            return null;
        }

        var limit = Math.Clamp(script.ForEachLimit, 1, ConfigurationValidator.MaxForEachLimit);
        return script.ForEach == ForEachMode.Items ? Items(script, context, limit) : Users(script, context, limit);
    }

    private List<EventContext> Items(ScriptDefinition script, EventContext context, int limit)
    {
        var matches = new List<EventContext>();
        if (script.ItemKinds.Count == 0)
        {
            return matches;
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = script.ItemKinds.ToArray(),
            Recursive = true,
            IsVirtualItem = false
        });

        // Play state is read from every user, so it is looked up before matching only when a condition needs it,
        // and otherwise just for the items that matched.
        var filtersOnPlays = script.Conditions.Any(c => c.Field is ConditionField.PlayedByAnyone or ConditionField.DaysSincePlayed);
        var users = _userManager.GetUsers().ToList();
        foreach (var item in items)
        {
            var candidate = _builder.ForItem(context, item);
            if (filtersOnPlays)
            {
                AddPlays(candidate, item, users);
            }

            if (!ConditionEvaluator.Matches(script, candidate))
            {
                continue;
            }

            if (!filtersOnPlays)
            {
                AddPlays(candidate, item, users);
            }

            matches.Add(candidate);
            if (matches.Count >= limit)
            {
                break;
            }
        }

        return matches;
    }

    private List<EventContext> Users(ScriptDefinition script, EventContext context, int limit)
    {
        var matches = new List<EventContext>();
        foreach (var user in _userManager.GetUsers().OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = _builder.ForUser(context, user);
            if (!ConditionEvaluator.Matches(script, candidate))
            {
                continue;
            }

            matches.Add(candidate);
            if (matches.Count >= limit)
            {
                break;
            }
        }

        return matches;
    }

    private void AddPlays(EventContext context, BaseItem item, List<User> users)
    {
        var played = false;
        DateTime? last = null;
        foreach (var user in users)
        {
            if (_userDataManager.GetUserData(user, item) is not { } data)
            {
                continue;
            }

            played |= data.Played;
            if (data.LastPlayedDate is { } when && (last is null || when > last))
            {
                last = when;
            }
        }

        context.Set("PLAYED_BY_ANYONE", played ? "true" : "false");
        var since = last ?? (item.DateCreated > DateTime.MinValue ? item.DateCreated : (DateTime?)null);
        if (since is { } date)
        {
            context.Set("DAYS_SINCE_PLAYED", Math.Max(0, (long)(DateTime.UtcNow - date.ToUniversalTime()).TotalDays).ToString(CultureInfo.InvariantCulture));
        }
    }
}
