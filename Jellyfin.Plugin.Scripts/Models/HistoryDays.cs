using System.Collections.Generic;

namespace Jellyfin.Plugin.Scripts.Models;

/// <summary>
/// The runs of each recent day, for the chart on the History tab. The three lists line up by index.
/// </summary>
public class HistoryDays
{
    /// <summary>Gets or sets each day as yyyy-MM-dd in the server's time zone, oldest first.</summary>
    public List<string> Days { get; set; } = new();

    /// <summary>Gets or sets how many runs succeeded on each day.</summary>
    public List<int> Succeeded { get; set; } = new();

    /// <summary>Gets or sets how many runs failed on each day.</summary>
    public List<int> Failed { get; set; } = new();
}
