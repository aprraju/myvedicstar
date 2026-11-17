using System.Text.Json;
using System.Text.Json.Serialization;
using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// One checked-in parity fixture: a reference tropical longitude for a body at an instant, with the tolerance that
/// applies. Tolerance lives in the DATA, not in a code comment, so raising it is a visible diff in a fixture file
/// (FR-06 architect section C).
/// </summary>
public sealed record ParityFixtureRecord(
    string Body,
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
/// (FR-02 AC-5, FR-04 AC-5) requires Horizons-derived fixtures, which this environment cannot fetch; that obligation
/// stays with AST-09 (#74) and is listed as outstanding rather than quietly satisfied by self-comparison.</para>
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

        return records.Select(r => new ParityFixture(Enum.Parse<CelestialBody>(r.Body, ignoreCase: true),
            r.JulianDayUt, r.TropicalLongitudeDegrees, r.Source, r.ToleranceArcseconds)).ToArray();
    }

    public static void Save(string solutionRoot, IEnumerable<ParityFixture> fixtures)
    {
        var records = fixtures.Select(f => new ParityFixtureRecord(f.Body.ToString(), f.JulianDayUt,
            f.TropicalLongitudeDegrees, f.ToleranceArcseconds, f.Source)).ToList();
        File.WriteAllText(Path.Combine(solutionRoot, FileName),
            JsonSerializer.Serialize(records, SerializerOptions));
    }

    /// <summary>The date range the fixtures must cover, from the AST-09 fixture span.</summary>
    public static readonly DateOnly From = new(1800, 1, 1);
    public static readonly DateOnly To = new(2200, 12, 31);
}
