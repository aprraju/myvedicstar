using MyVedicStar.Astronomy.Core;
using NodaTime;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// AST-09 (issue #74) corpus structure: the matrix is complete, deterministic and independent of the implementation.
/// </summary>
/// <remarks>
/// <para><b>What this file does and does not prove.</b> It proves the corpus is the reviewed matrix - every category
/// meeting its declared count, totals at or above 10,000, enumeration reproducible, and no case dependent on the
/// code under test. It does <b>not</b> prove accuracy: that requires a Horizons reference row per case, and a case
/// without one is reported as unmeasured by <see cref="HorizonsCorpusMeasurementTests"/>. Keeping the two files apart
/// is deliberate, so a green structural suite can never be read as an accuracy pass.</para>
/// <para><b>Placement.</b> These live in the astronomy test project because the corpus enumerator and the parity
/// fixtures are both reachable from here; they assert on data and enumeration only, so they would pass unchanged
/// against a different provider.</para>
/// </remarks>
public sealed class HorizonsCorpusTests
{
    private static readonly Lazy<string> SolutionRoot = new(() => FindSolutionRoot());

    [Fact]
    public void The_corpus_enumerates_at_least_the_required_case_count()
    {
        var cases = Spec().Enumerate();

        // 10000 is the acceptance number; asserting the exact constant rather than "> 0" is what makes a truncated
        // corpus a failure instead of a smaller green run.
        Assert.True(cases.Count >= 10_000,
            $"AST-09 requires at least 10000 cases; the corpus enumerates {cases.Count}. " +
            $"Per category: {Format(cases)}.");
    }

    [Fact]
    public void Every_category_meets_its_declared_count()
    {
        var spec = Spec();
        var cases = spec.Enumerate();
        var actual = spec.CountByCategory(cases);
        var declared = spec.Document.CaseCounts;

        Assert.Equal(declared.PlanetaryPositions, actual[CorpusCategory.PlanetaryPositions]);
        Assert.Equal(declared.TithiNakshatraTransitions, actual[CorpusCategory.TithiNakshatraTransitions]);
        Assert.Equal(declared.SunriseSunset, actual[CorpusCategory.SunriseSunset]);
        Assert.Equal(declared.Lagna, actual[CorpusCategory.Lagna]);
        Assert.Equal(declared.Ayanamsa, actual[CorpusCategory.Ayanamsa]);
        Assert.Equal(declared.HistoricalDates, actual[CorpusCategory.HistoricalDates]);

        // A category at zero would satisfy the total while silently dropping the dimension AC-1 names explicitly.
        Assert.All(actual.Values, count => Assert.True(count > 0, $"A category enumerated no cases: {Format(cases)}."));
    }

