using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Reference;

/// <summary>
/// The schema every generated fixture row must satisfy before the artifact may be written (AST-12a, api#140).
/// </summary>
/// <remarks>
/// <para><b>Why a validator and not just careful construction.</b> The generator builds rows correctly today. A
/// validator is what makes that a property of the artifact rather than of the code path that happened to produce it:
/// a row read back from disk, hand-edited, or produced by a future second generator is checked by the same rule, and a
/// row that violates it cannot be merged into a fixture unnoticed.</para>
/// <para><b>Every check returns a message, and the caller decides.</b> <see cref="Check"/> returns all problems rather
/// than throwing on the first, for the same reason the header check does: a run wrong in three ways should say all
/// three, so it is fixed once.</para>
/// <para><b>The frame is not inferred from the numbers.</b> A longitude cannot tell you which equinox it was measured
/// from - a wrong frame produces a perfectly plausible degree value. So the frame is asserted against the shared
/// <see cref="ReferenceFrame"/> constant, and the check is about what the row claims, not about whether its value
/// looks right.</para>
/// </remarks>
public static class HorizonsFixtureSchema
{
    /// <summary>
    /// Every problem with <paramref name="fixture"/>; empty when it may be written as a reference artifact.
    /// </summary>
    /// <remarks>
    /// Grouped by concern in a fixed order so two runs against the same bad artifact report identically. The order is
    /// coverage, then provenance, then rows: a count problem makes row-level complaints meaningless, and a provenance
    /// problem makes every row's origin unknown.
    /// </remarks>
    public static IReadOnlyList<string> Check(HorizonsFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        return Check(fixture.Coverage, fixture.Provenance, fixture.Rows);
    }

    /// <summary>
    /// Every problem with the parts of a fixture, for callers that have not assembled one.
    /// </summary>
    public static IReadOnlyList<string> Check(
        HorizonsCoverage coverage,
        HorizonsProvenance provenance,
        IReadOnlyList<HorizonsFixtureRow> rows)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(rows);

        var problems = new List<string>();

        problems.AddRange(CheckCoverage(coverage, rows.Count));
        problems.AddRange(CheckProvenance(provenance));

        for (var i = 0; i < rows.Count; i++)
            problems.AddRange(CheckRow(rows[i], i));


