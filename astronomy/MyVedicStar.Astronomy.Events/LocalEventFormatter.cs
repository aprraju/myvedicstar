using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Events;

/// <summary>How a local rendering resolved a daylight-saving gap or fold (policy B3).</summary>
public enum LocalTimeResolution
{
    /// <summary>The local time exists exactly once.</summary>
    Unambiguous,
    /// <summary>The local time does not exist (a spring-forward gap); the resolution used is reported.</summary>
    NonexistentGapShiftedForward,
    /// <summary>The local time occurs twice (an autumn fold); the earlier instant is chosen and that is reported.</summary>
    AmbiguousFoldEarlierOccurrence
}

/// <summary>A locally rendered event time with the resolution it required. Never the engine's primary result.</summary>
public sealed record LocalEventTime(
    DateTimeOffset LocalInstant,
    string TimeZoneId,
    string TimezoneDataVersion,
    LocalTimeResolution Resolution);

/// <summary>
/// Edge-only local formatting. The engine's results are instants (Julian Day UT, UTC-based); this converts for
/// presentation and <b>states</b> how a DST gap or fold was resolved instead of hiding it.
/// </summary>
public static class LocalEventFormatter
{
    /// <summary>Renders an event instant in the supplied IANA zone, reporting any gap or fold resolution.</summary>
    public static LocalEventTime ToLocal(JulianDayUt instant, string timeZoneId, string timezoneDataVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(timezoneDataVersion);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var utc = JulianDayToUtc(instant);

        // The instant always exists: only its LOCAL rendering can be ambiguous or nonexistent. Ask the zone about the
        // local wall-clock value directly rather than inferring from an offset round trip.
        var localUnspecified = DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, timeZone), DateTimeKind.Unspecified);

        var resolution = LocalTimeResolution.Unambiguous;
        if (timeZone.IsInvalidTime(localUnspecified))
            resolution = LocalTimeResolution.NonexistentGapShiftedForward;
        else if (timeZone.IsAmbiguousTime(localUnspecified))
            resolution = LocalTimeResolution.AmbiguousFoldEarlierOccurrence;

        var offset = timeZone.GetUtcOffset(utc);
        return new LocalEventTime(new DateTimeOffset(localUnspecified, offset), timeZoneId, timezoneDataVersion,
            resolution);
    }

    /// <summary>Julian Day UT to a UTC <see cref="DateTime"/>; the inverse of the standard calendar conversion.</summary>
    public static DateTimeOffset JulianDayToUtc(JulianDayUt instant)
    {
        // JD 2440587.5 is 1970-01-01T00:00:00Z.
        var seconds = (instant.Value - 2440587.5) * 86400.0;
        return DateTimeOffset.UnixEpoch.AddSeconds(seconds);
    }
}
