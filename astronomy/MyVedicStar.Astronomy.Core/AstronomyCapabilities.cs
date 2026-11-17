namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// The engine's capability boundary, published as data (same pattern as ADR-0016 section 3) so a caller cannot assume
/// support that does not exist. A body, node convention or date outside these bounds is refused explicitly and is
/// never approximated silently.
/// </summary>
public static class AstronomyCapabilities
{
    /// <summary>Bodies the engine computes. Keplerian/SPICE sources cover all of them; nothing here is a
    /// placeholder for a later implementation.</summary>
    public static readonly IReadOnlyList<CelestialBody> SupportedBodies =
    [
        CelestialBody.Sun, CelestialBody.Moon, CelestialBody.Mercury, CelestialBody.Venus, CelestialBody.Mars,
        CelestialBody.Jupiter, CelestialBody.Saturn, CelestialBody.Uranus, CelestialBody.Neptune, CelestialBody.Pluto,
        CelestialBody.MeanNode, CelestialBody.TrueNode
    ];

    /// <summary>Both node conventions are produced; the caller selects one and the choice is recorded in
    /// provenance.</summary>
    public static readonly IReadOnlyList<NodeConvention> SupportedNodeConventions =
        [NodeConvention.Mean, NodeConvention.True];

    public static readonly IReadOnlyList<AyanamsaKind> SupportedAyanamsas =
        [AyanamsaKind.Lahiri, AyanamsaKind.Raman, AyanamsaKind.Krishnamurti];

    /// <summary>Inclusive supported date range (Q4, approved). Outside it the engine refuses rather than
    /// extrapolating a series beyond its verified span.</summary>
    public static readonly DateOnly MinimumSupportedDate = new(1800, 1, 1);
    public static readonly DateOnly MaximumSupportedDate = new(2200, 12, 31);

    public static bool IsSupportedBody(CelestialBody body) => SupportedBodies.Contains(body);
    public static bool IsSupportedDate(DateOnly date) =>
        date >= MinimumSupportedDate && date <= MaximumSupportedDate;

    /// <summary>Machine-readable statement of what this engine does not do, so absence is never read as success.</summary>
    public static IReadOnlyList<string> UnsupportedCapabilities { get; } =
    [
        "Dates outside 1800-01-01..2200-12-31 are refused, not extrapolated.",
        "No satellite, asteroid or comet positions.",
        "No runtime kernel download: kernels are supplied by the host image or mounted read-only.",
        "Ayanamsa Raman and Krishnamurti are exposed by the engine API but are not product defaults until a separate owner decision.",
        "No astrology interpretation: the engine returns astronomy only, never product business rules."
    ];

    public static AstronomyCapabilityReport Report() => new(
        SupportedBodies, SupportedNodeConventions, SupportedAyanamsas,
        MinimumSupportedDate, MaximumSupportedDate, UnsupportedCapabilities);
}

/// <summary>Immutable capability payload returned by the engine.</summary>
public sealed record AstronomyCapabilityReport(
    IReadOnlyList<CelestialBody> SupportedBodies,
    IReadOnlyList<NodeConvention> SupportedNodeConventions,
    IReadOnlyList<AyanamsaKind> SupportedAyanamsas,
    DateOnly MinimumSupportedDate,
    DateOnly MaximumSupportedDate,
    IReadOnlyList<string> UnsupportedCapabilities);
