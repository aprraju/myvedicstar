using NodaTime;

namespace MyVedicDay.Panchang.Calculation.Models;

/// <summary>
/// Calculated Panchang for one local calendar date. All times are absolute instants; the Panchang day runs
/// from <see cref="SunEvents.Sunrise"/> to <see cref="SunEvents.NextSunrise"/>, so periods may start before
/// local midnight of the previous date or end after local midnight of the next date.
/// </summary>
public sealed record PanchangDay(
    LocalDate Date,
    IsoDayOfWeek Weekday,
    DateTimeZone TimeZone,
    SunEvents Sun,
    IReadOnlyList<ElementPeriod<Tithi>> Tithis,
    IReadOnlyList<ElementPeriod<Nakshatra>> Nakshatras,
    IReadOnlyList<ElementPeriod<Yoga>> Yogas,
    IReadOnlyList<ElementPeriod<Karana>> Karanas,
    IReadOnlyList<TimePeriod> Periods);

/// <summary>Sun events that bound the Panchang day and its day/night divisions.</summary>
public sealed record SunEvents(Instant PreviousSunset, Instant Sunrise, Instant Sunset, Instant NextSunrise);

/// <summary>
/// An element (Tithi, Nakshatra, Yoga, Karana) with its exact start and end. Lists of periods are ordered and
/// contiguous: the first period is the one prevailing at sunrise, later periods are the transitions that follow.
/// </summary>
public sealed record ElementPeriod<TElement>(TElement Element, Instant Start, Instant End)
    where TElement : struct, Enum;

/// <summary>A classified time period such as Rahu Kalam, Abhijit Muhurta or a Choghadiya segment.</summary>
public sealed record TimePeriod(PanchangPeriodKind Kind, PeriodNature Nature, PeriodGroup Group, Instant Start, Instant End);
