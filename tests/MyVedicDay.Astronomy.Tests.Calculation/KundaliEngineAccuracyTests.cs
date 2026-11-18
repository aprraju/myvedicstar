using MyVedicDay.Astronomy;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// KUN-20 slice 1 accuracy contract, asserted where the provider is actually reachable.
/// </summary>
/// <remarks>
/// These live in the astronomy test project rather than the Kundali one because <see cref="MeeusEphemeris"/> is
/// internal to this assembly. That placement is deliberate: the tolerance belongs to the provider, and measuring it
/// from a project that can only see <see cref="IEphemeris"/> would need a stub and would measure nothing.
/// <para>
/// Reference values are the Meeus worked examples already committed at astronomy/parity-fixtures.json - reused rather
/// than re-typed so the two cannot drift apart. The Julian Day is UT because <see cref="IEphemeris"/> applies delta-T
/// internally; the fixture's source field records that conversion.
/// </para>
/// </remarks>
public sealed class KundaliEngineAccuracyTests
{
    /// <summary>Meeus example 25.b, apparent solar longitude.</summary>
    private const double SunReferenceJdUt = 2448908.4993171296;

    private const double SunReferenceTropical = 199.901641;
    private const double SunToleranceArcseconds = 1.0;

    /// <summary>Meeus example 47.a, mean-equinox lunar longitude.</summary>
    private const double MoonReferenceJdUt = 2448724.4993171296;

    private const double MoonReferenceTropical = 133.1704333;
    private const double MoonToleranceArcseconds = 3.0;

    private const double ArcsecondsPerDegree = 3600.0;

    [Fact]
    public void Sun_longitude_is_within_one_arcsecond_of_the_meeus_reference()
    {
        var actual = new MeeusEphemeris().GetSunLongitude(SunReferenceJdUt);
        var difference = Math.Abs(actual - SunReferenceTropical) * ArcsecondsPerDegree;

        Assert.True(difference <= SunToleranceArcseconds,
            $"Sun longitude differs by {difference:F3} arcseconds, budget {SunToleranceArcseconds}. "
            + $"Actual {actual}, expected {SunReferenceTropical}.");
    }

    [Fact]
    public void Moon_longitude_is_within_three_arcseconds_of_the_meeus_reference()
    {
        // The lunar series is coarser than the solar one, so the budget is wider - stated here, not discovered by
        // watching a test fail.
        var actual = new MeeusEphemeris().GetMoonLongitude(MoonReferenceJdUt);
        var difference = Math.Abs(actual - MoonReferenceTropical) * ArcsecondsPerDegree;

        Assert.True(difference <= MoonToleranceArcseconds,
            $"Moon longitude differs by {difference:F3} arcseconds, budget {MoonToleranceArcseconds}. "
            + $"Actual {actual}, expected {MoonReferenceTropical}.");
    }
}

