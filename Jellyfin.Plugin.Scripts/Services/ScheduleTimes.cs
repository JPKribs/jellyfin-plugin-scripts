using System;
using System.Globalization;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Parses the <c>HH:mm</c> times a daily schedule uses.
/// </summary>
public static class ScheduleTimes
{
    /// <summary>
    /// Parses a 24 hour <c>HH:mm</c> time of day.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <param name="timeOfDay">The time of day.</param>
    /// <returns>True when the text is a valid time.</returns>
    public static bool TryParse(string? value, out TimeSpan timeOfDay)
    {
        timeOfDay = TimeSpan.Zero;
        if (!TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        timeOfDay = time.ToTimeSpan();
        return true;
    }
}
