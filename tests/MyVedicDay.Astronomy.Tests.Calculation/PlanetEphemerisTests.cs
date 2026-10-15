using NodaTime;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// Planet positions against published conjunctions, oppositions and greatest elongations (UT dates), which bound the
/// error of every combustion boundary.
/// </summary>
public sealed class PlanetEphemerisTests
{
    private static readonly IPlanetEphemeris Ephemeris = new KeplerianPlanetEphemeris();

    /// <summary>
    /// The elongation changes sign at a conjunction with the Sun, and passes ±180° at an opposition. References are UT
    /// dates (taken at noon), so ±0.8 days allows for the time of day; the actual errors are a few hours at most.
    /// </summary>
    [Theory]
    [InlineData(Planet.Jupiter, 2024, 5, 18, 0)]
    [InlineData(Planet.Jupiter, 2025, 6, 24, 0)]
    [InlineData(Planet.Jupiter, 2026, 7, 29, 0)]
    [InlineData(Planet.Jupiter, 2024, 12, 7, 180)]
    [InlineData(Planet.Jupiter, 2026, 1, 10, 180)]
    [InlineData(Planet.Venus, 2023, 8, 13, 0)]
    [InlineData(Planet.Venus, 2025, 3, 23, 0)]
    [InlineData(Planet.Venus, 2024, 6, 4, 0)]
    [InlineData(Planet.Venus, 2026, 1, 6, 0)]
    public void ConjunctionsAndOppositions_FallOnThePublishedDate(Planet planet, int year, int month, int day, double target)
    {
        var published = JulianDay.FromInstant(Instant.FromUtc(year, month, day, 12, 0));
        double Offset(double julianDay) => AngleMath.NormalizeSignedDegrees(Ephemeris.GetElongation(planet, julianDay) - target);

        var crossing = FindSignChange(Offset, published - 10, published + 10);

        Assert.NotNull(crossing);
        Assert.InRange(crossing.Value, published - 0.8, published + 0.8);
    }

    /// <summary>The first instant in [from, to] where <paramref name="f"/> changes sign (stepping by 6 hours, then bisecting).</summary>
    private static double? FindSignChange(Func<double, double> f, double from, double to)
    {
        const double step = 0.25;
        for (var start = from; start < to; start += step)
        {
            var (low, high) = (start, start + step);
            var lowSign = Math.Sign(f(low));
            if (lowSign == Math.Sign(f(high)) || Math.Abs(f(low) - f(high)) > 90)
            {
                continue; // no crossing, or the ±180° wrap rather than a crossing
            }

            for (var i = 0; i < 30; i++)
            {
                var middle = (low + high) / 2;
                (low, high) = Math.Sign(f(middle)) == lowSign ? (middle, high) : (low, middle);
            }

            return (low + high) / 2;
        }

        return null;
    }

    [Fact]
    public void Venus_GreatestEasternElongation2025_IsAbout47Degrees()
    {
        // 2025-01-10: 47.2° east of the Sun.
        var julianDay = JulianDay.FromInstant(Instant.FromUtc(2025, 1, 10, 0, 0));
        var maximum = Enumerable.Range(-5, 11).Max(offset => Ephemeris.GetElongation(Planet.Venus, julianDay + offset));

        Assert.InRange(maximum, 46.9, 47.5);
        Assert.InRange(Ephemeris.GetElongation(Planet.Venus, julianDay), 46.9, 47.5);
    }

    [Fact]
    public void Venus_IsRetrogradeAroundInferiorConjunction_AndDirectAroundSuperior()
    {
        Assert.True(Ephemeris.IsRetrograde(Planet.Venus, JulianDay.FromInstant(Instant.FromUtc(2025, 3, 23, 0, 0))));
        Assert.False(Ephemeris.IsRetrograde(Planet.Venus, JulianDay.FromInstant(Instant.FromUtc(2026, 1, 6, 0, 0))));
    }
}
