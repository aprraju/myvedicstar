using NodaTime;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// Checks the astronomical primitives against the worked examples in Meeus, "Astronomical Algorithms" (2nd ed.).
/// </summary>
public sealed class AstronomicalAlgorithmTests
{
    [Fact]
    public void JulianDay_J2000Epoch_Is2451545()
    {
        var instant = Instant.FromUtc(2000, 1, 1, 12, 0);

        Assert.Equal(2451545.0, JulianDay.FromInstant(instant), precision: 9);
        Assert.Equal(instant, JulianDay.ToInstant(2451545.0));
    }

    [Fact]
    public void SolarPosition_MatchesMeeusExample25a()
    {
        // 1992 October 13.0 TD. Meeus: true longitude 199.90988°, apparent δ = −7.78507°, α = 198.38083°.
        const double julianEphemerisDay = 2448908.5;

        var longitude = SolarPosition.MeanEquinoxLongitude(julianEphemerisDay);
        var equatorial = SolarPosition.ApparentEquatorial(julianEphemerisDay);

        Assert.Equal(199.90988 - 0.00569, longitude, precision: 3);
        Assert.Equal(-7.78507, equatorial.Declination, precision: 2);
        Assert.Equal(198.38083, equatorial.RightAscension, precision: 2);
    }

    [Fact]
    public void LunarPosition_MatchesMeeusExample47a()
    {
        // 1992 April 12.0 TD. Meeus: λ = 133.162655° (mean equinox of date, before nutation).
        var longitude = LunarPosition.MeanEquinoxLongitude(2448724.5);

        Assert.InRange(longitude, 133.162655 - 0.0005, 133.162655 + 0.0005);
    }

    [Fact]
    public void SiderealTime_MatchesMeeusExample12a()
    {
        // 1987 April 10, 0h UT: θ0 = 13h10m46.3668s = 197.693195°.
        Assert.Equal(197.693195, SiderealTime.GreenwichMeanDegrees(2446895.5), precision: 4);
    }

    [Fact]
    public void DeltaT_AtYear2000_IsAbout64Seconds()
    {
        Assert.InRange(DeltaT.GetSeconds(JulianDay.J2000), 63.5, 64.2);
    }

    /// <summary>
    /// Every era of the Espenak–Meeus expressions, including the long-term parabola used just before 1900 (the day
    /// before 1900-01-01 needs the previous sunset) and after 2150.
    /// </summary>
    [Theory]
    [InlineData(1850.0, -17.12)]
    [InlineData(1900.0, -2.79)]
    [InlineData(1930.0, 24.13)]
    [InlineData(1955.0, 31.05)]
    [InlineData(1980.0, 50.51)]
    [InlineData(2010.0, 66.70)]
    [InlineData(2100.0, 202.74)]
    [InlineData(2200.0, 442.08)]
    public void DeltaT_FollowsTheEspenakMeeusExpressions(double year, double expectedSeconds)
    {
        var julianDay = JulianDay.J2000 + ((year - 2000) * 365.25);

        Assert.Equal(expectedSeconds, DeltaT.GetSeconds(julianDay), precision: 1);
    }

    [Fact]
    public void LahiriAyanamsa_AtJ2000_Is23Point857Degrees()
    {
        // Swiss Ephemeris SE_SIDM_LAHIRI mean ayanamsa at J2000: 23.857°.
        Assert.InRange(new LahiriAyanamsa().GetDegrees(JulianDay.J2000), 23.856, 23.858);
    }
}
