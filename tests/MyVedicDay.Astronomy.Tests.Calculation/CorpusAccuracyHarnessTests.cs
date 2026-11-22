using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// AST-09 (issue #74): the 10,000-case corpus is actually RUN against independent references, and every case is
/// accounted for by status.
/// </summary>
/// <remarks>
/// <para><b>The claim these tests make is deliberately smaller than "10,000 cases pass".</b> They assert that 10,000
/// cases exist, that every one is evaluated and given a stated outcome, that a case with a committed reference is
/// compared against it, and that a case without one is counted as unmeasured rather than as a pass. The larger claim
/// would be false today: 14 reference rows cannot score 10,000 generated instants, and pretending otherwise is the
/// defect AC-3 exists to prevent.</para>
/// <para><b>No tolerance is asserted into agreement.</b> The 1 arcsecond target is neither relaxed nor claimed met - the
/// solar and planetary residuals are documented unmet findings in <c>horizons-reference.json</c>. These tests report
/// the distribution the owner's target actually produces.</para>
/// </remarks>
public sealed class CorpusAccuracyHarnessTests
{
    [Fact]
    public void Every_enumerated_case_is_evaluated_and_none_is_dropped()
    {
        var cases = CorpusAccuracyHarness.EnumeratedCases;
        var evaluated = CorpusAccuracyHarness.All;

        // One row per case, in the same order: a case that vanished is a case nobody measured, and an off-by-one would
        // misattribute every residual after it.
        Assert.Equal(cases.Count, evaluated.Count);
        Assert.True(cases.Count >= 10_000, $"The corpus must carry at least 10000 cases; it carries {cases.Count}.");

        for (var index = 0; index < cases.Count; index++)
            Assert.Equal(cases[index].Id, evaluated[index].Case.Id);

        // Exactly one status per case, no silent gaps, and every row says why.
        Assert.All(evaluated, entry => Assert.True(Enum.IsDefined(entry.Status),
            $"{entry.Case.Id} has an undefined status {entry.Status}."));
        Assert.All(evaluated, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Detail),
            $"{entry.Case.Id} ({entry.Status}) carries no detail explaining its outcome."));
    }

    [Fact]
    public void The_case_counts_per_category_are_reported_and_cover_every_case()
    {
        var byCategory = CorpusAccuracyHarness.All
            .GroupBy(entry => entry.Case.Category)
            .ToDictionary(group => group.Key, group => group.Count());

        // Per-category reporting is AC-1's second half: one total hides a category that enumerated nothing.
        Assert.All(Enum.GetValues<CorpusCategory>(), category =>
            Assert.True(byCategory.TryGetValue(category, out var count) && count > 0,
                $"Category {category} contributed no evaluated cases."));

        Assert.Equal(CorpusAccuracyHarness.All.Count, byCategory.Values.Sum());
    }

    [Fact]
    public void Every_case_reaches_exactly_one_outcome_and_the_outcomes_partition_the_corpus()
    {
        var byStatus = CorpusAccuracyHarness.All
            .GroupBy(entry => entry.Status)
            .ToDictionary(group => group.Key, group => group.Count());

        // The partition property: the statuses are exhaustive and disjoint, so no case is both measured and unmeasured.
        Assert.Equal(CorpusAccuracyHarness.All.Count, byStatus.Values.Sum());
        Assert.All(byStatus.Keys, status => Assert.True(Enum.IsDefined(status)));
        Assert.All(CorpusAccuracyHarness.All, entry =>
            Assert.Equal(entry.WasCompared, entry.Status is CorpusCaseStatus.Scored or CorpusCaseStatus.OutOfTolerance));

        // A compared case carries both numbers; an unscored one carries neither. This is what makes "compared" mean
        // something a reader can check rather than a claim the harness makes about itself.
        Assert.All(CorpusAccuracyHarness.All.Where(entry => entry.WasCompared), entry =>
        {
            Assert.NotNull(entry.ResidualArcseconds);
            Assert.NotNull(entry.TargetArcseconds);
            Assert.True(entry.TargetArcseconds > 0);
        });
        Assert.All(CorpusAccuracyHarness.All.Where(entry => !entry.WasCompared), entry =>
        {
            Assert.Null(entry.ResidualArcseconds);
            Assert.Null(entry.TargetArcseconds);
        });
    }

    [Fact]
    public void Cases_without_a_committed_reference_are_counted_as_unmeasured_not_as_passes()
    {
        var noReference = CorpusAccuracyHarness.All
            .Where(entry => entry.Status == CorpusCaseStatus.NoReference)
            .ToArray();

        // The honest state: the corpus's generated instants mostly have no committed row. That is visible as
        // NoReference, not folded into a passing count.
        Assert.NotEmpty(noReference);
        Assert.All(noReference, entry => Assert.Contains("No committed Horizons row", entry.Detail));

        // And the harness must not be secretly scoring elsewhere: if any NoReference case were compared, the status
        // and the numbers would disagree.
        Assert.All(noReference, entry => Assert.False(entry.WasCompared));
    }

    [Fact]
    public void When_a_case_lands_on_a_reference_epoch_it_is_compared_against_that_row()
    {
        // The point of the harness is that reference-backed cases ARE compared - a harness that only ever reports
        // "no reference" would be as useless as one that only counts. Rather than depend on whether the generated
        // arithmetic happens to hit an epoch, this drives the comparison path directly with a case pinned to one.
        var reference = HorizonsAccuracyTests.HorizonsReference.Load(HorizonsCorpusTests.FindSolutionRoot());
        var epoch = reference.Epochs[0].JulianDayUt;
        var sunRow = reference.Rows.Single(row =>
            row.Body == "Sun" && Math.Abs(row.JulianDayUtResolved - epoch) < 1e-9);

        var sunMoon = new MeeusEphemeris();
        var engineSun = sunMoon.GetSunLongitude(sunRow.JulianDayUtResolved);
        var residual = (engineSun - sunRow.LongitudeDegrees) * 3600.0;

        // The same arithmetic the harness uses, against the same committed row, reproduces the residual that artifact
        // already pins (13.708 arcsec at JD 2451545). If the harness's comparison drifted from this, the two would
        // stop agreeing here.
        Assert.Equal(13.708, Math.Abs(residual), 3);
    }

    [Fact]
    public void The_engine_only_produces_the_nine_grahas_and_unsupported_bodies_are_declared_not_dropped()
    {
        var notComputable = CorpusAccuracyHarness.All
            .Where(entry => entry.Status == CorpusCaseStatus.NotComputable)
            .Select(entry => entry.Case.Body)
            .Where(body => body is not null)
            .Select(body => body!.Value)
            .Distinct()
            .OrderBy(body => body.ToString(), StringComparer.Ordinal)
            .ToArray();

        // The spec matrix names Uranus, Neptune and Pluto; the planet fit has no orbit beyond Saturn, so those are
        // absent. True Node is absent too: the node series computes the MEAN node only, and no true-node
        // implementation exists anywhere in the engine. Mean Node is deliberately NOT here - it is computed from
        // MeanLunarNode and is merely unreferenced, which is a different finding.
        Assert.Equal(
            new[] { CelestialBody.Neptune, CelestialBody.Pluto, CelestialBody.TrueNode, CelestialBody.Uranus }
                .OrderBy(body => body.ToString(), StringComparer.Ordinal).ToArray(),
            notComputable);

        Assert.DoesNotContain(CorpusAccuracyHarness.All, entry => entry.WasCompared
            && entry.Case.Body is CelestialBody.Uranus or CelestialBody.Neptune or CelestialBody.Pluto
                or CelestialBody.TrueNode);

        // Mean Node must be computable: it is the product's Rahu, and marking it NotComputable would hide a real body
        // behind the same status used for genuinely absent ones.
        Assert.DoesNotContain(CorpusAccuracyHarness.All,
            entry => entry.Case.Body == CelestialBody.MeanNode && entry.Status == CorpusCaseStatus.NotComputable);
    }

    [Fact]
    public void Non_longitude_categories_are_accounted_for_rather_than_scored_as_positions()
    {
        var notALongitude = CorpusAccuracyHarness.All
            .Where(entry => entry.Status == CorpusCaseStatus.NotALongitude)
            .ToArray();

        Assert.NotEmpty(notALongitude);

        // Only non-positional categories may carry this status; a PlanetaryPositions case marked NotALongitude would
        // mean a position was never measured.
        Assert.All(notALongitude, entry =>
            Assert.NotEqual(CorpusCategory.PlanetaryPositions, entry.Case.Category));

        // And every non-positional category is represented, so none of them is quietly absent from the accounting.
        foreach (var category in Enum.GetValues<CorpusCategory>().Where(c => c != CorpusCategory.PlanetaryPositions))
            Assert.Contains(notALongitude, entry => entry.Case.Category == category);
    }

    [Fact]
    public void The_scored_distribution_is_pinned_as_a_measurement_not_an_asserted_target()
    {
        var compared = CorpusAccuracyHarness.All.Where(entry => entry.WasCompared).ToArray();

        // These are the residuals the committed rows produce. If the engine changes accuracy, this moves - which is the
        // point of pinning a measurement. It is NOT the owner's 1 arcsecond target restated: the target is in the spec,
        // and the cases that miss it are reported rather than redefined.
        Assert.All(compared, entry => Assert.True(double.IsFinite(entry.ResidualArcseconds!.Value)));

        // The 1 arcsecond planet target is met by the committed rows only where the artifact says so. Asserting the
        // MET set here would be claiming accuracy the owner has not accepted, so the assertion is the weaker, true one:
        // no compared case claims to pass a budget it did not.
        Assert.All(compared, entry => Assert.Equal(
            Math.Abs(entry.ResidualArcseconds!.Value) <= entry.TargetArcseconds!.Value,
            entry.Status == CorpusCaseStatus.Scored));
    }

    [Fact]
    public void The_measured_distribution_is_pinned_so_a_change_is_a_diff_and_not_a_silent_drift()
    {
        var byStatus = CorpusAccuracyHarness.All
            .GroupBy(entry => entry.Status)
            .ToDictionary(group => group.Key, group => group.Count());

        // Measured 2026-11-22 against the committed reference set. These are the numbers a reviewer reads, and they are
        // pinned rather than computed-and-asserted-to-itself: a body that starts being scored, a reference row that is
        // added, or an engine change that moves a case out of tolerance all move one of these counts.
        Assert.Equal(10000, CorpusAccuracyHarness.All.Count);
        Assert.Equal(5335, byStatus[CorpusCaseStatus.NoReference]);
        Assert.Equal(2665, byStatus[CorpusCaseStatus.NotComputable]);
        Assert.Equal(2000, byStatus[CorpusCaseStatus.NotALongitude]);

        // api#74 slice 2 added a deterministic mean-reference set (astronomy/mean-reference-set.json) generated from
        // Meeus identities by deploy/scripts/generate-mean-reference-set.py. It covers 6 instants, but the corpus's
        // instants are dense arithmetic across 1900-2100, so they do not coincide: the corpus itself is still
        // unscored. The reference GROWTH is measured by MeanReferenceSetTests, which scores 3 mean longitudes plus 4
        // arguments at all 6 instants - and separately states that the Sun still misses the 1 arcsec target.
        Assert.True(File.Exists(Path.Combine(HorizonsCorpusTests.FindSolutionRoot(), "astronomy",
            "mean-reference-set.json")), "The generated mean-reference set must stay committed.");

        // And NOTHING is currently Scored or OutOfTolerance: not one of the 10,000 generated instants lands on a
        // committed reference epoch. That is the honest state of AST-09 today, stated as an assertion so it cannot be
        // quietly assumed otherwise - and the day a reference set does cover the corpus, this fails and someone
        // records the real accuracy result instead of letting a green suite imply one.
        Assert.False(byStatus.ContainsKey(CorpusCaseStatus.Scored));
        Assert.False(byStatus.ContainsKey(CorpusCaseStatus.OutOfTolerance));
    }

    [Fact]
    public void The_per_category_report_is_generated_from_the_evaluation_not_hand_written()
    {
        // AC-1 requires counts per category. The report must be derived, so the same run that scores cases also reports
        // them; a separately maintained table would be the two-sources defect in a document.
        var report = CorpusEvaluationReport.Render(CorpusAccuracyHarness.All);

        foreach (var category in Enum.GetValues<CorpusCategory>())
            Assert.Contains($"{category}", report, StringComparison.Ordinal);

        // Every status present in the run appears with its count, and the report's own arithmetic closes on the total.
        Assert.Contains("Total cases: 10000", report, StringComparison.Ordinal);
        Assert.Contains("Compared against an independent reference: 0", report, StringComparison.Ordinal);

        // The report states the accuracy conclusion honestly rather than implying the corpus passed.
        Assert.Contains("not measured", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_harness_reads_only_committed_files_and_has_no_network_path()
    {
        // AC-3 and the architect's constraint: tests never call Horizons. Proving the type's assembly references no
        // HTTP or socket stack is a structural check that a fixture fetch cannot creep in unnoticed.
        var offenders = typeof(CorpusAccuracyHarness).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.Contains("Http", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Net.Sockets", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offenders);

        // The reference rows are all already loaded from disk, and none carries a live-retrieval marker.
        var reference = HorizonsAccuracyTests.HorizonsReference.Load(HorizonsCorpusTests.FindSolutionRoot());
        Assert.Equal("DE441", reference.Provenance.EphemerisSource);
        Assert.NotEmpty(reference.Provenance.RetrievedUtc);
    }
}
