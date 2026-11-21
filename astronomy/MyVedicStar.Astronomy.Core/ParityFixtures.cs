using System.Text.Json;
using System.Text.Json.Serialization;
using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// One checked-in parity fixture: a reference tropical longitude for a body at an instant, with the tolerance that
/// applies. Tolerance lives in the DATA, not in a code comment, so raising it is a visible diff in a fixture file
/// (FR-06 architect section C).
/// </summary>
/// <remarks>
/// <para><b>Why <see cref="Claim"/> is required and not defaulted (api#130).</b> A body can carry more than one
/// published tolerance: the composed engine reference (Meeus 47.a plus the Drik offset, 3 arcsec) and the unoffset
/// series alone (Meeus 47.a's own published precision, about 1.8 arcsec) are different budgets for the same body at
/// the same instant. Keying only on body made the second one either unrepresentable or ambiguous by ordering, which is
/// how it ended up as a literal in a test - a value nobody owned.</para>
/// <para>The key is explicitly <c>(Body, Claim)</c>. The field is non-nullable so an absent discriminator fails to
/// deserialise rather than silently joining the default set: a default here would make omission a legal value again,
/// which is the defect in one field.</para>
/// </remarks>
public sealed record ParityFixtureRecord(
    string Body,
    /// <summary>
    /// A <c>string</c>, deliberately NOT a <see cref="FixtureClaim"/>. THIS ASYMMETRY IS THE GUARD, not an oversight.
    /// </summary>
    /// <remarks>
    /// <see cref="ParityFixture.Claim"/> is the enum with no default, so a code-built row must state its claim. This
    /// record stays a string so the JSON path cannot acquire the same convenience: an unknown or absent value is
    /// carried through as text and must be converted by <see cref="ParityFixtures.ParseClaim"/>, which throws on null,
    /// whitespace and unrecognised values.
    ///
    /// Giving this field the enum type would let System.Text.Json bind a missing or mistyped claim to the enum's
    /// zeroth member - which is EngineReference - silently putting the row into the parity gate. That is exactly the
    /// default #135 closed and #136 closed on the constructor. If a later reader is tempted to unify the two types
    /// for tidiness, this paragraph is the reason not to.
    /// </remarks>
    string Claim,
    double JulianDayUt,
    double TropicalLongitudeDegrees,
    double ToleranceArcseconds,
    string Source);

/// <summary>
/// The checked-in parity fixture set.
/// </summary>
/// <remarks>
/// <para><b>What these fixtures are, stated honestly.</b> They are generated from the current analytical
/// implementation and record ITS values, so they prove <i>agreement between the two providers</i> rather than
/// absolute astronomical truth. That is exactly what the parity gate is for: it detects that SPICE and the analytical
/// series disagree, which is the regression AST-06 must prevent.</para>
/// <para>They are <b>not</b> an independent accuracy claim. Absolute accuracy against JPL Horizons
/// (FR-02 AC-5, FR-04 AC-5) requires Horizons-derived fixtures, which live in a separate artifact
/// (<c>horizons-fixtures.json</c>) so this file's self-comparison can never be mistaken for them. That obligation
/// stays with AST-09 (#74) and is listed as outstanding rather than quietly satisfied by self-comparison.</para>
/// <para><b>Correction:</b> this paragraph previously said Horizons "cannot be fetched" in this environment. That was
/// wrong - the service is reachable from the build host, and a stale note claiming a capability is unavailable is the
/// most expensive kind of comment, because it stops anyone from trying. What limits the fixture is not access but the
/// <c>APPARENT</c>/geometric parameter set, which is unresolved (see #140).</para>
/// </remarks>
public static class ParityFixtures
{
    /// <summary>File name inside the solution directory.</summary>
    public const string FileName = "parity-fixtures.json";

