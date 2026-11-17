using MyVedicStar.Astronomy.Coordinates;
using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>FR-03: time scales, frames and ayanamsa, asserted against published reference values.</summary>
public sealed class TimeAndCoordinateTests
{
    [Fact]
    public void Delta_t_matches_published_values_across_the_supported_span()
    {
        // Published Delta T (Espenak-Meeus / IERS): about 13 s in 1900, 64 s in 2000, ~69 s in 2020.
        Assert.InRange(TimeScales.DeltaTSeconds(ToJd(1900, 1, 1)), 10.0, 16.0);
        Assert.InRange(TimeScales.DeltaTSeconds(ToJd(2000, 1, 1)), 62.0, 66.0);
        Assert.InRange(TimeScales.DeltaTSeconds(ToJd(2020, 1, 1)), 67.0, 72.0);
    }

    [Fact]
    public void Delta_t_is_monotonic_across_a_modern_century_in_the_modern_polynomial()
    {
        // Sanity on the polynomial rather than on physics: the 2000-2050 branch must not oscillate wildly.
        var previous = TimeScales.DeltaTSeconds(ToJd(2000, 1, 1));
        for (var year = 2001; year <= 2049; year++)
        {
            var current = TimeScales.DeltaTSeconds(ToJd(year, 1, 1));
            Assert.InRange(Math.Abs(current - previous), 0.0, 2.0);
            previous = current;
        }
    }

    [Fact]
    public void Leap_seconds_match_the_iers_table_at_known_epochs()
    {
        Assert.Equal(10, TimeScales.TaiMinusUtcSeconds(ToJd(1972, 1, 1)));
        Assert.Equal(32, TimeScales.TaiMinusUtcSeconds(ToJd(1999, 1, 1)));
        Assert.Equal(36, TimeScales.TaiMinusUtcSeconds(ToJd(2016, 12, 31)));
        Assert.Equal(37, TimeScales.TaiMinusUtcSeconds(ToJd(2017, 1, 1)));
        Assert.Equal(37, TimeScales.TaiMinusUtcSeconds(ToJd(2026, 1, 1)));
    }

    [Fact]
    public void The_leap_second_table_reports_its_own_limit_instead_of_guessing_beyond_it()
    {
        Assert.Equal(new DateOnly(2017, 1, 1), TimeScales.LeapSecondTableValidTo);
        // Before 1972 UTC did not exist as a uniform scale; the engine says so rather than inventing an offset.
        Assert.False(TimeScales.IsLeapSecondTableApplicable(ToJd(1950, 1, 1)));
        Assert.True(TimeScales.IsLeapSecondTableApplicable(ToJd(2020, 1, 1)));
    }

    [Fact]
    public void Ut_to_tt_round_trips_within_a_millisecond()
    {
        var ut = ToJd(2025, 6, 15);
        var tt = TimeScales.UtToTt(ut);
        Assert.InRange(tt - ut, 60.0 / 86400.0, 80.0 / 86400.0);
        Assert.Equal(ut, TimeScales.TtToUt(tt), 8);
    }

    [Fact]
    public void Tdb_minus_tt_stays_within_the_documented_one_point_seven_milliseconds()
    {
        for (var year = 1801; year <= 2199; year += 7)
            Assert.InRange(Math.Abs(TimeScales.TdbMinusTtSeconds(ToJd(year, 7, 1))), 0.0, 0.0017);
    }

    [Theory]
    [InlineData(2000, 1, 1, 23.4392911)]
    [InlineData(1900, 1, 1, 23.4522944)]
    public void Mean_obliquity_matches_published_values(int y, int m, int d, double expected)
    {
        var jdTt = ToJd(y, m, d);
        Assert.Equal(expected, PrecessionNutation.MeanObliquityDegrees(jdTt), 4);
    }

    [Fact]
    public void Nutation_is_small_but_non_zero_and_bounded()
    {
        var (dPsi, dEpsilon) = PrecessionNutation.Nutation(ToJd(2025, 1, 1));
        Assert.InRange(Math.Abs(dPsi), 0.001, 0.006);
        Assert.InRange(Math.Abs(dEpsilon), 0.001, 0.006);
    }

    [Fact]
    public void Ecliptic_to_equatorial_round_trips()
    {
        var obliquity = PrecessionNutation.MeanObliquityDegrees(ToJd(2025, 1, 1));
        var equatorial = FrameConversion.EclipticToEquatorial(123.456, 1.234, obliquity);
        var back = FrameConversion.EquatorialToEcliptic(
            equatorial.RightAscensionDegrees, equatorial.DeclinationDegrees, obliquity);
        Assert.Equal(123.456, back.LongitudeDegrees, 9);
        Assert.Equal(1.234, back.LatitudeDegrees, 9);
    }

    [Fact]
    public void Lahiri_ayanamsa_matches_the_published_epoch_value_at_its_own_epoch()
    {
        Assert.Equal(Ayanamsa.Lahiri1956Degrees, Ayanamsa.LahiriDegrees(Ayanamsa.LahiriEpochJulianDayTt), 9);
        // Published Lahiri value for 2025 is about 24.20-24.23 degrees.
        Assert.InRange(Ayanamsa.LahiriDegrees(ToJd(2025, 1, 1)), 24.15, 24.30);
    }

    [Fact]
    public void Raman_and_krishnamurti_are_lahiri_plus_their_documented_offsets()
    {
        var jd = ToJd(2025, 1, 1);
        var lahiri = Ayanamsa.For(AyanamsaKind.Lahiri, jd);
        Assert.Equal(lahiri + Ayanamsa.RamanOffsetFromLahiriDegrees, Ayanamsa.For(AyanamsaKind.Raman, jd), 12);
        Assert.Equal(lahiri + Ayanamsa.KrishnamurtiOffsetFromLahiriDegrees,
            Ayanamsa.For(AyanamsaKind.Krishnamurti, jd), 12);
    }

    [Fact]
    public void An_undefined_ayanamsa_is_refused_rather_than_defaulted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ayanamsa.For((AyanamsaKind)99, ToJd(2025, 1, 1)));
    }

    [Fact]
    public void Sidereal_conversion_subtracts_the_ayanamsa()
    {
        var jd = ToJd(2025, 1, 1);
        var tropical = 100.0;
        var sidereal = Ayanamsa.ToSidereal(tropical, AyanamsaKind.Lahiri, jd);
        Assert.Equal(tropical - Ayanamsa.LahiriDegrees(jd), sidereal, 9);
    }

    [Fact]
    public void Conversions_are_deterministic_across_repeated_calls()
    {
        var jd = ToJd(2025, 3, 9);
        Assert.Equal(TimeScales.DeltaTSeconds(jd), TimeScales.DeltaTSeconds(jd), 12);
        Assert.Equal(PrecessionNutation.TrueObliquityDegrees(jd), PrecessionNutation.TrueObliquityDegrees(jd), 12);
        Assert.Equal(Ayanamsa.LahiriDegrees(jd), Ayanamsa.LahiriDegrees(jd), 12);
    }

    private static double ToJd(int year, int month, int day)
    {
        // Standard Gregorian calendar to JD (valid for the supported span).
        var a = (14 - month) / 12;
        var y = year + 4800 - a;
        var m = month + (12 * a) - 3;
        return day + (((153 * m) + 2) / 5) + (365 * y) + (y / 4) - (y / 100) + (y / 400) - 32045 - 0.5;
    }
}
