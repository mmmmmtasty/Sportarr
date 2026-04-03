using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class ReleaseMatchingServiceTests
{
    private readonly ReleaseMatchingService _service;

    public ReleaseMatchingServiceTests()
    {
        var logger = Mock.Of<ILogger<ReleaseMatchingService>>();
        var partDetector = new EventPartDetector(Mock.Of<ILogger<EventPartDetector>>());
        _service = new ReleaseMatchingService(logger, new SportsFileNameParser(Mock.Of<ILogger<SportsFileNameParser>>()), partDetector);
    }

    [Fact]
    public void ValidateRelease_ShouldRejectEventMoreThanTwelveHoursAwayEvenWhenReleaseDateMatches()
    {
        var eventDate = DateTime.UtcNow.AddDays(2).Date.AddHours(3);
        var evt = new Event
        {
            Title = "UFC 300",
            Sport = "Fighting",
            EventDate = eventDate
        };

        var release = new ReleaseSearchResult
        {
            Title = "UFC.300.2026.1080p.WEB-DL.x264",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        var result = _service.ValidateRelease(release, evt);

        result.IsHardRejection.Should().BeTrue();
        result.Rejections.Should().Contain(r => r.Contains("more than 12 hours away"));
        result.IsMatch.Should().BeFalse();
    }

    [Fact]
    public void ValidateRelease_ShouldAllowEventWithinTwelveHourBuffer()
    {
        var eventDate = DateTime.UtcNow.AddHours(11);
        var evt = new Event
        {
            Title = "UFC 300",
            Sport = "Fighting",
            EventDate = eventDate
        };

        var release = new ReleaseSearchResult
        {
            Title = $"UFC.300.{eventDate:yyyy}.1080p.WEB-DL.x264",
            Guid = "test-guid",
            DownloadUrl = "http://test.com/download",
            Indexer = "TestIndexer",
            Size = 1024 * 1024 * 1024
        };

        var result = _service.ValidateRelease(release, evt);

        result.IsHardRejection.Should().BeFalse();
    }
}
