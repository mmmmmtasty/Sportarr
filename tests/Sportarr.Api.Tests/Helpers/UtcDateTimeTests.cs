using FluentAssertions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;

namespace Sportarr.Api.Tests.Helpers;

public class UtcDateTimeTests
{
    [Fact]
    public void Normalize_ShouldMarkUnspecifiedDateTimeAsUtc()
    {
        var value = new DateTime(2026, 6, 6, 12, 34, 56, DateTimeKind.Unspecified);

        var normalized = UtcDateTime.Normalize(value);

        normalized.Kind.Should().Be(DateTimeKind.Utc);
        normalized.Should().Be(new DateTime(2026, 6, 6, 12, 34, 56, DateTimeKind.Utc));
    }

    [Fact]
    public void Normalize_ShouldConvertLocalDateTimeToUtc()
    {
        var value = DateTime.SpecifyKind(new DateTime(2026, 6, 6, 12, 34, 56), DateTimeKind.Local);

        var normalized = UtcDateTime.Normalize(value);

        normalized.Kind.Should().Be(DateTimeKind.Utc);
        normalized.Should().Be(value.ToUniversalTime());
    }

    [Fact]
    public void TryParseAsUtc_ShouldTreatTimezoneLessTimestampAsUtc()
    {
        var parsed = UtcDateTime.TryParseAsUtc("2026-06-06T00:00:00", out var value);

        parsed.Should().BeTrue();
        value.Should().Be(new DateTime(2026, 6, 6, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TryParseUpstreamEventDateTime_ShouldFallBackToDateAndTime()
    {
        var parsed = UtcDateTime.TryParseUpstreamEventDateTime(null, "2026-06-06", "03:30:00", out var value);

        parsed.Should().BeTrue();
        value.Should().Be(new DateTime(2026, 6, 6, 3, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void EventResponse_FromEvent_ShouldNormalizeUtcSensitiveFields()
    {
        var evt = new Event
        {
            Title = "UFC 300",
            Sport = "Fighting",
            EventDate = new DateTime(2026, 6, 6, 0, 0, 0, DateTimeKind.Unspecified),
            Added = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Unspecified),
            LastUpdate = new DateTime(2026, 4, 3, 13, 0, 0, DateTimeKind.Unspecified)
        };

        var response = EventResponse.FromEvent(evt);

        response.EventDate.Kind.Should().Be(DateTimeKind.Utc);
        response.Added.Kind.Should().Be(DateTimeKind.Utc);
        response.LastUpdate!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void EventFileResponse_FromEventFile_ShouldNormalizeAdded()
    {
        var file = new EventFile
        {
            FilePath = "/tmp/test.mkv",
            Added = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Unspecified)
        };

        var response = EventFileResponse.FromEventFile(file);

        response.Added.Kind.Should().Be(DateTimeKind.Utc);
    }
}
