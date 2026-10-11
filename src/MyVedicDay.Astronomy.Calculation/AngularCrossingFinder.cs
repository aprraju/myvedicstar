using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Finds the moment a steadily increasing angle (e.g. Moon–Sun elongation) reaches a target value,
/// using Newton iteration with a numerical derivative. Used for element transitions such as Tithi and Nakshatra.
/// </summary>
public static class AngularCrossingFinder
{
    private const double ToleranceDays = 0.1 / 86400d;
    private const double DerivativeStepDays = 1d / 24d;
    private const int MaxIterations = 30;

    /// <param name="angleAt">Angle in degrees [0, 360) as a function of Julian Day (UT). Must increase monotonically.</param>
    /// <param name="targetDegrees">Angle whose crossing is sought.</param>
    /// <param name="initialJulianDay">Estimate of the crossing time; must be within half a revolution of the answer.</param>
    public static double FindCrossing(Func<double, double> angleAt, double targetDegrees, double initialJulianDay)
    {
        var julianDay = initialJulianDay;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var error = AngleMath.NormalizeSignedDegrees(targetDegrees - angleAt(julianDay));
            var rate = AngleMath.NormalizeSignedDegrees(
                angleAt(julianDay + DerivativeStepDays) - angleAt(julianDay - DerivativeStepDays)) / (2 * DerivativeStepDays);

            if (rate <= 0)
            {
                throw new CalculationException("Angular crossing search encountered a non-increasing angle.");
            }

            var step = error / rate;
            julianDay += step;

            if (Math.Abs(step) < ToleranceDays)
            {
                return julianDay;
            }
        }

        throw new CalculationException("Angular crossing search did not converge.");
    }
}
