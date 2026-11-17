using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Jpl.Provider;

/// <summary>
/// How the SPICE runtime is provided, recorded in provenance so a result is always traceable to a toolkit version and
/// a kernel set (FR-08, AC-4).
/// </summary>
/// <remarks>
/// <para>The supported date span is <b>derived from the kernels present</b>, not asserted in prose. A claim like
/// "supported from 1800-01-01" is only true if a kernel covering that date is actually mounted.</para>
/// <para><b>One gate, not two.</b> <see cref="CoversDate"/> and <see cref="AstronomyCapabilities.IsSupportedDate"/>
/// previously answered independently, and disagreed: a kernel declared as <c>DateOnly.Min..Max</c> accepted year 5
/// while the capability floor rejected anything before 1800. Whichever gate a caller checked, the weaker one won.
/// There is now exactly one decision - <see cref="IsServiceable"/> - and both public gates delegate to it, so they
/// cannot drift apart again (issue #83).</para>
/// </remarks>
public sealed record SpiceConfiguration
{
    /// <summary>Toolkit identifier as reported by the image, not a constant baked into the source. Left null when
    /// the host does not report one, which makes the FR-08 claim falsifiable rather than assumed.</summary>
    public string? ReportedToolkitVersion { get; init; }

    public required string ToolkitSource { get; init; }

    /// <summary>Kernel mount points, each with the real span it covers. Null coverage marks a non-ephemeris kernel
    /// (leap seconds, planetary constants).</summary>
    public required IReadOnlyList<SpiceKernel> Kernels { get; init; }

    /// <summary>The kernel set this epic ships, with each kernel's <b>real</b> documented span.</summary>
    /// <remarks>
    /// DE440 covers 1550-01-01..2650-01-01. DE441 covers roughly -13200..17191, which <see cref="DateOnly"/> cannot
    /// represent; its declared span is therefore narrowed to the representable intersection
    /// (DateOnly.MinValue..DateOnly.MaxValue) rather than being invented. The product floor then narrows it further.
    /// The span was <b>narrowed to reality</b>; it was never widened to match a convenient declaration.
    /// </remarks>
    public static SpiceConfiguration Default { get; } = new()
    {
        ReportedToolkitVersion = null,
        ToolkitSource = "NAIF (JPL), https://naif.jpl.nasa.gov/naif/toolkit.html - public domain",
        Kernels =
        [
            new SpiceKernel("/var/spice/de440.bsp", new DateOnly(1550, 1, 1), new DateOnly(2650, 1, 1)),
            new SpiceKernel("/var/spice/de441.bsp", DateOnly.MinValue, DateOnly.MaxValue),
            new SpiceKernel("/var/spice/naif0012.tls", null, null),
            new SpiceKernel("/var/spice/pck00011.tpc", null, null)
        ]
    };

    /// <summary>The date range the mounted ephemeris kernels cover, intersected with the product promise. Null when
    /// no ephemeris kernel is declared. This is the single source both gates read.</summary>
    public (DateOnly From, DateOnly To)? SupportedDateRange => KernelDateRange() is { } kernel
        ? (Max(kernel.From, AstronomyCapabilities.MinimumSupportedDate),
           Min(kernel.To, AstronomyCapabilities.MaximumSupportedDate))
        : null;

    /// <summary>The raw span the declared kernels alone cover, before the product floor is applied. Exposed so a test
    /// can prove the kernels are at least as wide as the promise.</summary>
    public (DateOnly From, DateOnly To)? KernelDateRange()
    {
        var spans = Kernels.Where(k => k.CoverageFrom is not null && k.CoverageTo is not null).ToArray();
        if (spans.Length == 0) return null;
        return (spans.Min(k => k.CoverageFrom!.Value), spans.Max(k => k.CoverageTo!.Value));
    }

    /// <summary>
    /// The single serviceability decision: a date is serviceable only when a declared kernel covers it <b>and</b> the
    /// product supports it. Both public gates delegate here, so no caller can get a weaker answer by choosing a
    /// different check (issue #83).
    /// </summary>
    public bool IsServiceable(DateOnly date) =>
        AstronomyCapabilities.IsSupportedDate(date) && CoversDateByKernel(date);

    /// <summary>True when a declared kernel covers this date, independent of the product floor. Public because a
    /// diagnostic or deployment check legitimately needs the narrowing split; a serviceability decision must use
    /// <see cref="IsServiceable"/>.</summary>
    public bool CoversDateByKernel(DateOnly date) =>
        Kernels.Any(k => k.CoverageFrom is not null && k.CoverageTo is not null
                         && date >= k.CoverageFrom && date <= k.CoverageTo);

    /// <summary>Kernel-only coverage. Retained for callers that mean exactly that; deliberately delegates to
    /// <see cref="CoversDateByKernel"/> so the two names cannot diverge.</summary>
    public bool CoversDate(DateOnly date) => CoversDateByKernel(date);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}

/// <summary>A mounted kernel and the span it is declared to cover. Null coverage marks a non-ephemeris kernel
/// (leap seconds, planetary constants).</summary>
public sealed record SpiceKernel(string Path, DateOnly? CoverageFrom, DateOnly? CoverageTo);

/// <summary>Raised when the native SPICE library or a kernel is unavailable. A dependency failure, never a silent
/// fallback to a different calculation route.</summary>
public sealed class SpiceUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
