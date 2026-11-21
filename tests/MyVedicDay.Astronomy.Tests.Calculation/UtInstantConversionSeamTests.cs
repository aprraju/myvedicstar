using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using Xunit;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// api#159. The UT-&gt;TT conversion is a process-wide latch (<see cref="TtOffsets.Install"/> is install-once), so the
/// two worlds a test needs to distinguish - "a conversion is loaded" and "no conversion is loaded" - are decided by
/// whichever test ran first, and an unlucky order leaves every later test running with <b>no</b> conversion.
///
/// <para>
/// The seam must make <b>absence</b> assertable without a production-callable reset. These tests are written against
/// that seam; on the latched design they fail for the stated reason: a loaded conversion is not asserted to throw and a
/// missing one is not asserted to throw, because the state cannot be chosen per instance at all.
/// </para>
/// </summary>
public sealed class UtInstantConversionSeamTests
{
    /// <summary>An uninstalled seam has no conversion, and asking it for one throws.</summary>
    [Fact]
    public void A_seam_constructed_without_a_conversion_throws_rather_than_defaulting()
    {
        var seam = TtConversionSeam.Uninstalled();

        Assert.False(seam.HasConversion);

        // Both directions must refuse: a silent fallback is the invisible delta-T error api#140 removed.
        Assert.Throws<InvalidOperationException>(() => { _ = seam.ToTerrestrialTime(new UtInstant(2451545.0)); });
        Assert.Throws<InvalidOperationException>(() => { _ = seam.ToUniversalTime(new TtInstant(2451545.0)); });
    }

    /// <summary>A seam constructed with a conversion converts, and round-trips through its own inverse.</summary>
    [Fact]
    public void A_seam_constructed_with_a_conversion_converts_and_never_consults_global_state()
    {
        // Two DIFFERENT conversions, both live at once. On the latch the second install is refused, so one of these
        // two assertions cannot hold - which is the defect: whichever ran last wins.
        var offsetDays = 1.0 / 8.0;
        var forwards = TtConversionSeam.Installed(ut => ut + offsetDays, tt => tt - offsetDays);
        var doubled = TtConversionSeam.Installed(ut => ut + (2.0 * offsetDays), tt => tt - (2.0 * offsetDays));

        Assert.True(forwards.HasConversion);
        Assert.True(doubled.HasConversion);

        Assert.Equal(2451545.0 + offsetDays, forwards.ToTerrestrialTime(new UtInstant(2451545.0)).JulianDay, 12);
        Assert.Equal(2451545.0 + (2.0 * offsetDays), doubled.ToTerrestrialTime(new UtInstant(2451545.0)).JulianDay, 12);

        // Round trip through a single seam is exact: the inverse is paired with the forward at construction.
        Assert.Equal(2451545.0, forwards.ToUniversalTime(forwards.ToTerrestrialTime(new UtInstant(2451545.0))).JulianDay, 12);
    }

    /// <summary>Disposing the seam restores whatever was there before, so tests cannot leak state into each other.</summary>
    [Fact]
    public void Disposing_a_seam_restores_the_previous_conversion_and_leaves_absence_observable()
    {
        var offsetDays = 1.0 / 8.0;
        var outer = TtConversionSeam.Installed(ut => ut + offsetDays, tt => tt - offsetDays);

        using (var inner = TtConversionSeam.Installed(ut => ut + (2.0 * offsetDays), tt => tt - (2.0 * offsetDays)))
        {
            // inner is the live conversion here - installed(D) matches `outer`, so a stale read would pass.
            Assert.Equal(2451545.0 + (2.0 * offsetDays), inner.ToTerrestrialTime(new UtInstant(2451545.0)).JulianDay, 12);
        }

        // Outer is whole again after the inner scope closes - the latch had no way to express this.
        Assert.Equal(2451545.0 + offsetDays, outer.ToTerrestrialTime(new UtInstant(2451545.0)).JulianDay, 12);

        // outer still HOLDS its conversion: disposing restores the process default, it does not empty the instance.
        // Conflating the two was the latch's mistake - a reset that was both "the state" and "how you inspect it".
        Assert.True(outer.HasConversion);
        Assert.Equal(2451545.0 + offsetDays, outer.ToTerrestrialTime(new UtInstant(2451545.0)).JulianDay, 12);

        // Absence, separately, is a seam that was constructed without one and stays without one.
        outer.Dispose();
        var absent = TtConversionSeam.Uninstalled();
        Assert.False(absent.HasConversion);
        Assert.Throws<InvalidOperationException>(() => { _ = absent.ToTerrestrialTime(new UtInstant(2451545.0)); });
    }

    /// <summary>
    /// The production defect, reproduced without a production-visible reset: the provider installs its conversion,
    /// and a test that then holds an uninstalled seam still sees absence.
    /// </summary>
    [Fact]
    public void The_providers_install_does_not_decide_what_an_uninstalled_seam_sees()
    {
        TtConversionSeam.InstallDefault(DeltaT.ToJulianEphemerisDay, DeltaT.ToJulianDayUt);

        var withoutConversion = TtConversionSeam.Uninstalled();

        Assert.False(withoutConversion.HasConversion);
        Assert.Throws<InvalidOperationException>(() => { _ = withoutConversion.ToTerrestrialTime(new UtInstant(2451545.0)); });

        // The default install is untouched by that observation - an observation is not a switch.
        Assert.True(TtConversionSeam.Default.HasConversion);
    }
}
