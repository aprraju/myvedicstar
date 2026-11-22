using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// What became of one enumerated AST-09 case when the engine was actually run against it (issue #74).
/// </summary>
/// <remarks>
/// <para><b>Why a status rather than a bool.</b> A case can fail to be measured for several genuinely different reasons,
/// and collapsing them into "pass/fail" is how a corpus reports 10,000 cases while measuring almost none of them: a
/// case with no reference value would have to be either silently skipped (invisible) or counted as passing (a lie).
/// Neither is acceptable here, so "not scored" is its own answer, with its own reason.</para>
/// <para><b>The scored states never include a waiver.</b> <see cref="CorpusCaseStatus.Scored"/> means a reference exists
/// and the engine was compared against it; <see cref="CorpusCaseStatus.OutOfTolerance"/> means it was compared and
/// missed its owner target. A miss is listed, never absorbed - AC-5 requires that a failure maps to the FR that owns
/// it.</para>
/// </remarks>
public enum CorpusCaseStatus
{
    /// <summary>Computed and compared against a reference, and the residual is inside the owner target.</summary>
    Scored,

    /// <summary>Computed and compared, and the residual is outside the owner target. Named, not waived.</summary>
    OutOfTolerance,

    /// <summary>No independent reference value covers this case, so nothing could be compared.</summary>
    NoReference,

    /// <summary>The engine cannot produce a longitude for this body, so there is no value to compare.</summary>
    NotComputable,

    /// <summary>
    /// The case's category is not a geocentric longitude, so a longitude reference cannot score it. Comparing it would
    /// measure a different quantity and report a match.
    /// </summary>
    NotALongitude,

    /// <summary>
    /// The case is a longitude at an instant this reproduction cannot resolve (a date before the engine's own supported
    /// range, or an edge case whose local time is not a single instant). Stated as unmeasurable, never as passing.
    /// </summary>
    NotMeasurable
}

/// <summary>One evaluated case: its identity, its status and - when compared - how far off it was.</summary>
/// <param name="Case">The enumerated case this row accounts for.</param>
/// <param name="Status">What became of it. Every enumerated case gets exactly one.</param>
/// <param name="ResidualArcseconds">The measured residual, present exactly when the case was compared.</param>
/// <param name="TargetArcseconds">The owner-approved budget for the body, present exactly when compared.</param>
/// <param name="Detail">Why an unscored case is unscored, or the numbers for a compared one. Never empty.</param>
public sealed record CorpusEvaluation(
    CorpusCase Case,
    CorpusCaseStatus Status,
    double? ResidualArcseconds,
    double? TargetArcseconds,
    string Detail)
{
    /// <summary>True only when the engine was actually compared against a reference.</summary>
    public bool WasCompared => Status is CorpusCaseStatus.Scored or CorpusCaseStatus.OutOfTolerance;

    public override string ToString() =>
        $"{Case.Id}: {Status}" + (ResidualArcseconds is { } residual
            ? $" ({residual:F3} / {TargetArcseconds:F3})"
            : string.Empty)
        + $" - {Detail}";
}

/// <summary>
/// Runs the enumerated AST-09 corpus against the engine and accounts for every case (issue #74).
/// </summary>
/// <remarks>
/// <para><b>This is the missing half of the story.</b> The corpus could already enumerate 10,000 cases and prove the
/// enumeration was deterministic; nothing ran them. A count is not a measurement, so the previous suite went green
/// while proving no accuracy claim at all. This type closes that: every case is evaluated and gets a stated outcome, so
/// the headline is "N cases compared" rather than "10,000 cases exist".</para>
/// <para><b>No network, by construction.</b> The only independent reference is <c>astronomy/horizons-reference.json</c>,
/// committed as data. Nothing here reaches JPL Horizons, so a CI job cannot be broken by a rate limit or an outage.</para>
/// <para><b>The reference coverage is honest and small.</b> The committed artifact holds 14 rows at two epochs, and the
/// corpus's generated instants are dense arithmetic that lands on those two epochs only by coincidence. Most cases are
/// therefore <see cref="CorpusCaseStatus.NoReference"/> - unmeasured and counted as such. That is the real state today.
/// Inflating it by comparing against values the implementation produced is exactly the self-reference AC-3 forbids, and
/// fabricating reference values to fill the gap would be worse than reporting the gap.</para>
/// </remarks>
public static class CorpusAccuracyHarness
{
    private const double ArcsecondsPerDegree = 3600.0;