    /// <summary>Bodies covered by the fixture set. Sun and Moon are the four-method port's actual surface; the
    /// planets are included because FR-02 requires them.</summary>
    public static readonly IReadOnlyList<CelestialBody> CoveredBodies =
    [
        CelestialBody.Sun, CelestialBody.Moon, CelestialBody.Mercury, CelestialBody.Venus, CelestialBody.Mars,
        CelestialBody.Jupiter, CelestialBody.Saturn
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads the fixture set from a directory, or from <c>astronomy/</c> beneath it. Both shapes are accepted because the
    /// astronomy tests run from inside the solution while the product tests run from the repository root.
    /// </summary>
    public static IReadOnlyList<ParityFixture> Load(string solutionRoot)
    {
        var direct = Path.Combine(solutionRoot, FileName);
        var nested = Path.Combine(solutionRoot, "astronomy", FileName);
        var path = File.Exists(direct) ? direct : nested;
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Parity fixture file '{FileName}' is missing at '{direct}' or '{nested}'. The parity gate cannot run "
                + "without it.", path);

        var records = JsonSerializer.Deserialize<List<ParityFixtureRecord>>(File.ReadAllText(path), SerializerOptions)
                      ?? throw new InvalidOperationException($"Parity fixture file '{FileName}' is empty or invalid.");

        // Enum.Parse on both discriminator fields, not a default: an unrecognised or missing Claim must fail to load
        // rather than land in EngineReference. That is what makes omission a compile/serialisation error instead of a
        // silently correct-looking value (api#130).
        return records.Select(r => new ParityFixture(Enum.Parse<CelestialBody>(r.Body, ignoreCase: true),
            r.JulianDayUt, r.TropicalLongitudeDegrees, r.Source, r.ToleranceArcseconds,
            ParseClaim(r))).ToArray();
    }

    public static void Save(string solutionRoot, IEnumerable<ParityFixture> fixtures)
    {
        var records = fixtures.Select(f => new ParityFixtureRecord(f.Body.ToString(), f.Claim.ToString(), f.JulianDayUt,
            f.TropicalLongitudeDegrees, f.ToleranceArcseconds, f.Source)).ToList();
        File.WriteAllText(Path.Combine(solutionRoot, FileName),
            JsonSerializer.Serialize(records, SerializerOptions));
    }

    /// <summary>
    /// The tolerance for one published claim about one body, or a failure naming both.
    /// </summary>
    /// <remarks>
    /// Keyed on <c>(body, claim)</c> explicitly. A body-only lookup cannot answer "which tolerance" when a body carries
    /// two claims, which is why the unoffset series tolerance was a literal in a test rather than data (api#130). The
    /// failure is thrown rather than defaulted: a missing row must not resolve to a permissive budget.
    /// <para>
    /// api#148 widened the key from (body, claim) to (body, claim, instant) for the same reason, one axis further on.
    /// The Moon now carries two rows with the same claim class at instants about eighteen years apart (Meeus 47.a and
    /// the instant the Drik offset was measured at), so claim alone is ambiguous again. The instant is an optional
    /// filter so the api#130 callers keep their meaning: omitting it resolves only a claim with exactly one row, and
    /// refuses when it has several, rather than resolving one by ordering.
    /// </para>
    /// </remarks>
    public static double ToleranceArcseconds(string solutionRoot, CelestialBody body, FixtureClaim claim,
        double? julianDayUt = null)
    {
        var matches = Load(solutionRoot)
            .Where(fixture => fixture.Body == body && fixture.Claim == claim
                && (julianDayUt is null || fixture.JulianDayUt == julianDayUt.Value))
            .ToArray();

        if (matches.Length == 1)
            return matches[0].ToleranceArcseconds;

        // Zero matches is the api#130 failure. More than one is the api#148 failure and gets its own message: a body
        // that has acquired a second row for the same claim must not silently resolve by ordering, which is the
        // ambiguity the Claim field was introduced to remove and which this returns one level down if left unguarded.
        throw new InvalidOperationException(
            matches.Length == 0
                ? $"No {claim} reference for {body}{(julianDayUt is null ? string.Empty : $" at JD {julianDayUt}")} "
                  + $"in {FileName}, so no tolerance can be defended."
                : $"The {claim} reference for {body} is ambiguous in {FileName}: {matches.Length} rows match and no "
                  + "instant was named. Name the instant; picking one by ordering is how a budget becomes unowned.");
    }

    /// <summary>The rows the analytic-vs-SPICE parity gate compares. Only one claim has an engine value to compare.</summary>
    public static IEnumerable<ParityFixture> Comparisons(IEnumerable<ParityFixture> fixtures) =>
        fixtures.Where(fixture => fixture.Claim == FixtureClaim.EngineReference);

    private static FixtureClaim ParseClaim(ParityFixtureRecord record) =>
        string.IsNullOrWhiteSpace(record.Claim)
            ? throw new InvalidOperationException(
                $"Parity fixture row for '{record.Body}' has no claim. Every row must state which published claim it "
                + "measures; an absent discriminator cannot be defaulted without making omission a legal value.")
            : Enum.Parse<FixtureClaim>(record.Claim, ignoreCase: true);

    /// <summary>The date range the fixtures must cover, from the AST-09 fixture span.</summary>
    public static readonly DateOnly From = new(1800, 1, 1);
    public static readonly DateOnly To = new(2200, 12, 31);
}
