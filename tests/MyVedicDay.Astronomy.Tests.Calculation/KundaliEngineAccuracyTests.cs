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
    public void Moon_longitude_is_within_the_engine_reference_tolerance()
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

    [Fact]
    public void The_drik_offset_is_the_only_difference_between_the_series_and_the_engine_reference()
    {
        // Auditable only if nothing else separates the two values. The expected offset is written as a LITERAL, not
        // read from MeeusEphemeris.DrikPanchangLunarOffsetDegrees: comparing the constant against itself would be
        // tautological and would pass for any value. A literal makes the offset's size a claim this test can fail on.
        const double PublishedDrikOffsetArcseconds = 28.0;

        var series = LunarPosition.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(MoonReferenceJdUt));
        var engine = new MeeusEphemeris().GetMoonLongitude(MoonReferenceJdUt);

        var difference = AngleMath.NormalizeDegrees(engine - series) * ArcsecondsPerDegree;

        Assert.Equal(PublishedDrikOffsetArcseconds, difference, 6);

        // The series alone is the Meeus 47.a value, so the engine equals Meeus plus exactly the Drik offset.
        Assert.Equal(133.162655, series, 0.0005);

        // And the composed reference still holds: engine == Meeus + 28 arcsec, to the fixture's published budget.
        // Read from the EngineReference row so this leg tracks the same data the parity gate uses.
        var composedBudgetArcseconds = ParityFixtures.ToleranceArcseconds(FindSolutionRoot(), CelestialBody.Moon,
            FixtureClaim.EngineReference);
        var composedDifferenceArcseconds =
            Math.Abs(engine - (133.162655 + PublishedDrikOffsetArcseconds / ArcsecondsPerDegree)) * ArcsecondsPerDegree;

        Assert.True(composedDifferenceArcseconds <= composedBudgetArcseconds,
            $"Composed engine value differs from Meeus 47.a plus the Drik offset by {composedDifferenceArcseconds:F3} "
            + $"arcseconds, budget {composedBudgetArcseconds}. Actual {engine}.");
    }

    [Fact]
    public void The_unoffset_lunar_series_matches_meeus_47a_to_the_published_precision()
    {
        // The unoffset series asserted directly, rather than inferred by subtracting the offset in another test. This
        // is the claim that makes the 28 arcsec auditable: the series alone lands on Meeus, so the offset is a
        // declared correction ON TOP of an accurate series and not a fudge covering a series that misses.
        //
        // 133.162655 is Meeus example 47.a's stated mean-equinox longitude, and the band is that example's published
        // precision (about 1.8 arcsec) - tighter than the composed 3 arcsec budget because nothing but the series
        // contributes here.
        //
        // api#130: the band is now READ FROM THE FIXTURE, keyed on (Moon, UnoffsetSeries), rather than a literal here.
        // It is a different claim from the Moon/EngineReference row, so it is a different row - not the same 3 arcsec
        // budget reused, which would have widened this assertion by about 67% and quietly weakened it. Editing the
        // series tolerance in the fixture now changes exactly this assertion (and the offset test's composed leg),
        // which is what "owned by data" means.
        var series = LunarPosition.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(MoonReferenceJdUt));
        var budgetArcseconds = ParityFixtures.ToleranceArcseconds(FindSolutionRoot(), CelestialBody.Moon,
            FixtureClaim.UnoffsetSeries);
        var differenceArcseconds = Math.Abs(series - 133.162655) * ArcsecondsPerDegree;

        Assert.True(differenceArcseconds <= budgetArcseconds,
            $"Unoffset lunar series differs from Meeus 47.a by {differenceArcseconds:F3} arcseconds, budget "
            + $"{budgetArcseconds}. Actual {series}, expected 133.162655.");
    }

    /// <summary>
    /// The residual between the unoffset mean-equinox lunar series and the reference the Drik offset was measured
    /// against, asserted at a named instant and bounded rather than pinned (api#148).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This claim previously existed only as arithmetic in a pull-request conversation: three people agreed the
    /// residual was 0.271 arcsec and nothing in the code could notice if it moved. An inference that lives in a thread
    /// is not evidence, and a later auditor of the drik offset had to re-derive it from prose.
    /// </para>
    /// <para>
    /// <b>Bounded, never exact.</b> 0.271 is tighter than this series guarantees; pinning it would make any legitimate
    /// refinement to the series fail, and a test that fails when the engine improves is one nobody removes, because
    /// failing-on-improvement looks like rigour. The reported residual is in the failure message so a real move is
    /// visible without asserting its current value.
    /// </para>
    /// <para>
    /// <b>The budget is the fixture's, not a fresh constant.</b> It is read from the (Moon, MeanEquinoxResidual) row so
    /// editing a budget is a diff in data; a literal 1.8 here would be the two-sources defect api#106/129 removed,
    /// rebuilt one commit later. That row currently borrows the Meeus 47.a precision because the owner has not set a
    /// budget for this claim (#144) - so this asserts the residual holds, and says so, rather than claiming a
    /// published tolerance exists for it. The 28 arcsec offset was MEASURED too; neither number is published.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_unoffset_lunar_series_stays_within_a_stated_band_of_the_residual_reference()
    {
        var fixtures = ParityFixtures.Load(FindSolutionRoot());
        var row = Assert.Single(fixtures,
            f => f.Body == CelestialBody.Moon && f.Claim == FixtureClaim.MeanEquinoxResidual);

        // The instant is the row's, so the assertion and the budget can never drift onto different instants.
        var series = LunarPosition.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(row.JulianDayUt));
        // Plain subtraction, deliberately NOT AngleMath.NormalizeDegrees: this compares two values near 133 degrees, and
        // the modular helper maps a difference of a few hundredths of an arcsecond to nearly a full revolution. The
        // wrap belongs where a longitude is produced, not where two nearby longitudes are subtracted.
        var differenceArcseconds =
            Math.Abs(series - row.TropicalLongitudeDegrees) * ArcsecondsPerDegree;

        Assert.True(differenceArcseconds <= row.ToleranceArcseconds,
            $"Unoffset lunar series is {differenceArcseconds:F3} arcseconds from the residual reference at "
            + $"JD {row.JulianDayUt}, budget {row.ToleranceArcseconds}. Actual {series}, "
            + $"reference {row.TropicalLongitudeDegrees}.");

        // Falsifiability of the lookup, not just of the value (api#148). The budget above resolves only because this
        // claim has exactly one row. A claim covering two instants must refuse an instant-less lookup instead of
        // resolving by ordering - asserted through the real lookup against a temporary fixture, so the guard is proved
        // on the production path rather than restated in the test.
        var directory = Directory.CreateTempSubdirectory("api148-ambiguous-");
        try
        {
            ParityFixtures.Save(directory.FullName,
            [
                new ParityFixture(CelestialBody.Moon, MoonReferenceJdUt, 133.162655, "test", 1.8,
                    FixtureClaim.MeanEquinoxResidual),
                new ParityFixture(CelestialBody.Moon, MoonReferenceJdUt + 1d, 133.162655, "test", 1.8,
                    FixtureClaim.MeanEquinoxResidual)
            ]);

            var ambiguous = Assert.Throws<InvalidOperationException>(() =>
                ParityFixtures.ToleranceArcseconds(directory.FullName, CelestialBody.Moon,
                    FixtureClaim.MeanEquinoxResidual));
            Assert.Contains("ambiguous", ambiguous.Message, StringComparison.OrdinalIgnoreCase);

            // Naming the instant still resolves, so the guard refuses ambiguity and not the claim itself.
            Assert.Equal(1.8, ParityFixtures.ToleranceArcseconds(directory.FullName, CelestialBody.Moon,
                FixtureClaim.MeanEquinoxResidual, MoonReferenceJdUt));
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        // And the row must stay out of the parity comparison set: the engine never returns the unoffset series.
        Assert.DoesNotContain(ParityFixtures.Comparisons(fixtures),
            f => f.Claim == FixtureClaim.MeanEquinoxResidual);
    }

    /// <summary>The budget for a body's composed engine value, read from the fixture so it is the only source.</summary>
    /// <remarks>
    /// Resolves the <see cref="FixtureClaim.EngineReference"/> row specifically (api#130). A body-only lookup would pick
    /// whichever row came first once a body carries two claims, which is the ambiguity the Claim field exists to remove.
    /// </remarks>
    private static double ToleranceArcseconds(CelestialBody body) =>
        ParityFixtures.ToleranceArcseconds(FindSolutionRoot(), body, FixtureClaim.EngineReference);

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
