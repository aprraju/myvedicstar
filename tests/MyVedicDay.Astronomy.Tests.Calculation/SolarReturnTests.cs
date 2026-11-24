using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using Xunit;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// api#210: the solar-return instant must be verifiable WITHOUT trusting the solver.
/// </summary>
/// <remarks>
/// The primary assertion is a round trip: take the returned instant, re-run the engine there, and require the sidereal
/// longitude to equal the natal value. That single check fails on a sign error (subtracting the ayanamsa instead of
/// adding it), on a wrong ayanamsa (the value lands off by the difference), and on an off-by-a-year (the Sun is a
/// different longitude a year earlier), which is why it is the test rather than three separate ones.
/// </remarks>
public sealed class SolarReturnTests
{
    /// <summary>
    /// INHERITED, NOT DERIVED. This is ADR-0016's positional claim for the analytical ephemeris, borrowed as a bound on
    /// solver error - it is NOT a Varshaphal accuracy doctrine. A doctrinal tolerance is an open decision on #210 and
    /// must not be read out of this constant: it answers "can the solver find the crossing", not "is this good enough
    /// to publish".
    /// </summary>
    private const double ToleranceDegrees = 3.0 / 3600.0;

    private static (IEphemeris Ephemeris, IAyanamsa Ayanamsa) Engine()
    {
        var services = new ServiceCollection();
        services.AddAstronomyProvider(ProviderSelectionParser.AnalyticalKey);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IEphemeris>(), new LahiriAyanamsa());
    }

    [Theory]
    // Natal values spread around the zodiac so a solver that only works near 0/360 is caught.
    [InlineData(10.0, 2026)]
    [InlineData(95.5, 2026)]
    [InlineData(200.25, 2027)]
    [InlineData(359.9, 2028)]
    public void The_returned_instant_reproduces_the_natal_sun_longitude(double natalSidereal, int year)
    {
        var (ephemeris, ayanamsa) = Engine();

        var returnJulianDay = SolarReturn.Find(natalSidereal, year, ephemeris, ayanamsa);

        Assert.NotNull(returnJulianDay);

        // THE assertion: re-run the ENGINE at the answer and require it to agree with the question.
        //
        // Deliberately NOT via SolarReturn.SiderealLongitudeAt. An earlier version did exactly that and a flipped
        // ayanamsa sign PASSED, because the solver and the check shared one helper - the test was comparing a function
        // to itself. This recomputes from the raw port contract (tropical, mean equinox of date) minus the ayanamsa, so
        // a sign error in the production helper is caught rather than mirrored.
        var actual = AngleMath.NormalizeDegrees(
            ephemeris.GetSunLongitude(returnJulianDay!.Value) - ayanamsa.GetDegrees(returnJulianDay!.Value));
        var error = Math.Abs(AngleMath.NormalizeSignedDegrees(actual - natalSidereal));

        Assert.True(error <= ToleranceDegrees,
            $"Solar return for {natalSidereal} deg in {year} landed at sidereal {actual}, "
            + $"an error of {error * 3600:N2} arcsec (tolerance {ToleranceDegrees * 3600:N0}).");
    }

    [Fact]
    public void The_return_falls_in_the_requested_year_and_not_a_neighbour()
    {
        var (ephemeris, ayanamsa) = Engine();
        const int year = 2027;

        var returnJulianDay = SolarReturn.Find(95.5, year, ephemeris, ayanamsa);
        Assert.NotNull(returnJulianDay);

        var instant = JulianDay.ToInstant(returnJulianDay!.Value);
        Assert.Equal(year, instant.InUtc().Year);

        // A neighbouring year's return must be a DIFFERENT instant, or the bracket is wrong and "the year" is decorative.
        var previous = SolarReturn.Find(95.5, year - 1, ephemeris, ayanamsa);
        Assert.NotNull(previous);
        Assert.NotEqual(returnJulianDay.Value, previous!.Value, 3);
    }

    [Fact]
    public void A_wrong_ayanamsa_visibly_misses_the_target()
    {
        // Falsifies the tolerance rather than the solver: an ayanamsa off by ~1 degree must NOT pass. If this ever starts
        // passing, the round-trip test above has been loosened into meaninglessness.
        var (ephemeris, _) = Engine();
        const double natalSidereal = 95.5;
        const int year = 2027;

        var correct = new LahiriAyanamsa();
        var returnJulianDay = SolarReturn.Find(natalSidereal, year, ephemeris, correct);
        Assert.NotNull(returnJulianDay);

        var shifted = SolarReturn.SiderealLongitudeAt(returnJulianDay!.Value, ephemeris, new ShiftedAyanamsa(correct));
        var error = Math.Abs(AngleMath.NormalizeSignedDegrees(shifted - natalSidereal));

        Assert.True(error > ToleranceDegrees,
            "An ayanamsa off by 1 degree still matched within tolerance; the tolerance is too loose to catch a frame error.");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(360.0)]
    public void A_longitude_outside_the_zodiac_is_refused(double natalSidereal)
    {
        var (ephemeris, ayanamsa) = Engine();
        Assert.Throws<ArgumentOutOfRangeException>(() => SolarReturn.Find(natalSidereal, 2027, ephemeris, ayanamsa));
    }

    /// <summary>Lahiri plus one degree - stands in for "the wrong school" without inventing a second real convention.</summary>
    private sealed class ShiftedAyanamsa(IAyanamsa inner) : IAyanamsa
    {
        public double GetDegrees(double julianDayUt) => inner.GetDegrees(julianDayUt) + 1.0;
    }
}
