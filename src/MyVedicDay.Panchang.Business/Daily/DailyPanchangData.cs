using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Panchang.Business.Daily;

/// <summary>
/// Language-neutral daily Panchang: the unit that is cached. Holds stable keys only; names, meanings and
/// classifications are applied per request, so one cache entry serves every language.
/// </summary>
internal sealed record DailyPanchangData(
    DateOnly Date,
    DayOfWeek Weekday,
    SunTimes Sun,
    IReadOnlyList<ElementPeriodData> Tithis,
    IReadOnlyList<ElementPeriodData> Nakshatras,
    IReadOnlyList<ElementPeriodData> Yogas,
    IReadOnlyList<ElementPeriodData> Karanas,
    IReadOnlyList<TimePeriodData> Periods);

internal sealed record ElementPeriodData(string Key, int Number, DateTimeOffset Start, DateTimeOffset End);

internal sealed record TimePeriodData(string Kind, string Group, bool IsAuspicious, DateTimeOffset Start, DateTimeOffset End);
