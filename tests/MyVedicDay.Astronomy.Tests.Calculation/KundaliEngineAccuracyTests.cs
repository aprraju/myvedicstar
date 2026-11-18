using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// KUN-20 slice 1 accuracy contract, asserted where the provider is actually reachable.
/// </summary>
/// <remarks>
/// These live in the astronomy test project rather than the Kundali one because <see cref="MeeusEphemeris"/> is
/// internal to this assembly. That placement is deliberate: the tolerance belongs to the provider, and measuring it
/// from a project that can only see <see cref="IEphemeris"/> would need a stub and would measure nothing.
/// <para>
/// api#106: the tolerances used to be constants in this file while the fixture carried its own
/// <c>toleranceArcseconds</c> field. Two sources that happen to agree are not a derivation - nothing kept them equal,
/// and an edit to either would have gone unnoticed. The fixture is now the only origin, so widening a budget is a
/// visible diff in data rather than a quiet edit to an assertion.
/// </para>
/// <para>
/// The Julian Day is UT because <see cref="IEphemeris"/> applies delta-T internally; the fixture's source field
/// records that conversion.
/// </para>
/// </remarks>
public sealed class KundaliEngineAccuracyTests
{
    private const double ArcsecondsPerDegree = 3600.0;

    /// <summary>Meeus example 25.b, apparent solar longitude.</summary>
    private const double SunReferenceJdUt = 2448908.4993171296;

    private const double SunReferenceTropical = 199.901641;

    /// <summary>Meeus example 47.a, in the UT argument <see cref="IEphemeris"/> takes.</summary>
    private const double MoonReferenceJdUt = 2448724.4993171296;

    /// <summary>
    /// The longitude the provider is expected to reproduce: Meeus 47.a's 133.162655 shifted by the provider's
    /// documented Drik Panchang lunar offset, because <see cref="IEphemeris.GetMoonLongitude"/> includes that
    /// correction by design. The offset is justified and measured through Panchang element times, which have their own
    /// published-time assertions; here it is accounted for rather than re-argued.
    /// </summary>
    private const double MoonReferenceTropical = 133.1704333;

    [Fact]
    public void Sun_longitude_is_within_the_published_reference_tolerance()
    {
        var actual = new MeeusEphemeris().GetSunLongitude(SunReferenceJdUt);
        var budget = ToleranceArcseconds(CelestialBody.Sun);
        var difference = Math.Abs(actual - SunReferenceTropical) * ArcsecondsPerDegree;

        Assert.True(difference <= budget,
            $"Sun longitude differs by {difference:F3} arcseconds, budget {budget}. "
            + $"Actual {actual}, expected {SunReferenceTropical}.");
    }

    [Fact]
    public void Moon_longitude_is_within_the_published_reference_tolerance()
    {
        // The lunar series is coarser than the solar one, so its published budget is wider - stated in the fixture
        // data, not discovered by watching a test fail.
        var actual = new MeeusEphemeris().GetMoonLongitude(MoonReferenceJdUt);
        var budget = ToleranceArcseconds(CelestialBody.Moon);
        var difference = Math.Abs(actual - MoonReferenceTropical) * ArcsecondsPerDegree;

        Assert.True(difference <= budget,
            $"Moon longitude differs by {difference:F3} arcseconds, budget {budget}. "
            + $"Actual {actual}, expected {MoonReferenceTropical}.");
    }

    [Fact]
    public void A_body_with_no_reference_fails_rather_than_defaulting_to_a_permissive_budget()
    {
        // Falsifiability: the tolerance path must refuse an absent budget instead of falling back to a default.
        // Without this, deleting a tolerance from the fixture would silently weaken the assertions above rather than
        // failing, which is the failure mode api#106 exists to remove.
        Assert.Throws<InvalidOperationException>(() => ToleranceArcseconds((CelestialBody)(-1)));
    }

    /// <summary>The budget for a body, read from the fixture so the published precision is the only source.</summary>
    private static double ToleranceArcseconds(CelestialBody body) =>
        ParityFixtures.Load(FindSolutionRoot())
            .FirstOrDefault(fixture => fixture.Body == body)?.ToleranceArcseconds
        ?? throw new InvalidOperationException(
            $"No accuracy reference for {body} in {ParityFixtures.FileName}, so no tolerance can be defended.");

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "astronomy", ParityFixtures.FileName))
                || File.Exists(Path.Combine(directory.FullName, ParityFixtures.FileName)))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            $"Parity fixture file '{ParityFixtures.FileName}' not found in any ancestor of {AppContext.BaseDirectory}.");
    }
}
