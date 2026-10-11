using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Elements;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Traditions;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Panchang.Calculation;

/// <summary>
/// Composes sun events, Panchang elements and the tradition's time periods into one Panchang day.
/// </summary>
internal sealed class PanchangCalculator(
    ISunEventCalculator sunEvents,
    PanchangElementCalculator elements,
    PanchangTraditionStrategyProvider traditions) : IPanchangCalculator
{
    public PanchangDay Calculate(PanchangCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tradition = traditions.GetStrategy(request.Tradition);
        var sun = CalculateSunEvents(request, tradition);

        var nakshatras = elements.CalculateNakshatras(sun.Sunrise, sun.NextSunrise, tradition.Ayanamsa);
        var context = new PanchangDayContext(request.Date.DayOfWeek, sun, nakshatras);

        return new PanchangDay(
            request.Date,
            request.Date.DayOfWeek,
            request.TimeZone,
            sun,
            elements.CalculateTithis(sun.Sunrise, sun.NextSunrise),
            nakshatras,
            elements.CalculateYogas(sun.Sunrise, sun.NextSunrise, tradition.Ayanamsa),
            elements.CalculateKaranas(sun.Sunrise, sun.NextSunrise),
            tradition.CalculatePeriods(context));
    }

    private SunEvents CalculateSunEvents(PanchangCalculationRequest request, IPanchangTraditionStrategy tradition)
    {
        var definition = tradition.SunriseDefinition;
        var sun = new SunEvents(
            PreviousSunset: sunEvents.CalculateSunset(request.Date.PlusDays(-1), request.Location, request.TimeZone, definition),
            Sunrise: sunEvents.CalculateSunrise(request.Date, request.Location, request.TimeZone, definition),
            Sunset: sunEvents.CalculateSunset(request.Date, request.Location, request.TimeZone, definition),
            NextSunrise: sunEvents.CalculateSunrise(request.Date.PlusDays(1), request.Location, request.TimeZone, definition));

        if (!(sun.PreviousSunset < sun.Sunrise && sun.Sunrise < sun.Sunset && sun.Sunset < sun.NextSunrise))
        {
            throw new CalculationException("Sun events are not in chronological order.");
        }

        return sun;
    }
}