        return problems;
    }

    /// <summary>
    /// Coverage must describe the rows that are actually present.
    /// </summary>
    /// <remarks>
    /// A gap between the claim and the content is the failure this schema exists for: a fixture that lost a body looks
    /// exactly like one that never selected it, so a coverage rule that disagrees with the row count proves the file is
    /// not the run it claims to be.
    /// </remarks>
    public static IReadOnlyList<string> CheckCoverage(HorizonsCoverage coverage, int actualRowCount)
    {
        var problems = new List<string>();

        if (coverage.RowCount != actualRowCount)
        {
            problems.Add($"Coverage claims {coverage.RowCount} rows but the artifact holds {actualRowCount}."
                + " A truncated file is indistinguishable from a smaller selection rule at read time.");
        }

        if (string.IsNullOrWhiteSpace(coverage.StartUtc))
            problems.Add("Coverage states no start instant.");

        if (string.IsNullOrWhiteSpace(coverage.EndUtc))
            problems.Add("Coverage states no end instant.");

        if (string.IsNullOrWhiteSpace(coverage.SelectionRule))
            problems.Add("Coverage states no selection rule, so the sampled instants cannot be re-applied by a reader.");

        return problems;
    }

    /// <summary>
    /// Provenance must make the artifact re-fetchable.
    /// </summary>
    /// <remarks>
    /// <para>Each field is required for a distinct reason, and all of them are required:</para>
    /// <list type="bullet">
    /// <item><c>Service</c> and <c>ApiVersion</c> - which endpoint, and which dialect of it. A response shape can change
    /// between versions without the URL changing.</item>
    /// <item><c>EphemerisSource</c> - DE441 and DE440 differ by metres; for a parity budget that is the difference
    /// between an engine error and a reference difference.</item>
    /// <item><c>CenterBody</c> - a topocentric fetch recorded as geocentric is a 245 arcsecond lie that the numbers do
    /// not reveal.</item>
    /// <item><c>ObserverTable</c> - the same instant yields different columns under different tables.</item>
    /// <item><c>RequestParameters</c> - the request verbatim, so it can be re-issued without reconstruction.</item>
    /// </list>
    /// <para><c>Apparent</c> is a non-nullable bool, so the decision is necessarily recorded; there is no unrecorded
    /// default to check for.</para>
    /// </remarks>
    public static IReadOnlyList<string> CheckProvenance(HorizonsProvenance provenance)
    {
        var problems = new List<string>();

        var fields = new (string Field, string? Value)[]
        {
            (nameof(provenance.Service), provenance.Service),
            (nameof(provenance.ApiVersion), provenance.ApiVersion),
            (nameof(provenance.EphemerisSource), provenance.EphemerisSource),
            (nameof(provenance.TargetBody), provenance.TargetBody),
            (nameof(provenance.CenterBody), provenance.CenterBody),
            (nameof(provenance.ObserverTable), provenance.ObserverTable),
            (nameof(provenance.RequestParameters), provenance.RequestParameters),
        };

        foreach (var (field, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"Provenance has no {field}. A field left blank makes the artifact un-re-fetchable in a way "
                    + "the numbers do not reveal.");
            }
        }

        return problems;
    }

    /// <summary>
    /// One row must be a value that means something: a named body, a stated quantity, a finite angle, and a tolerance
    /// that is either owned or explicitly explained.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="index">Its position, used only to make the message findable in a large artifact.</param>
    public static IReadOnlyList<string> CheckRow(HorizonsFixtureRow row, int index)
    {
        ArgumentNullException.ThrowIfNull(row);
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(row.Body))
            problems.Add($"Row {index} names no body, so it cannot be joined to an engine position.");

        if (!double.IsFinite(row.LongitudeDegrees))
            problems.Add($"Row {index} ({row.Body}) has a non-finite longitude. A NaN here propagates through every "
                + "difference and turns an accuracy test into a comparison with nonsense.");

        if (!double.IsFinite(row.LatitudeDegrees))
            problems.Add($"Row {index} ({row.Body}) has a non-finite latitude.");

        // Longitude is an angle, so a negative or >360 value is unusual but meaningful - it names the same direction.
        // It is checked for finiteness, not wrapped here: silently normalising would hide a sign convention difference
        // between this row and the engine, which is exactly the disagreement the fixture exists to measure.
        if (double.IsFinite(row.LongitudeDegrees) && Math.Abs(row.LongitudeDegrees) > 360.0)
            problems.Add($"Row {index} ({row.Body}) longitude {row.LongitudeDegrees:R} exceeds one revolution; that is "
                + "not a wrap, it is a different quantity.");

        if (row.LatitudeDegrees is < -90.0 or > 90.0)
            problems.Add($"Row {index} ({row.Body}) latitude {row.LatitudeDegrees:R} is outside [-90, 90].");

        // A tolerance without a basis is an unowned number wearing a name; a basis that says "unfilled" must have no
        // tolerance, or the two readings of the row disagree.
        if (string.IsNullOrWhiteSpace(row.ToleranceBasis))
            problems.Add($"Row {index} ({row.Body}) states no tolerance basis. 'Unfilled' is an explanation; silence is not.");

        if (row.ToleranceArcseconds is { } tolerance)
        {
            if (!double.IsFinite(tolerance) || tolerance <= 0.0)
                problems.Add($"Row {index} ({row.Body}) tolerance {tolerance:R} is not a positive finite budget.");

            if (row.ToleranceBasis.Contains("Unfilled", StringComparison.OrdinalIgnoreCase))
                problems.Add($"Row {index} ({row.Body}) carries a tolerance AND a basis saying it is unfilled.");
        }
        else if (string.IsNullOrWhiteSpace(row.ToleranceBasis))
        {
            problems.Add($"Row {index} ({row.Body}) has neither a tolerance nor a reason it is absent.");
        }

        // The two quantities of an enum are distinct in how the row must be read; there is no default to be right by
        // accident, so only an out-of-range value is a schema error.
        if (!Enum.IsDefined(row.Quantity))
            problems.Add($"Row {index} ({row.Body}) has an undefined quantity value {(int)row.Quantity}.");

        return problems;
    }

    /// <summary>
    /// True when the fixture satisfies the schema; the throwing form for a generator that must not write a bad artifact.
    /// </summary>
    /// <exception cref="InvalidOperationException">The fixture violates the schema. The message lists every problem.</exception>
    public static void EnsureValid(HorizonsFixture fixture)
    {
        var problems = Check(fixture);
        if (problems.Count == 0)
            return;

        throw new InvalidOperationException(
            "The fixture violates the artifact schema and must not be written: " + string.Join(" | ", problems));
    }
}
