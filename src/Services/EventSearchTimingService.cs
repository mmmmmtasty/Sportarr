namespace Sportarr.Api.Services;

/// <summary>
/// Centralizes the rules for whether an event should be searched yet.
/// Date-only metadata often lands as midnight UTC, so those events are
/// compared by calendar date instead of exact time.
/// </summary>
public static class EventSearchTimingService
{
    public static bool IsUnaired(DateTime eventDate)
    {
        var now = DateTime.UtcNow;

        if (eventDate.TimeOfDay == TimeSpan.Zero)
        {
            return eventDate.Date > now.Date;
        }

        return eventDate > now.AddHours(24);
    }

    public static bool CanSearch(DateTime eventDate, bool allowManualSearch = false)
    {
        if (allowManualSearch)
        {
            return true;
        }

        return !IsUnaired(eventDate);
    }
}
