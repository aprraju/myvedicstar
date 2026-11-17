namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// Provenance stamped on every engine result. Determinism (AC-7) requires that identical inputs produce identical
/// output, so every input that can change a number is represented here rather than read from ambient state.
/// </summary>
public sealed record AstronomyProvenance(
    string EngineVersion,
    string EphemerisSource,
    string KernelIdentifier,
    double DeltaTSeconds,
    NodeConvention NodeConvention,
    AyanamsaKind Ayanamsa,
    ZodiacConvention Zodiac);

/// <summary>One body's computed state at one instant.</summary>
public sealed record BodyPosition(
    CelestialBody Body,
    EclipticPoint Ecliptic,
    EquatorialPoint Equatorial,
    double DailySpeedDegrees,
    bool IsRetrograde);

/// <summary>Requested settings. Defaults are explicit so no caller depends on hidden ambient configuration.</summary>
public sealed record AstronomySettings
{
    public ZodiacConvention Zodiac { get; init; } = ZodiacConvention.Tropical;
    public AyanamsaKind Ayanamsa { get; init; } = AyanamsaKind.Lahiri;
    public NodeConvention NodeConvention { get; init; } = NodeConvention.Mean;
    /// <summary>When set, equatorial output is topocentric for this observer instead of geocentric.</summary>
    public ObserverLocation? Observer { get; init; }
}

/// <summary>An engine result plus the provenance needed to explain and cache it.</summary>
public sealed record AstronomyResult(
    JulianDayUt Time,
    AstronomySettings Settings,
    IReadOnlyList<BodyPosition> Bodies,
    AstronomyProvenance Provenance);

/// <summary>
/// The richer engine contract that new modules use directly. <c>IEphemeris</c> becomes a thin adapter over this:
/// two entry points, one implementation, never a second calculation path.
/// </summary>
public interface IAstronomyEngine
{
    /// <summary>Positions for every supported body at the given instant.</summary>
    AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings);

    /// <summary>Positions for a named subset. Requesting an unsupported body fails; it is never omitted silently.</summary>
    AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings, IReadOnlyCollection<CelestialBody> bodies);

    AstronomyCapabilityReport GetCapabilities();
}

/// <summary>A capability or input the engine refuses rather than approximates.</summary>
public sealed class AstronomyNotSupportedException(string message) : Exception(message);
