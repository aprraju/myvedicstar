using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVedicStar.Astronomy.Core;

/// <summary>One checked-in independent reference value for a graha's tropical longitude.</summary>
/// <param name="ToleranceArcseconds">The budget this case must meet. It is derived from the source, not from the
/// implementation's output: the fixture records the published precision of the reference, so widening it is a visible
/// diff in data rather than a quiet edit to an assertion.</param>
public sealed record PlanetaryReferenceRecord(
    string Graha,
    double JulianDayUt,
    double TropicalLongitudeDegrees,
    double ToleranceArcseconds,
    string Source);

/// <summary>
/// The planetary accuracy references, kept separate from <see cref="ParityFixtures"/> on purpose.
/// </summary>
/// <remarks>
/// <para><b>Why a second file.</b> <see cref="ParityFixtures"/> holds provider-agreement fixtures generated from the
/// analytical implementation itself, which is exactly right for the AST-06 parity gate and exactly useless as an
/// accuracy claim. Mixing the two would let a self-generated number inherit the credibility of a published one.</para>
/// <para><b>What a fixture in this file must be.</b> A value a third party published, with the instant and the
/// published precision, so anyone can re-derive the tolerance. A generated value must never be checked in here.</para>
/// </remarks>
public static class PlanetaryReferences
{
    /// <summary>File name inside the solution directory (and beneath <c>astronomy/</c>).</summary>
    public const string FileName = "planetary-references.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads the reference set. A missing file is an <b>empty</b> set rather than an error: the honest state of this
    /// release is that no planetary reference has been checked in yet, and the engine must publish that state rather
    /// than fail to start.
    /// </summary>
    public static IReadOnlyList<PlanetaryReference> Load(string solutionRoot)
    {
        var direct = Path.Combine(solutionRoot, FileName);
        var nested = Path.Combine(solutionRoot, "astronomy", FileName);
        var path = File.Exists(direct) ? direct : nested;
        if (!File.Exists(path))
            return [];

        var records = JsonSerializer.Deserialize<List<PlanetaryReferenceRecord>>(File.ReadAllText(path), SerializerOptions)
                      ?? throw new InvalidOperationException($"Reference file '{FileName}' is empty or invalid.");

        return records
            .Select(r => new PlanetaryReference(Enum.Parse<Graha>(r.Graha, ignoreCase: true), r.JulianDayUt,
                r.TropicalLongitudeDegrees, r.ToleranceArcseconds, r.Source))
            .ToArray();
    }

    /// <summary>
    /// Resolves the load root from the test/agent working directory upward, so a caller does not have to know whether
    /// it is running from the repository root, the solution directory or a bin folder.
    /// </summary>
    public static IReadOnlyList<PlanetaryReference> LoadFromAncestors(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var direct = Path.Combine(directory.FullName, FileName);
            var nested = Path.Combine(directory.FullName, "astronomy", FileName);
            if (File.Exists(direct) || File.Exists(nested))
                return Load(directory.FullName);
            directory = directory.Parent;
        }

        return [];
    }

    /// <summary>
    /// The per-body tolerance the envelope may publish, as the tightest independently-sourced budget checked in.
    /// A body absent here has no defensible tolerance and must not be asserted as validated.
    /// </summary>
    public static IReadOnlyDictionary<string, double> PerBodyToleranceArcseconds(IEnumerable<PlanetaryReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        return references
            .GroupBy(reference => reference.Graha)
            .ToDictionary(group => group.Key.ToString(), group => group.Min(reference => reference.ToleranceArcseconds),
                StringComparer.Ordinal);
    }
}

/// <summary>One independent reference case for a graha's tropical longitude of date.</summary>
/// <param name="Graha">Body the reference is for.</param>
/// <param name="JulianDayUt">Instant, Julian Day UT, matching the port's time argument.</param>
/// <param name="TropicalLongitudeDegrees">Reference tropical longitude of date, degrees.</param>
/// <param name="ToleranceArcseconds">The published precision of the source, which is the budget this case must meet.</param>
/// <param name="Source">Provenance, including the frame and any frame conversion, so the value is re-derivable.</param>
public sealed record PlanetaryReference(
    Graha Graha,
    double JulianDayUt,
    double TropicalLongitudeDegrees,
    double ToleranceArcseconds,
    string Source);
