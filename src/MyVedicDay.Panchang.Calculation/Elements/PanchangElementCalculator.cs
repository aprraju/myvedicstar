using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Panchang.Calculation.Elements;

/// <summary>
/// Calculates the Panchang limbs (Tithi, Nakshatra, Yoga, Karana) from solar and lunar longitudes.
/// These are astronomical definitions; the only tradition-dependent input is the ayanamsa.
/// </summary>
internal sealed class PanchangElementCalculator(IEphemeris ephemeris)
{
    private const double NakshatraSpan = 360d / 27d;

    // Mean rates in degrees per day, used only to seed the crossing search.
    private const double MeanElongationRate = 12.19;
    private const double MeanMoonRate = 13.18;
    private const double MeanYogaRate = 14.17;

    // A sunrise-to-sunrise window never holds more than a handful of any element; this guards against runaway loops.
    private const int MaxPeriodsPerWindow = 8;

    public IReadOnlyList<ElementPeriod<Tithi>> CalculateTithis(Instant from, Instant to) =>
        CalculatePeriods(Elongation, 12d, 30, MeanElongationRate, index => (Tithi)(index + 1), from, to);

    public IReadOnlyList<ElementPeriod<Karana>> CalculateKaranas(Instant from, Instant to) =>
        CalculatePeriods(Elongation, 6d, 60, MeanElongationRate, ToKarana, from, to);

    public IReadOnlyList<ElementPeriod<Nakshatra>> CalculateNakshatras(Instant from, Instant to, IAyanamsa ayanamsa) =>
        CalculatePeriods(
            jd => AngleMath.NormalizeDegrees(ephemeris.GetMoonLongitude(jd) - ayanamsa.GetDegrees(jd)),
            NakshatraSpan,
            27,
            MeanMoonRate,
            index => (Nakshatra)(index + 1),
            from,
            to);

    public IReadOnlyList<ElementPeriod<Yoga>> CalculateYogas(Instant from, Instant to, IAyanamsa ayanamsa) =>
        CalculatePeriods(
            jd => AngleMath.NormalizeDegrees(
                ephemeris.GetSunLongitude(jd) + ephemeris.GetMoonLongitude(jd) - (2 * ayanamsa.GetDegrees(jd))),
            NakshatraSpan,
            27,
            MeanYogaRate,
            index => (Yoga)(index + 1),
            from,
            to);

    /// <summary>
    /// Karana 1 is Kimstughna, Karanas 2–57 cycle through the seven movable Karanas, and 58–60 are
    /// Shakuni, Chatushpada and Naga.
    /// </summary>
    private static Karana ToKarana(int index) => index switch
    {
        0 => Karana.Kimstughna,
        57 => Karana.Shakuni,
        58 => Karana.Chatushpada,
        59 => Karana.Naga,
        _ => (Karana)((int)Karana.Bava + ((index - 1) % 7)),
    };

    private double Elongation(double julianDay) =>
        AngleMath.NormalizeDegrees(ephemeris.GetMoonLongitude(julianDay) - ephemeris.GetSunLongitude(julianDay));

    private static IReadOnlyList<ElementPeriod<TElement>> CalculatePeriods<TElement>(
        Func<double, double> angleAt,
        double span,
        int count,
        double meanRate,
        Func<int, TElement> toElement,
        Instant from,
        Instant to)
        where TElement : struct, Enum
    {
        var fromJulianDay = JulianDay.FromInstant(from);
        var toJulianDay = JulianDay.FromInstant(to);

        var angle = angleAt(fromJulianDay);
        var index = (int)Math.Floor(angle / span) % count;
        var startJulianDay = AngularCrossingFinder.FindCrossing(
            angleAt, index * span, fromJulianDay - ((angle - (index * span)) / meanRate));

        var periods = new List<ElementPeriod<TElement>>();
        while (true)
        {
            var nextIndex = (index + 1) % count;
            var endJulianDay = AngularCrossingFinder.FindCrossing(angleAt, nextIndex * span, startJulianDay + (span / meanRate));
            if (endJulianDay <= startJulianDay)
            {
                throw new CalculationException("Panchang element transitions are out of order.");
            }

            periods.Add(new ElementPeriod<TElement>(
                toElement(index), JulianDay.ToInstant(startJulianDay), JulianDay.ToInstant(endJulianDay)));

            if (endJulianDay >= toJulianDay)
            {
                return periods;
            }

            if (periods.Count >= MaxPeriodsPerWindow)
            {
                throw new CalculationException("Too many Panchang element transitions in one day.");
            }

            startJulianDay = endJulianDay;
            index = nextIndex;
        }
    }
}
