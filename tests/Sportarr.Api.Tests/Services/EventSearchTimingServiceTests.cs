using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class EventSearchTimingServiceTests
{
    [Fact]
    public void CanSearch_ShouldReturnFalse_WhenEventIsMoreThanTwelveHoursAway()
    {
        var eventDate = DateTime.UtcNow.AddHours(13);

        var canSearch = EventSearchTimingService.CanSearch(eventDate);

        canSearch.Should().BeFalse();
    }

    [Fact]
    public void CanSearch_ShouldReturnTrue_WhenEventIsWithinTwelveHourBuffer()
    {
        var eventDate = DateTime.UtcNow.AddHours(11);

        var canSearch = EventSearchTimingService.CanSearch(eventDate);

        canSearch.Should().BeTrue();
    }

    [Fact]
    public void CanSearch_ShouldReturnTrue_ForManualSearchEvenWhenEventIsMoreThanTwelveHoursAway()
    {
        var eventDate = DateTime.UtcNow.AddDays(30);

        var canSearch = EventSearchTimingService.CanSearch(eventDate, allowManualSearch: true);

        canSearch.Should().BeTrue();
    }
}