    private static readonly Lazy<CorpusCase[]> Cases = new(() => HorizonsCorpusSpec
        .Load(HorizonsCorpusTests.FindSolutionRoot()).Enumerate().ToArray());

    private static readonly Lazy<HorizonsAccuracyTests.HorizonsReference> Reference =
        new(() => HorizonsAccuracyTests.HorizonsReference.Load(HorizonsCorpusTests.FindSolutionRoot()));

    private static readonly Lazy<IReadOnlyList<CorpusEvaluation>> Evaluated = new(EvaluateAll);

    /// <summary>The enumerated cases, materialised once so the enumeration runs a single time per test run.</summary>
    internal static IReadOnlyList<CorpusCase> EnumeratedCases => Cases.Value;

    /// <summary>Every evaluated case, computed once. Order matches the enumeration order.</summary>
    internal static IReadOnlyList<CorpusEvaluation> All => Evaluated.Value;

    /// <summary>The corpus document the tolerances come from, so the harness never carries its own budgets.</summary>
    internal static HorizonsCorpusSpec Spec => HorizonsCorpusSpec.Load(HorizonsCorpusTests.FindSolutionRoot());

    private static IReadOnlyList<CorpusEvaluation> EvaluateAll()
    {
        var spec = Spec;
        var planets = new MeeusGrahaPositionsProvider();
        var sunMoon = new MeeusEphemeris();

        return EnumeratedCases.Select(caseItem => Evaluate(caseItem, spec, planets, sunMoon)).ToArray();
    }

    private static CorpusEvaluation Evaluate(CorpusCase caseItem, HorizonsCorpusSpec spec,
        MeeusGrahaPositionsProvider planets, IEphemeris sunMoon)
    {
        // Only a planetary-position case is a geocentric longitude at a named instant. The other categories pose
        // different questions - a transition time, an event time, an angle at a site - and scoring them with a longitude
        // reference would measure one quantity and report another.
        if (caseItem.Category != CorpusCategory.PlanetaryPositions)
            return Unscored(caseItem, CorpusCaseStatus.NotALongitude,
                $"{caseItem.Category} is not a geocentric longitude; a longitude reference cannot score it.");

        if (caseItem.Body is not { } body)
            return Unscored(caseItem, CorpusCaseStatus.NotALongitude, "The case names no body.");

        // The engine's reachable surface is the nine grahas; Uranus, Neptune and Pluto are named by the matrix but not
        // producible, so there is no value to attribute to accuracy (or to a reference).
        if (EngineLongitude(body, caseItem.JulianDayUt, planets, sunMoon) is not { } engineValue)
            return Unscored(caseItem, CorpusCaseStatus.NotComputable,
                $"The engine produces no longitude for {body}.");

        var targets = TargetArcseconds(spec, body);
        if (targets is null)
            return Unscored(caseItem, CorpusCaseStatus.NotMeasurable,
                $"No owner-approved tolerance covers {body}, so the case cannot be scored against one.");

        var row = FindReferenceRow(body, caseItem.JulianDayUt);
        if (row is null)
            return Unscored(caseItem, CorpusCaseStatus.NoReference,
                $"No committed Horizons row for {body} at JD {caseItem.JulianDayUt:R}.");

        var target = targets.Value;
        var residual = NormalizeSignedDifference(engineValue - row.LongitudeDegrees) * ArcsecondsPerDegree;
        var status = Math.Abs(residual) <= target ? CorpusCaseStatus.Scored : CorpusCaseStatus.OutOfTolerance;

        return new CorpusEvaluation(caseItem, status, residual, target,
            $"engine {engineValue:F7}, reference {row.LongitudeDegrees:F7}, residual {residual:F3} against {target:F3}.");
    }

