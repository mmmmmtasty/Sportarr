namespace Sportarr.Api.Services;

/// <summary>
/// Centralizes the rules for whether an event should be searched yet.
/// The upstream data source provides the authoritative UTC timestamp.
/// Automatic searches may start slightly early to tolerate small upstream time errors.
/// </summary>
public static class EventSearchTimingService
{
    public static readonly TimeSpan AutomaticSearchLeadTime = TimeSpan.FromHours(12);

    public static bool IsUnaired(DateTime eventDate)
    {
        return eventDate > DateTime.UtcNow.Add(AutomaticSearchLeadTime);
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
