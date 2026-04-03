using System.Globalization;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Shared UTC parsing and normalization helpers for non-DVR/IPTV timestamps.
/// The app stores and compares these values as UTC, even when upstream data omits an explicit offset.
/// </summary>
public static class UtcDateTime
{
    public static DateTime Normalize(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    public static DateTime? Normalize(DateTime? value)
    {
        return value.HasValue ? Normalize(value.Value) : null;
    }

    public static bool TryParseAsUtc(string? value, out DateTime utcValue)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utcValue = parsed.UtcDateTime;
            return true;
        }

        utcValue = default;
        return false;
    }

    public static bool TryParseExactAsUtc(string? value, string format, out DateTime utcValue)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTime.TryParseExact(
                value,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utcValue = Normalize(parsed);
            return true;
        }

        utcValue = default;
        return false;
    }

    public static bool TryParseUpstreamEventDateTime(string? timestamp, string? fallbackDate, string? fallbackTime, out DateTime utcValue)
    {
        if (TryParseAsUtc(timestamp, out utcValue))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(fallbackDate))
        {
            utcValue = default;
            return false;
        }

        var combined = string.IsNullOrWhiteSpace(fallbackTime)
            ? fallbackDate
            : $"{fallbackDate}T{fallbackTime}";

        return TryParseAsUtc(combined, out utcValue);
    }
}
