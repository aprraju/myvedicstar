using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;
using NodaTime.Utility;

namespace MyVedicDay.Panchang.Business.Daily;

/// <summary>
/// Maps calculation results (UTC instants, enums) to the cached, language-neutral model
/// (local times with the offset in force at each instant, stable string keys).
/// </summary>
internal static class DailyPanchangMapper
{
    public static DailyPanchangData ToData(PanchangDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        var zone = day.TimeZone;
        return new DailyPanchangData(
            day.Date.ToDateOnly(),
            BclConversions.ToDayOfWeek(day.Weekday),
            new SunTimes(ToLocal(day.Sun.Sunrise, zone), ToLocal(day.Sun.Sunset, zone), ToLocal(day.Sun.NextSunrise, zone)),
            day.Tithis.Select(period => ToElement(period, zone)).ToArray(),
            day.Nakshatras.Select(period => ToElement(period, zone)).ToArray(),
            day.Yogas.Select(period => ToElement(period, zone)).ToArray(),
            day.Karanas.Select(period => ToElement(period, zone)).ToArray(),
            day.Periods.Select(period => ToTimePeriod(period, zone)).ToArray());
    }

    private static ElementPeriodData ToElement<TElement>(ElementPeriod<TElement> period, DateTimeZone zone)
        where TElement : struct, Enum =>
        new(
            period.Element.ToString(),
            Convert.ToInt32(period.Element, System.Globalization.CultureInfo.InvariantCulture),
            ToLocal(period.Start, zone),
            ToLocal(period.End, zone));

    private static TimePeriodData ToTimePeriod(TimePeriod period, DateTimeZone zone) =>
        new(
            period.Kind.ToString(),
            period.Group.ToString(),
            period.Nature == PeriodNature.Auspicious,
            ToLocal(period.Start, zone),
            ToLocal(period.End, zone));

    /// <summary>Rounds to the nearest second: calculation accuracy is well below a second's display value.</summary>
    private static DateTimeOffset ToLocal(Instant instant, DateTimeZone zone)
    {
        var rounded = Instant.FromUnixTimeSeconds((long)Math.Round(instant.ToUnixTimeTicks() / (double)NodaConstants.TicksPerSecond));
        return rounded.InZone(zone).ToDateTimeOffset();
    }
}
