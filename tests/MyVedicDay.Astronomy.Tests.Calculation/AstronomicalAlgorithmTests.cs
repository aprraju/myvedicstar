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
    public void SolarPosition_MatchesMeeusExample25b()
    {
        // 1992 October 13.0 TD, VSOP87. Meeus: apparent λ = 199°54′21.818″ with Δψ = +15.908″,
        // α = 13h13m30.749s = 198.378121°, δ = −7°47′01.74″ = −7.783817°.
        const double julianEphemerisDay = 2448908.5;

        var longitude = SolarPosition.MeanEquinoxLongitude(julianEphemerisDay);
        var equatorial = SolarPosition.ApparentEquatorial(julianEphemerisDay);

        Assert.InRange(longitude, 199.9016410 - 0.0001, 199.9016410 + 0.0001);
        Assert.Equal(-7.783817, equatorial.Declination, precision: 3);
        Assert.Equal(198.378121, equatorial.RightAscension, precision: 3);
    }

    /// <summary>
    /// Equinoxes and solstices (apparent solar longitude 0°, 90°, 180°, 270°) against the published instants, which
    /// bound the error of every Sankranti and of the solar term in Tithi times.
    /// </summary>
    [Theory]
    [InlineData(2025, 3, 20, 9, 1, 0)]
    [InlineData(2025, 6, 21, 2, 42, 90)]
    [InlineData(2025, 9, 22, 18, 19, 180)]
    [InlineData(2025, 12, 21, 15, 3, 270)]
    [InlineData(2026, 3, 20, 14, 46, 0)]
    [InlineData(2026, 6, 21, 8, 24, 90)]
    public void SolarLongitude_ReachesTheCardinalPointsAtThePublishedInstants(int year, int month, int day, int hour, int minute, double target)
    {
        var expected = Instant.FromUtc(year, month, day, hour, minute);
        var ephemeris = new MeeusEphemeris();

        var crossing = AngularCrossingFinder.FindCrossing(
            julianDay => AngleMath.NormalizeDegrees(
                ephemeris.GetSunLongitude(julianDay) + Nutation.InLongitude(DeltaT.ToJulianEphemerisDay(julianDay))),
            target,
            JulianDay.FromInstant(expected));

        var error = JulianDay.ToInstant(crossing) - expected;
        Assert.True(Duration.Max(error, -error) < Duration.FromMinutes(1.5), $"Off by {error.TotalMinutes:0.0} min.");
    }

    [Fact]
    public void LunarPosition_MatchesMeeusExample47a()
    {
        // 1992 April 12.0 TD. Meeus: λ = 133.162655° (mean equinox of date, before nutation).
        var longitude = LunarPosition.MeanEquinoxLongitude(2448724.5);

        Assert.InRange(longitude, 133.162655 - 0.0005, 133.162655 + 0.0005);
    }

    [Fact]
    public void LunarPosition_LatitudeDistanceAndParallax_MatchMeeusExample47a()
    {
        // 1992 April 12.0 TD. Meeus: β = −3.229126°, Δ = 368409.7 km, π = 0.991990°.
        var position = LunarPosition.Position(2448724.5);

        Assert.InRange(position.Longitude, 133.162655 - 0.0005, 133.162655 + 0.0005);
        Assert.InRange(position.Latitude, -3.229126 - 0.0005, -3.229126 + 0.0005);
        Assert.InRange(position.DistanceKm, 368409.7 - 1, 368409.7 + 1);
        Assert.InRange(LunarPosition.HorizontalParallax(position.DistanceKm), 0.991990 - 0.0001, 0.991990 + 0.0001);
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
    public void LahiriAyanamsa_MatchesTheValueDrikPanchangPublishes()
    {
        // Drik Panchang, New Delhi, 2025-10-20 (sunrise 00:55 UT): Lahiri Ayanamsha 24.224241°.
        var sunrise = JulianDay.FromInstant(Instant.FromUtc(2025, 10, 20, 0, 55));

        Assert.InRange(new LahiriAyanamsa().GetDegrees(sunrise), 24.224241 - 0.000005, 24.224241 + 0.000005);
    }

    [Fact]
    public void LahiriAyanamsa_AtJ2000_IsTheSwissEphemerisValuePlusDriksOffset()
    {
        // Swiss Ephemeris SE_SIDM_LAHIRI mean ayanamsa at J2000: 23.857092°, plus Drik Panchang's 24.1″.
        Assert.InRange(new LahiriAyanamsa().GetDegrees(JulianDay.J2000), 23.8638 - 0.0003, 23.8638 + 0.0003);
    }
}
