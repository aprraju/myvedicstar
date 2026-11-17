namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// The engine's capability boundary, published as data (same pattern as ADR-0016 section 3) so a caller cannot assume
/// support that does not exist. A body, node convention or date outside these bounds is refused explicitly and is
/// never approximated silently.
/// </summary>
/// <remarks>
/// The supported date span is a <b>policy floor</b>, not a hardware-independent fact: whether a date can actually be
/// computed depends on the kernels mounted in the image. The provider derives its real coverage from its kernel
/// configuration and refuses uncovered dates; this constant states the product promise so a test can prove the
/// shipped configuration satisfies it (architect note 1).
/// </remarks>
public static class AstronomyCapabilities
{
    public static readonly IReadOnlyList<CelestialBody> SupportedBodies =
    [
        CelestialBody.Sun, CelestialBody.Moon, CelestialBody.Mercury, CelestialBody.Venus, CelestialBody.Mars,
        CelestialBody.Jupiter, CelestialBody.Saturn, CelestialBody.Uranus, CelestialBody.Neptune, CelestialBody.Pluto,
        CelestialBody.MeanNode, CelestialBody.TrueNode
    ];

    public static readonly IReadOnlyList<NodeConvention> SupportedNodeConventions =
        [NodeConvention.Mean, NodeConvention.True];

    public static readonly IReadOnlyList<AyanamsaKind> SupportedAyanamsas =
        [AyanamsaKind.Lahiri, AyanamsaKind.Raman, AyanamsaKind.Krishnamurti];

    /// <summary>The product's promised date span. A test asserts the shipped kernel configuration actually covers
    /// it, so the promise cannot drift away from the configuration.</summary>
    public static readonly DateOnly MinimumSupportedDate = new(1800, 1, 1);
    public static readonly DateOnly MaximumSupportedDate = new(2200, 12, 31);

    public static bool IsSupportedBody(CelestialBody body) => SupportedBodies.Contains(body);
    public static bool IsSupportedDate(DateOnly date) =>
        date >= MinimumSupportedDate && date <= MaximumSupportedDate;

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
