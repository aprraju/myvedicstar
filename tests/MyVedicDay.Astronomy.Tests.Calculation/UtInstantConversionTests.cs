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
    /// <summary>The measured delta-T at J2000, in days.</summary>
    /// <remarks>
    /// <b>This literal is deliberately NOT derived from <see cref="DeltaT"/>.</b> Deriving it would call the same function
    /// the conversion calls and compare the provider to itself - the api#131 tautology: a test that cannot disagree with
    /// the thing it is testing. The value is written out so that a change to delta-T shows up here as a failure.
    ///
    /// Do not "fix" this into <c>DeltaT.ToJulianEphemerisDay(2451545.0)</c> in a DRY pass. The duplication looks like a
    /// wart and IS the guard.
    ///
    /// This is a separate fact from the series pins below: those are protected by taking <c>atTt</c> through the
    /// conversion rather than a hard-coded ephemeris day. One sentence covering both would hide one of them.
    /// </remarks>
    private const double J2000DeltaTDays = 0.0007391204;

    /// <summary>
    /// api#159: the real conversion, held by <b>this test instance</b>. Nothing is installed process-wide and nothing is
    /// reset, so no test can change what another test sees and an unlucky order cannot leave the process uninstalled.
    /// </summary>
    private static readonly TtConversionSeam Real =
        TtConversionSeam.Installed(DeltaT.ToJulianEphemerisDay, DeltaT.ToJulianDayUt);

    [Fact]
    public void Converting_J2000_from_UT_to_TT_lands_on_the_measured_ephemeris_day()
    {
        var tt = new UtInstant(2451545.0).ToTerrestrialTime(Real);

        Assert.Equal(2451545.0 + J2000DeltaTDays, tt.JulianDay, 7);
    }

    [Fact]
    public void The_two_instants_are_63_86_seconds_apart_and_the_type_pair_is_why_that_is_not_invisible()
    {
        // The whole finding: same literal, two instants. 63.86 s is ~35 arcsec of lunar motion and ~2.5 arcsec of solar
        // motion, so the mistake is invisible on the Sun and fatal on the Moon.
        var tt = new UtInstant(2451545.0).ToTerrestrialTime(Real);
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
        var atTt = LunarPosition.MeanEquinoxLongitude(new UtInstant(2451545.0).ToTerrestrialTime(Real).JulianDay);

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
        var deltaT = (new UtInstant(2451545.0).ToTerrestrialTime(Real).JulianDay - 2451545.0);

        Assert.Equal(223.3353738726, viaUt, 9);
        Assert.True(deltaT > 0, "delta-T must be positive at J2000; a sign error here would invert the whole finding.");
    }

    [Fact]
    public void A_missing_conversion_throws_rather_than_defaulting()
    {
        // A silent fallback would reintroduce the invisible difference the type pair exists to remove. api#159: the
        // missing state is CONSTRUCTED here, not produced by a reset that the previous test had to give back.
        var absent = TtConversionSeam.Uninstalled();

        Assert.Throws<InvalidOperationException>(() => { _ = new UtInstant(2451545.0).ToTerrestrialTime(absent); });
    }

    [Fact]
    public void The_installed_conversion_survives_a_neighbour_observing_absence()
    {
        // The latch's real hazard: a test that looked at the uninstalled branch left every LATER test without a
        // conversion. Here the neighbouring seam is constructed, used and disposed, and this one is untouched.
        var absent = TtConversionSeam.Uninstalled();
        Assert.False(absent.HasConversion);
        absent.Dispose();

        Assert.Equal(2451545.0 + J2000DeltaTDays, new UtInstant(2451545.0).ToTerrestrialTime(Real).JulianDay, 7);
    }

    [Fact]
    public void The_providers_composition_is_what_populates_the_default_seam()
    {
        // api#159. This test project never composes a host, so nothing may have touched the default on its behalf - the
        // latch's worst state ("no conversion, and later tests cannot get one back") is now just `HasConversion == false`,
        // which is observable rather than inferred from an ordering accident.
        Assert.False(TtConversionSeam.Default.HasConversion,
            "A test project that never composed a host must not find a conversion installed by another test.");

        // And the provider really does install one: composition is the only thing that changes this.
        TtConversionSeam.InstallDefault(DeltaT.ToJulianEphemerisDay, DeltaT.ToJulianDayUt);
        try
        {
            Assert.True(TtConversionSeam.Default.HasConversion);
            Assert.Equal(2451545.0 + J2000DeltaTDays,
                new UtInstant(2451545.0).ToTerrestrialTime(TtConversionSeam.Default).JulianDay, 7);
        }
        finally
        {
            // Default is process-wide by design (the provider sets it once at composition); put the process back the
            // way this project found it so this test cannot decide what a LATER test sees - the api#159 hazard itself.
            TtConversionSeam.ResetDefaultForTests();
        }
    }
}
