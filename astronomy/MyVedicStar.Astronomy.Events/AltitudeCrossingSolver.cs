using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Events;

/// <summary>Kinds of boundary the event finder resolves.</summary>
public enum EventKind { Rise, Set, Transit }

/// <summary>A resolved event instant, expressed as a Julian Day UT so callers control presentation.</summary>
public readonly record struct AstronomyEvent(EventKind Kind, JulianDayUt Time, bool Occurred);

/// <summary>
/// Numerical root finding for altitude-crossing events over a body position function. Kept generic so the same
/// solver serves sunrise, moonrise and planetary ingress without duplicating search logic.
/// </summary>
public static class AltitudeCrossingSolver
{
    /// <summary>
    /// Finds the first instant in [startDays, startDays + windowDays) where the supplied altitude function crosses
    /// the target altitude, searching at <paramref name="stepDays"/> resolution and then bisecting the bracket.
    /// Returns Occurred=false when no crossing exists in the window, which is a real answer at high latitude rather
    /// than a failure.
    /// </summary>
    public static AstronomyEvent FindCrossing(Func<JulianDayUt, double> altitudeDegrees, JulianDayUt start,
        double windowDays, double stepDays, double targetAltitudeDegrees, EventKind kind, bool rising)
    {
        ArgumentNullException.ThrowIfNull(altitudeDegrees);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowDays);
        if (stepDays <= 0 || stepDays > windowDays) throw new ArgumentOutOfRangeException(nameof(stepDays));

        var previous = altitudeDegrees(start) - targetAltitudeDegrees;
        var t = start.Value;
        for (var elapsed = stepDays; elapsed <= windowDays; elapsed += stepDays)
        {
            var currentDay = start.Value + elapsed;
            var current = altitudeDegrees(new JulianDayUt(currentDay)) - targetAltitudeDegrees;
            var crossed = rising ? previous < 0 && current >= 0 : previous > 0 && current <= 0;
            if (crossed)
            {
                var bracketLow = t;
                var bracketHigh = currentDay;
                var lowValue = previous;
                for (var i = 0; i < 60; i++)
                {
                    var middle = (bracketLow + bracketHigh) / 2.0;
                    var middleValue = altitudeDegrees(new JulianDayUt(middle)) - targetAltitudeDegrees;
                    if ((rising && middleValue < 0) || (!rising && middleValue > 0))
                    {
                        bracketLow = middle;
                        lowValue = middleValue;
                    }
                    else
                    {
                        bracketHigh = middle;
                    }
                    if (bracketHigh - bracketLow < 1e-9) break;
                }
                return new AstronomyEvent(kind, new JulianDayUt((bracketLow + bracketHigh) / 2.0), true);
            }
            previous = current;
            t = currentDay;
        }
        return new AstronomyEvent(kind, new JulianDayUt(start.Value), false);
    }
}
