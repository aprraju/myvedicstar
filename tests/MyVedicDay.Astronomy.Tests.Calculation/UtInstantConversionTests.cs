using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using Xunit;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// api#140. <c>2451545.0</c> is J2000.0 in TT and also what a fixture author types. <see cref="UtInstant"/> and
/// <see cref="TtInstant"/> exist so that passing one where the other is wanted does not compile, rather than producing
/// a 31.99 arcsecond discrepancy that looks like an accuracy problem.
/// </summary>
public sealed class UtInstantConversionTests
{
    /// <summary>The measured delta-T at J2000, in days, from the provider's own implementation.</summary>
    private const double J2000DeltaTDays = 0.0007391204;

    public UtInstantConversionTests()
    {
        // Deliberately NOT `??=`. That operator means "if nothing is installed, install one and carry on", which is the
        // silent-fallback pattern this type pair exists to remove - reintroduced in the fixture. The test either
        // installs the real conversion on purpose, or asserts that composition already did.
        TtOffsets.Install(DeltaT.ToJulianEphemerisDay);
    }

    [Fact]
    public void Converting_J2000_from_UT_to_TT_lands_on_the_measured_ephemeris_day()
    {
        var tt = new UtInstant(2451545.0).ToTerrestrialTime();

        Assert.Equal(2451545.0 + J2000DeltaTDays, tt.JulianDay, 7);
    }

    [Fact]
    public void The_two_instants_are_63_86_seconds_apart_and_the_type_pair_is_why_that_is_not_invisible()
    {
        // The whole finding: same literal, two instants. 63.86 s is ~35 arcsec of lunar motion and ~2.5 arcsec of solar
        // motion, so the mistake is invisible on the Sun and fatal on the Moon.
        var tt = new UtInstant(2451545.0).ToTerrestrialTime();
        var seconds = (tt.JulianDay - 2451545.0) * 86400.0;

        Assert.InRange(seconds, 63.8, 63.9);
    }

    [Fact]
    public void The_series_at_each_argument_reproduces_the_pinned_values()
    {
        // SERIES pins from two independent derivations (engine execution and an independent 60-term transcription of
        // the repo's own table). If the series moves while the conversion stays correct, this fails - which the
        // conversion pin alone could not detect.
        var atUt = LunarPosition.MeanEquinoxLongitude(2451545.0);
        var atTt = LunarPosition.MeanEquinoxLongitude(new UtInstant(2451545.0).ToTerrestrialTime().JulianDay);

        Assert.Equal(223.3187110273, atUt, 9);
        Assert.Equal(223.3275960948, atTt, 9);

        // And the gap between them is the conversion, measured, not inferred.
        Assert.Equal(31.986, (atTt - atUt) * 3600.0, 2);
    }

    [Fact]
    public void The_engine_takes_UT_and_a_bare_TT_literal_would_be_the_wrong_instant()
    {
        // GetMoonLongitude takes UT. Feeding the TT literal is a 31.99 arcsec error, and the type pair is what stops it
        // being written: this test documents the magnitude, it cannot prevent the mistake.
        var viaUt = new MeeusEphemeris().GetMoonLongitude(2451545.0);
        var deltaT = (new UtInstant(2451545.0).ToTerrestrialTime().JulianDay - 2451545.0);

        Assert.Equal(223.3353738726, viaUt, 9);
        Assert.True(deltaT > 0, "delta-T must be positive at J2000; a sign error here would invert the whole finding.");
    }

    [Fact]
    public void A_missing_conversion_throws_rather_than_defaulting()
    {
        // A silent fallback would reintroduce the invisible difference the type pair exists to remove.
        var installed = TtOffsets.Conversion;
        try
        {
            TtOffsets.Conversion = null;
            Assert.Throws<InvalidOperationException>(() => new UtInstant(2451545.0).ToTerrestrialTime());
        }
        finally
        {
            // Restored through the same install path the provider uses, so this test cannot leave a null behind.
            TtOffsets.Install(installed!);
        }
    }
}
