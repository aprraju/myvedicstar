using NodaTime;

namespace MyVedicDay.Caching;

/// <summary>
/// Expiry for results that belong to one local calendar date at a location (a day's Panchang, a day's Muhurta list…).
/// </summary>
/// <remarks>
/// A result for local date D expires at the start of D+1 in the location's time zone, converted to UTC and DST-aware,
/// so today's entry expires at the location's next local midnight. Dates that have already ended use
/// <c>pastDateLifetime</c>, and every entry is capped at <c>maxLifetime</c>.
/// </remarks>
public static class LocalDayCacheExpiration
{
    public static DateTimeOffset Calculate(
        LocalDate date, DateTimeZone timeZone, Instant now, TimeSpan pastDateLifetime, TimeSpan maxLifetime)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        // AtStartOfDay handles zones where midnight is skipped by a DST transition.
        var endOfLocalDay = timeZone.AtStartOfDay(date.PlusDays(1)).ToInstant();
        var expiresAt = endOfLocalDay > now ? endOfLocalDay : now + Duration.FromTimeSpan(pastDateLifetime);

        var latest = now + Duration.FromTimeSpan(maxLifetime);
        return (expiresAt < latest ? expiresAt : latest).ToDateTimeOffset();
    }
}
