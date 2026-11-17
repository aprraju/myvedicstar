using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Jpl.Provider;

/// <summary>
/// How the SPICE runtime is provided, recorded in provenance so a result is always traceable to a toolkit version and
/// a kernel set (FR-08, AC-4).
/// </summary>
/// <remarks>
/// The supported date span is <b>derived from the kernels present</b>, not asserted in prose (architect note 1). A
/// claim like "supported from 1800-01-01" is only true if a kernel covering that date is actually mounted, so
/// <see cref="SupportedDateRange"/> computes it from the declared kernel coverage and a test proves the claim against
/// the configuration rather than against a comment.
/// </remarks>
public sealed record SpiceConfiguration
{
    /// <summary>Toolkit identifier as reported by the image, not a constant baked into the source. Left null when
    /// the host does not report one, which makes the FR-08 claim falsifiable rather than assumed (architect note 2).
    /// </summary>
    public string? ReportedToolkitVersion { get; init; }

    public required string ToolkitSource { get; init; }

    /// <summary>Kernel mount points, each with the span it is declared to cover.</summary>
    public required IReadOnlyList<SpiceKernel> Kernels { get; init; }

    /// <summary>The kernel set this epic ships. DE440 covers the modern era; DE441 extends the historical span. Both
    /// are declared here so the 1800-01-01 minimum is provable from configuration (architect note 1).</summary>
    public static SpiceConfiguration Default { get; } = new()
    {
        ReportedToolkitVersion = null,
        ToolkitSource = "NAIF (JPL), https://naif.jpl.nasa.gov/naif/toolkit.html - public domain",
        Kernels =
        [
            new SpiceKernel("/var/spice/de440.bsp", new DateOnly(1550, 1, 1), new DateOnly(2650, 1, 1)),
            // DE441's own span starts before year 1, which DateOnly cannot represent. The declared coverage is
            // therefore clamped to what the product promises; the engine never claims dates outside the policy
            // floor anyway, and CoversDate is what actually gates a request.
            // Coverage is clamped to the representable DateOnly range on both ends; the product floor is what
            // actually gates a request, and CoversDate is the check the engine uses.
            new SpiceKernel("/var/spice/de441.bsp", DateOnly.MinValue, DateOnly.MaxValue),
            new SpiceKernel("/var/spice/naif0012.tls", null, null),
            new SpiceKernel("/var/spice/pck00011.tpc", null, null)
        ]
    };

    /// <summary>The date range the mounted ephemeris kernels actually cover, or null when no ephemeris kernel is
    /// declared. Computed, never hardcoded.</summary>
    public (DateOnly From, DateOnly To)? SupportedDateRange
    {
        get
        {
            var spans = Kernels.Where(k => k.CoverageFrom is not null && k.CoverageTo is not null).ToArray();
            if (spans.Length == 0) return null;
            return (spans.Min(k => k.CoverageFrom!.Value), spans.Max(k => k.CoverageTo!.Value));
        }
    }

    /// <summary>True when a kernel covering this date is declared. Used by the capability check so an uncovered date
    /// is refused by configuration rather than attempted against a kernel that cannot answer.</summary>
    public bool CoversDate(DateOnly date) =>
        Kernels.Any(k => k.CoverageFrom is not null && k.CoverageTo is not null
                         && date >= k.CoverageFrom && date <= k.CoverageTo);
}

/// <summary>A mounted kernel and the span it is declared to cover. Null coverage marks a non-ephemeris kernel
/// (leap seconds, planetary constants).</summary>
public sealed record SpiceKernel(string Path, DateOnly? CoverageFrom, DateOnly? CoverageTo);

/// <summary>Raised when the native SPICE library or a kernel is unavailable. A dependency failure, never a silent
/// fallback to a different calculation route.</summary>
public sealed class SpiceUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
