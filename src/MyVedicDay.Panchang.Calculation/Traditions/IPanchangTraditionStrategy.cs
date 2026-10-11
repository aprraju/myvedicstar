using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Panchang.Calculation.Traditions;

/// <summary>
/// Encapsulates the conventions that differ between Panchang traditions: how sunrise is defined, which ayanamsa
/// is used, and how Muhurta-style periods are derived. Astronomical facts stay in the engine; interpretation lives here.
/// </summary>
internal interface IPanchangTraditionStrategy
{
    string Key { get; }

    SunriseDefinition SunriseDefinition { get; }

    IAyanamsa Ayanamsa { get; }

    IReadOnlyList<TimePeriod> CalculatePeriods(PanchangDayContext context);
}

/// <summary>
/// Astronomical inputs a tradition needs to derive its time periods.
/// </summary>
internal sealed record PanchangDayContext(
    IsoDayOfWeek Weekday,
    SunEvents Sun,
    IReadOnlyList<ElementPeriod<Nakshatra>> Nakshatras);
