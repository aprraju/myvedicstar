using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Jpl.Provider;

/// <summary>
/// Astronomy engine backed by NAIF CSPICE and JPL DE440/DE441 kernels.
/// </summary>
/// <remarks>
/// This provider never fabricates a position. If the native toolkit or a kernel is not present, every calculation
/// raises <see cref="SpiceUnavailableException"/>: a dependency failure a host can surface as 503, not a quiet
/// degradation to a different numerical route (D1). The kernel-loading and ET-conversion plumbing is implemented; the
/// body-position mapping is completed in AST-02..AST-07, which are blocked until this story is reviewed and merged.
/// </remarks>
public sealed class SpiceAstronomyEngine : IAstronomyEngine
{
    private readonly SpiceConfiguration _configuration;
    private readonly Lazy<bool> _available;

    public SpiceAstronomyEngine(SpiceConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        // Availability is probed once per process: importing a missing shared library is expensive to repeat and its
        // state cannot change at run time because kernels and the toolkit are mounted, never downloaded.
        _available = new Lazy<bool>(ProbeNativeLibrary, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>True when the native toolkit can be loaded. A false value is a deployment fault to be reported, and
    /// every calculation then fails explicitly.</summary>
    public bool IsNativeToolkitAvailable => _available.Value;

    public AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings) =>
        GetPositions(time, settings, AstronomyCapabilities.SupportedBodies);

    public AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings,
        IReadOnlyCollection<CelestialBody> bodies)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(bodies);
        if (!time.IsFinite) throw new ArgumentOutOfRangeException(nameof(time), "Julian Day must be finite.");

        // Refuse rather than approximate: the capability boundary is enforced before any native call.
        foreach (var body in bodies)
            if (!AstronomyCapabilities.IsSupportedBody(body))
                throw new AstronomyNotSupportedException($"Body '{body}' is not supported by this engine.");
        if (bodies.Count == 0)
            throw new ArgumentException("At least one body must be requested.", nameof(bodies));

        EnsureAvailable();
        throw new NotImplementedException(
            "SPICE body-position mapping lands in AST-02..AST-07, which are blocked pending review of AST-01 (#66). "
            + "The native toolkit is available; no numerical route is substituted in the meantime.");
    }

    public AstronomyCapabilityReport GetCapabilities() => AstronomyCapabilities.Report();

    /// <summary>The provenance this provider will stamp once positions are computed.</summary>
    public AstronomyProvenance DescribeProvenance(AstronomySettings settings, double deltaTSeconds) => new(
        EngineVersion: "mvs-astronomy-spice-0.1.0",
        EphemerisSource: _configuration.ToolkitSource,
        KernelIdentifier: string.Join(';', _configuration.Kernels),
        DeltaTSeconds: deltaTSeconds,
        NodeConvention: settings.NodeConvention,
        Ayanamsa: settings.Ayanamsa,
        Zodiac: settings.Zodiac);

    private void EnsureAvailable()
    {
        if (!_available.Value)
            throw new SpiceUnavailableException(
                $"The native SPICE toolkit ('{SpiceNative.LibraryName}') could not be loaded. The container image must "
                + "supply it; this engine never downloads a toolkit or kernel at run time.");
    }

    private static bool ProbeNativeLibrary()
    {
        try
        {
            // A zero-length kernel name is rejected by CSPICE before any file access, so this detects the library
            // without requiring a kernel to be mounted.
            SpiceNative.unload_c(string.Empty);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    }
}
