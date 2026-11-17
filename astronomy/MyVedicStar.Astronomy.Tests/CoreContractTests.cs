using MyVedicStar.Astronomy.Coordinates;
using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Events;
using MyVedicStar.Astronomy.Houses;
using MyVedicStar.Astronomy.Jpl.Provider;

namespace MyVedicStar.Astronomy.Tests;

public sealed class CoreContractTests
{
    [Theory]
    [InlineData(-90.0, 270.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(359.9, 359.9)]
    [InlineData(360.0, 0.0)]
    [InlineData(720.5, 0.5)]
    [InlineData(-0.5, 359.5)]
    public void Angles_normalise_into_a_single_turn(double input, double expected) =>
        Assert.Equal(expected, AngleMath.Normalize(input), 9);

    [Fact]
    public void Difference_is_the_shortest_signed_arc()
    {
        Assert.Equal(10.0, AngleMath.Difference(20.0, 10.0), 9);
        Assert.Equal(-10.0, AngleMath.Difference(10.0, 20.0), 9);
        Assert.Equal(5.0, AngleMath.Difference(2.0, 357.0), 9);
        Assert.Equal(-5.0, AngleMath.Difference(357.0, 2.0), 9);
    }

    [Fact]
    public void Non_finite_angles_are_rejected_rather_than_normalised_silently()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AngleMath.Normalize(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => AngleMath.Normalize(double.PositiveInfinity));
    }

    [Fact]
    public void Sidereal_time_is_deterministic_and_advances_by_just_under_a_degree_per_solar_day()
    {
        var day = new JulianDayUt(2451545.0);
        var gmst0 = SiderealTime.GreenwichMeanSiderealTimeDegrees(day);
        var gmst1 = SiderealTime.GreenwichMeanSiderealTimeDegrees(new JulianDayUt(2451546.0));

        // Same input, same output: no ambient state inside the calculation (AC-7).
        Assert.Equal(gmst0, SiderealTime.GreenwichMeanSiderealTimeDegrees(day), 12);
        Assert.Equal(0.985647, AngleMath.Difference(gmst1, gmst0), 3);
    }

    [Fact]
    public void Ecliptic_to_equatorial_is_identity_at_zero_obliquity()
    {
        var point = FrameConversion.EclipticToEquatorial(123.4, 0.0, 0.0);
        Assert.Equal(123.4, point.RightAscensionDegrees, 9);
        Assert.Equal(0.0, point.DeclinationDegrees, 9);
    }

    [Fact]
    public void Ascendant_rejects_an_invalid_observer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HouseGeometry.AscendantLongitude(JulianDayUt.J2000, new ObserverLocation(120.0, 0.0, 0.0), 23.44));
    }

    [Fact]
    public void Capability_boundary_is_published_as_data_including_the_unsupported_list()
    {
        var report = AstronomyCapabilities.Report();

        Assert.Contains(CelestialBody.Pluto, report.SupportedBodies);
        Assert.Contains(NodeConvention.True, report.SupportedNodeConventions);
        Assert.Contains(NodeConvention.Mean, report.SupportedNodeConventions);
        Assert.Equal(new DateOnly(1800, 1, 1), report.MinimumSupportedDate);
        Assert.Equal(new DateOnly(2200, 12, 31), report.MaximumSupportedDate);

        // Absence must never be read as support: the unsupported list is explicit and non-empty.
        Assert.NotEmpty(report.UnsupportedCapabilities);
        Assert.Contains(report.UnsupportedCapabilities, c => c.Contains("1800-01-01", StringComparison.Ordinal));
        Assert.Contains(report.UnsupportedCapabilities, c => c.Contains("download", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(1799, 12, 31, false)]
    [InlineData(1800, 1, 1, true)]
    [InlineData(2100, 6, 15, true)]
    [InlineData(2200, 12, 31, true)]
    [InlineData(2201, 1, 1, false)]
    public void Supported_date_span_is_enforced(int year, int month, int day, bool supported) =>
        Assert.Equal(supported, AstronomyCapabilities.IsSupportedDate(new DateOnly(year, month, day)));

    [Fact]
    public void An_unsupported_body_is_refused_rather_than_omitted()
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        Assert.Throws<AstronomyNotSupportedException>(() =>
            engine.GetPositions(JulianDayUt.J2000, new AstronomySettings(), [(CelestialBody)(-1)]));
    }

    [Fact]
    public void Requesting_no_bodies_is_a_caller_error_not_an_empty_success()
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        Assert.Throws<ArgumentException>(() =>
            engine.GetPositions(JulianDayUt.J2000, new AstronomySettings(), []));
    }

    [Fact]
    public void A_missing_native_toolkit_fails_loudly_instead_of_substituting_another_route()
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        if (engine.IsNativeToolkitAvailable) return; // The image supplies CSPICE in production.

        var failure = Assert.Throws<SpiceUnavailableException>(() =>
            engine.GetPositions(JulianDayUt.J2000, new AstronomySettings(), [CelestialBody.Sun]));
        Assert.Contains("never downloads", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Altitude_solver_finds_a_rising_crossing_and_reports_absence_honestly()
    {
        // A synthetic altitude curve crossing zero at t=0.25 days keeps the solver itself under test, independent of
        // any ephemeris.
        double Altitude(JulianDayUt day) => (day.Value - 2451545.0 - 0.25) * 100.0;

        var found = AltitudeCrossingSolver.FindCrossing(Altitude, JulianDayUt.J2000, 1.0, 0.05, 0.0,
            EventKind.Rise, rising: true);
        Assert.True(found.Occurred);
        Assert.Equal(2451545.25, found.Time.Value, 6);

        // Never crossing in the window is a real answer, not an exception and not a fabricated time.
        var absent = AltitudeCrossingSolver.FindCrossing(_ => 5.0, JulianDayUt.J2000, 1.0, 0.05, 0.0,
            EventKind.Rise, rising: true);
        Assert.False(absent.Occurred);
    }
}
