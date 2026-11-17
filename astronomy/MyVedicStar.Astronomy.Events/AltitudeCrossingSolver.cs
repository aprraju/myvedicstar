using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Events;

/// <summary>
/// Deterministic numerical root finding for altitude-crossing events.
/// </summary>
/// <remarks>
/// Bracketing is fixed and documented so a result is reproducible (policy B4): a forward scan of
/// <see cref="ScanStepDays"/>, then bisection on the first bracketing pair with at most
/// <see cref="MaxIterations"/> halvings or until the bracket is under <see cref="ToleranceDays"/>.
/// A bracket that does not converge, or an altitude function that never crosses, is reported as such - never
/// replaced by an interpolation, because AC-2 forbids the substitution.
/// </remarks>
public static class AltitudeCrossingSolver
{
    /// <summary>Forward scan resolution. Ten minutes is fine enough that no real altitude crossing is skipped except
    /// at extreme polar latitudes, where non-occurrence is the correct answer anyway.</summary>
    public const double ScanStepDays = 10.0 / 1440.0;

    /// <summary>Bisection cap. 60 halvings take a 10-minute bracket below a microsecond, so the cap is a safety net
    /// rather than the usual exit.</summary>
    public const int MaxIterations = 60;

    /// <summary>Convergence tolerance: one second of time.</summary>
    public const double ToleranceDays = 1.0 / 86400.0;

    /// <summary>
    /// Finds the first altitude crossing inside <paramref name="windowDays"/> from <paramref name="start"/>.
    /// </summary>
    /// <param name="altitudeDegrees">Altitude of the body at an instant, in degrees.</param>
    /// <param name="rising">True for a rising crossing (altitude increasing through the target), false for setting.
    /// </param>
    /// <param name="targetAltitudeDegrees">The altitude considered "on the horizon", including refraction and dip as
    /// the caller chooses.</param>
    /// <param name="nonOccurrenceReason">Reason to report if no crossing exists in the window.</param>
    public static AstronomyEventResult Find(
        Func<JulianDayUt, double> altitudeDegrees,
        JulianDayUt start,
        double windowDays,
        double targetAltitudeDegrees,
        EventType type,
        bool rising,
        NonOccurrenceReason nonOccurrenceReason,
        EventProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(altitudeDegrees);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowDays);
        if (!start.IsFinite) throw new ArgumentOutOfRangeException(nameof(start), "Start instant must be finite.");

        var previous = altitudeDegrees(start) - targetAltitudeDegrees;
        var previousInstant = start.Value;

        for (var elapsed = ScanStepDays; elapsed <= windowDays; elapsed += ScanStepDays)
        {
            var currentInstant = start.Value + elapsed;
            var current = altitudeDegrees(new JulianDayUt(currentInstant)) - targetAltitudeDegrees;

            var crossed = rising ? previous < 0 && current >= 0 : previous > 0 && current <= 0;
            if (crossed)
                return Bisect(altitudeDegrees, previousInstant, currentInstant, targetAltitudeDegrees, rising, type,
                    provenance);

            previous = current;
            previousInstant = currentInstant;
        }

        // No crossing in the window is a real answer (polar day/night, circumpolar body), not a failure.
        return AstronomyEventResult.DidNotOccur(type, nonOccurrenceReason,
            new ConvergenceDiagnostics(ScanStepDays, 0, 0, ToleranceDays, Converged: false), provenance);
    }

    private static AstronomyEventResult Bisect(Func<JulianDayUt, double> altitudeDegrees, double low, double high,
        double targetAltitudeDegrees, bool rising, EventType type, EventProvenance provenance)
    {
        var initialWidth = high - low;
        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var middle = (low + high) / 2.0;
            var middleValue = altitudeDegrees(new JulianDayUt(middle)) - targetAltitudeDegrees;

            if (middleValue == 0.0 || high - low < ToleranceDays)
            {
                var instant = new JulianDayUt((low + high) / 2.0);
                return new AstronomyEventResult(type, instant, NonOccurrenceReason.None,
                    new ConvergenceDiagnostics(ScanStepDays, initialWidth, iteration, ToleranceDays, Converged: true),
                    provenance);
            }

            var goHigher = rising ? middleValue < 0 : middleValue > 0;
            if (goHigher) low = middle;
            else high = middle;
        }

        // Exhausted the cap without converging: an error result, never an interpolated fallback.
        return new AstronomyEventResult(type, null, NonOccurrenceReason.BodyNeverCrosses,
            new ConvergenceDiagnostics(ScanStepDays, initialWidth, MaxIterations, ToleranceDays, Converged: false),
            provenance);
    }
}