    [Fact]
    public void Case_identifiers_are_unique()
    {
        var cases = Spec().Enumerate();

        // A duplicate id means two cases share a reference lookup, so one failure would be reported twice and the
        // other dimension would lose its case without any count changing.
        var duplicates = cases.GroupBy(caseItem => caseItem.Id).Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} x{group.Count()}").ToArray();
        Assert.True(duplicates.Length == 0, $"Duplicate case ids: {string.Join(", ", duplicates)}.");
    }

    [Fact]
    public void Enumeration_is_deterministic()
    {
        var first = Spec().Enumerate();
        var second = Spec().Enumerate();

        // The corpus is regenerated from a committed spec, so reruns must agree exactly. Randomness would make a
        // failure unreproducible and a reviewer unable to recompute a single case.
        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
            Assert.Equal(first[index], second[index]);
    }

    [Fact]
    public void The_span_covers_the_fixture_range_and_the_histories_document()
    {
        var spec = Spec();
        var cases = spec.Enumerate();
        var from = JulianDay.FromUtcMidnight(new LocalDate(spec.Document.DateSpanUtYears.From, 1, 1));
        var to = JulianDay.FromUtcMidnight(new LocalDate(spec.Document.DateSpanUtYears.To, 12, 31));

        // Span-generated cases sit inside the span exactly; declared edge cases get a one-day margin because a local
        // midnight in a zone east or west of Greenwich can land a fraction of a second outside a UT-midnight span.
        // That margin is stated here rather than widening the range for everything, which would hide a real spread bug
        // in the planetary or transition enumeration.
        var edgeIds = spec.EdgeCases.Select(edgeCase => $"historical-{edgeCase.Id}").ToHashSet();
        Assert.All(cases, caseItem => Assert.InRange(caseItem.JulianDayUt,
            edgeIds.Contains(caseItem.Id) ? from - 1.0 : from,
            edgeIds.Contains(caseItem.Id) ? to + 1.0 : to));

        // The declared span must contain the parity fixture span, or a body covered by parity has no accuracy case.
        var fixtureFrom = JulianDay.FromUtcMidnight(new LocalDate(ParityFixtures.From.Year, ParityFixtures.From.Month,
            ParityFixtures.From.Day));
        var fixtureTo = JulianDay.FromUtcMidnight(new LocalDate(ParityFixtures.To.Year, ParityFixtures.To.Month,
            ParityFixtures.To.Day));
        Assert.True(from <= fixtureFrom && to >= fixtureTo,
            $"Corpus span {spec.Document.DateSpanUtYears.From}-{spec.Document.DateSpanUtYears.To} does not contain " +
            $"the fixture span {ParityFixtures.From}-{ParityFixtures.To}.");
    }

    [Fact]
    public void Every_declared_body_is_carried_by_at_least_one_case()
    {
        var spec = Spec();
        var covered = spec.Enumerate().Where(caseItem => caseItem.Body is not null)
            .Select(caseItem => caseItem.Body!.Value).ToHashSet();

        var missing = spec.Bodies.Where(body => !covered.Contains(body)).ToArray();
        Assert.True(missing.Length == 0,
            $"Declared bodies with no case: {string.Join(", ", missing)}. A body in the matrix but not in the corpus " +
            "is an accuracy claim nobody measures.");
    }

    [Fact]
    public void Sites_span_the_tropics_mid_latitudes_and_polar_latitudes()
    {
        var sites = Spec().Sites;

        Assert.True(sites.Count >= 20, $"The matrix requires at least 20 sites; the spec declares {sites.Count}.");
        Assert.Contains(sites, site => Math.Abs(site.LatitudeDegrees) < 23.5);
        Assert.Contains(sites, site => site.LatitudeDegrees is > 40 and < 60);
        Assert.Contains(sites, site => Math.Abs(site.LatitudeDegrees) > 66.5);

        // Both hemispheres, because a sunrise solver that only ever sees positive latitudes can subtract instead of
        // add a declination and still look correct.
        Assert.Contains(sites, site => site.LatitudeDegrees > 0);
        Assert.Contains(sites, site => site.LatitudeDegrees < 0);
    }

    [Fact]
    public void Timezone_and_leap_second_edge_cases_are_present()
    {
        var edgeCases = Spec().EdgeCases;
        var ids = edgeCases.Select(edgeCase => edgeCase.Id).ToArray();

        Assert.Contains(ids, id => id.Contains("spring-forward"));
        Assert.Contains(ids, id => id.Contains("fall-back"));
        Assert.Contains(ids, id => id.Contains("leap-second"));
        Assert.Contains(ids, id => id.Contains("polar-night"));
        Assert.Contains(ids, id => id.Contains("polar-day"));
        Assert.Contains(ids, id => id.Contains("delta-t"));

        // Each zone must resolve in the tz database the engine uses, or the case would fall back to UTC and stop
        // being an edge case while still counting as one.
        foreach (var edgeCase in edgeCases)
            Assert.True(DateTimeZoneProviders.Tzdb.GetZoneOrNull(edgeCase.Timezone) is not null,
                $"Edge case '{edgeCase.Id}' names a zone absent from tzdb: {edgeCase.Timezone}.");
    }

    [Fact]
    public void No_declared_tolerance_exceeds_the_owner_approved_target()
    {
        var tolerances = Spec().Tolerances;

        // Architect section C.3: no widening after the fact. The approved numbers are literals here so a spec edit
        // that relaxes one is a failing test, not a quiet budget increase.
        Assert.Equal(1, tolerances.PlanetsArcseconds);
        Assert.Equal(3, tolerances.MoonArcseconds);
        Assert.Equal(5, tolerances.LagnaArcseconds);
        Assert.Equal(1, tolerances.AyanamsaArcseconds);
        Assert.Equal(2, tolerances.EclipseContactSeconds);
        Assert.Equal(30, tolerances.SunriseSunsetSeconds);

        Assert.Throws<InvalidOperationException>(() => tolerances.ArcsecondsFor((CelestialBody)(-1)));
    }

    [Fact]
    public void A_lowered_minimum_case_count_is_refused_by_the_loader()
    {
        // Falsifiability for the >=10000 gate: the loader must reject a spec that lowers the floor, so the constant
        // cannot be edited in JSON to make the suite pass with a smaller corpus.
        var specPath = Path.Combine(SolutionRoot.Value, "astronomy", HorizonsCorpusSpec.FileName);
        var original = File.ReadAllText(specPath);
        var lowered = original.Replace("\"minimumTotalCases\": 10000", "\"minimumTotalCases\": 100");

        // If the replacement did not apply, the assertion below would pass for the wrong reason.
        Assert.NotEqual(original, lowered);

        var temp = Path.Combine(Path.GetTempPath(), $"ast09-spec-{Guid.NewGuid():N}.json");
        File.WriteAllText(temp, lowered);
        try
        {
            // Load the edited file BY PATH. Passing its temp directory asked the loader for a file named
            // horizons-corpus-spec.json that was never there, so the assertion passed on FileNotFoundException-shaped
            // intent rather than on the refusal it names.
            Assert.Throws<InvalidOperationException>(() => HorizonsCorpusSpec.Load(temp));
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static HorizonsCorpusSpec Spec() => HorizonsCorpusSpec.Load(SolutionRoot.Value);

    private static string Format(IEnumerable<CorpusCase> cases) =>
        string.Join(", ", Enum.GetValues<CorpusCategory>()
            .Select(category => $"{category}={cases.Count(caseItem => caseItem.Category == category)}"));

    internal static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "astronomy", HorizonsCorpusSpec.FileName))
                || File.Exists(Path.Combine(directory.FullName, "astronomy", ParityFixtures.FileName)))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find '{HorizonsCorpusSpec.FileName}' in any parent of '{AppContext.BaseDirectory}'.");
    }
}