    /// <summary>The owner-approved budget for a body, or null when no target covers it.</summary>
    /// <remarks>
    /// The tolerance always comes from the spec document, never from a literal in this harness, so widening one is a
    /// visible diff in data. A body the spec has no row for returns null rather than a default: a missing target must
    /// not turn "unmeasured" into "passing".
    /// </remarks>
    private static double? TargetArcseconds(HorizonsCorpusSpec spec, CelestialBody body)
    {
        try
        {
            return spec.Tolerances.ArcsecondsFor(body);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The difference folded into (-180, 180], so a body either side of 0/360 compares to its neighbour.</summary>
    private static double NormalizeSignedDifference(double degrees)
    {
        var wrapped = degrees % 360.0;
        if (wrapped > 180.0)
            wrapped -= 360.0;
        if (wrapped <= -180.0)
            wrapped += 360.0;
        return wrapped;
    }

    /// <summary>The committed row for a body at exactly this instant, or null when none is checked in.</summary>
    /// <remarks>
    /// Exact equality on the written instant, deliberately not an epsilon. A tolerance here would let a case inherit
    /// the reference row of an instant a few seconds away, attributing that row's residual to a time nobody measured.
    /// </remarks>
    private static HorizonsAccuracyTests.HorizonsReferenceRow? FindReferenceRow(CelestialBody body, double julianDayUt)
    {
        var name = body.ToString();
        return Reference.Value.Rows.FirstOrDefault(row =>
            string.Equals(row.Body, name, StringComparison.OrdinalIgnoreCase)
            && row.JulianDayUtResolved == julianDayUt);
    }

    /// <summary>The engine's tropical longitude for a body, or null when the provider cannot produce it.</summary>
    /// <remarks>
    /// <para><b>Mean Node is computed, and deliberately not by the planet path.</b> <see cref="CelestialBody.MeanNode"/> is
    /// the product's Rahu, and the engine produces it from <see cref="MeanLunarNode"/> - the omega term - not from the
    /// Keplerian element fit. Falling through to the planet default would have measured a fit for a body that has its
    /// own defined series, which is a wrong answer that looks like a computed one; <c>HorizonsAccuracyTests</c>' mapping
    /// had no MeanNode arm for exactly this reason and returned null. It is reachable here because the corpus scores it.</para>
    /// <para><b>True Node and Uranus/Neptune/Pluto are genuinely absent.</b> There is no true-node implementation in the
    /// engine at all (the node code computes the mean node only), and the planet fit has no orbit beyond Saturn. Those
    /// return null and are reported NotComputable rather than approximated.</para>
    /// </remarks>
    /// <summary>The body-to-longitude decision, exposed so the reference-scoring suite shares this one mapping.</summary>
    /// <remarks>
    /// Test-only visibility rather than a second public surface: exactly one place decides whether a body is
    /// computable, so a suite cannot score a body the harness calls uncomputable.
    /// </remarks>
    internal static double? EngineLongitudeForTesting(CelestialBody body, double julianDayUt) =>
        EngineLongitude(body, julianDayUt, new MeeusGrahaPositionsProvider(), new MeeusEphemeris());

    private static double? EngineLongitude(CelestialBody body, double julianDayUt,
        MeeusGrahaPositionsProvider planets, IEphemeris sunMoon) => body switch
    {
        CelestialBody.Sun => sunMoon.GetSunLongitude(julianDayUt),
        CelestialBody.Moon => sunMoon.GetMoonLongitude(julianDayUt),
        CelestialBody.MeanNode => MeanLunarNode.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(julianDayUt)),
        CelestialBody.Mercury => planets.TropicalLongitude(Graha.Mercury, julianDayUt),
        CelestialBody.Venus => planets.TropicalLongitude(Graha.Venus, julianDayUt),
        CelestialBody.Mars => planets.TropicalLongitude(Graha.Mars, julianDayUt),
        CelestialBody.Jupiter => planets.TropicalLongitude(Graha.Jupiter, julianDayUt),
        CelestialBody.Saturn => planets.TropicalLongitude(Graha.Saturn, julianDayUt),
        _ => null
    };

    private static CorpusEvaluation Unscored(CorpusCase caseItem, CorpusCaseStatus status, string detail) =>
        new(caseItem, status, null, null, detail);
}
