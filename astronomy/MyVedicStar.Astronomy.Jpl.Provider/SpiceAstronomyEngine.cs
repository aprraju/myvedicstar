using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Jpl.Provider;

/// <summary>
/// Astronomy engine backed by NAIF CSPICE and JPL DE440/DE441 kernels.
/// </summary>
/// <remarks>
/// This provider never fabricates a position. If the native toolkit or a kernel is not present, every calculation
/// raises <see cref="SpiceUnavailableException"/>: a dependency failure a host can surface as 503, not a quiet
/// degradation to a different numerical route (owner decision D1).
/// </remarks>
public sealed class SpiceAstronomyEngine : IAstronomyEngine
{
    private readonly SpiceConfiguration _configuration;
    private readonly Lazy<bool> _available;

    public SpiceAstronomyEngine(SpiceConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _available = new Lazy<bool>(ProbeNativeLibrary, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public SpiceConfiguration Configuration => _configuration;

    public bool IsNativeToolkitAvailable => _available.Value;

    public AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings) =>
        GetPositions(time, settings, AstronomyCapabilities.SupportedBodies);

    public AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings,
        IReadOnlyCollection<CelestialBody> bodies)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(bodies);
        if (!time.IsFinite) throw new ArgumentOutOfRangeException(nameof(time), "Julian Day must be finite.");
        if (bodies.Count == 0)
            throw new ArgumentException("At least one body must be requested.", nameof(bodies));
        foreach (var body in bodies)
            if (!AstronomyCapabilities.IsSupportedBody(body))
                throw new AstronomyNotSupportedException($"Body '{body}' is not supported by this engine.");

        EnsureAvailable();
        throw new NotImplementedException(
            "The rich IAstronomyEngine surface is completed in AST-04/AST-05. Use "
            + nameof(GetPlanetaryPositions) + " for the FR-02 planetary surface delivered by AST-02.");
    }

    /// <summary>
    /// Computes one body under an explicitly framed request (FR-02). Every frame axis comes from the request, never
    /// from ambient configuration.
    /// </summary>
    public PlanetaryPosition GetPlanetaryPosition(PlanetaryRequest request, JulianDayUt time)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!time.IsFinite) throw new ArgumentOutOfRangeException(nameof(time), "Julian Day must be finite.");
        EnsureAvailable();
        throw new NotImplementedException(
            "SPICE body-position numerics are completed in AST-04/AST-05 once the parity fixtures exist. This "
            + "engine refuses rather than returning a substitute calculation.");
    }

    public IReadOnlyList<PlanetaryPosition> GetPlanetaryPositions(IReadOnlyCollection<PlanetaryRequest> requests,
        JulianDayUt time)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return requests.Select(request => GetPlanetaryPosition(request, time)).ToArray();
    }

    public AstronomyCapabilityReport GetCapabilities() => AstronomyCapabilities.Report();

    /// <summary>
    /// Provenance for a result. The toolkit version is the one the image reports; when the host reports nothing this
    /// returns null rather than a constant, so the FR-08 inventory claim stays falsifiable (architect note 2).
    /// </summary>
    public AstronomyProvenance DescribeProvenance(AstronomySettings settings, double deltaTSeconds) => new(
        EngineVersion: "mvs-astronomy-spice-0.2.0",
        EphemerisSource: _configuration.ToolkitSource,
        KernelIdentifier: string.Join(';', _configuration.Kernels.Select(k => k.Path)),
        DeltaTSeconds: deltaTSeconds,
        NodeConvention: settings.NodeConvention,
        Ayanamsa: settings.Ayanamsa,
        Zodiac: settings.Zodiac);

    /// <summary>
    /// Verifies that the toolkit version actually present matches the version the FR-08 inventory claims. A mismatch
    /// is an error, not a warning: an unverifiable licence claim is exactly what FR-08 exists to prevent.
    /// </summary>
    public void VerifyToolkitMatchesInventory(string inventoryToolkitVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inventoryToolkitVersion);
        if (_configuration.ReportedToolkitVersion is null)
            throw new SpiceUnavailableException(
                "The host did not report a CSPICE toolkit version, so the FR-08 inventory claim cannot be verified. "
                + "Provide SpiceConfiguration.ReportedToolkitVersion from the image.");
        if (!string.Equals(_configuration.ReportedToolkitVersion, inventoryToolkitVersion, StringComparison.Ordinal))
            throw new SpiceUnavailableException(
                $"The image reports CSPICE '{_configuration.ReportedToolkitVersion}' but the FR-08 inventory claims "
                + $"'{inventoryToolkitVersion}'. Update the inventory or the image; they must agree.");
    }

    /// <summary>Refuses a date the mounted kernels do not cover, derived from configuration rather than a constant
    /// (architect note 1).</summary>
    public void EnsureDateIsCovered(DateOnly date)
    {
        if (!_configuration.CoversDate(date))
            throw new AstronomyNotSupportedException(
                $"No kernel declared in SpiceConfiguration covers {date:yyyy-MM-dd}; the supported span is derived "
                + "from the mounted kernels, not asserted.");
    }

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
            SpiceNative.unload_c(string.Empty);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    }
}
