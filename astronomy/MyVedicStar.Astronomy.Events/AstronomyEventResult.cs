using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Events;

/// <summary>Every event kind FR-04 names. Eclipses are their own kinds because their accuracy target (±2 s)
/// differs from the rise/set target (≤30 s).</summary>
public enum EventType
{
    Sunrise, Sunset, SolarNoon, Moonrise, Moonset,
    PlanetaryIngress, Station, Conjunction, RetrogradeTransition,
    SolarEclipseFirstContact, SolarEclipseSecondContact, SolarEclipseThirdContact, SolarEclipseFourthContact,
    LunarEclipseFirstContact, LunarEclipseSecondContact, LunarEclipseThirdContact, LunarEclipseFourthContact
}

/// <summary>Why an event did not occur. Explicit reasons, never a sentinel instant and never the next day's event
/// (FR-04 AC-3, architect edge-case policy B2).</summary>
public enum NonOccurrenceReason
{
    None,
    SunNeverSets,
    SunNeverRises,
    MoonNeverSets,
    MoonNeverRises,
    BodyNeverCrosses,
    EclipseDoesNotOccur,
    OutsideSearchWindow
}

/// <summary>Convergence diagnostics, so a wrong event can be attributed rather than merely re-run (AC-6 added by
/// the architect).</summary>
public sealed record ConvergenceDiagnostics(
    double ScanStepDays,
    double BracketWidthDays,
    int Iterations,
    double ToleranceDays,
    bool Converged)
{
    /// <summary>Whether the solver converged. A non-convergent bracket is an error result, never a fallback
    /// interpolation (AC-2).</summary>
    public bool IsUsable => Converged;
}

/// <summary>
/// An event result. The instant is always UTC-based (Julian Day UT); any local rendering happens at the edge, because
/// the engine never returns a local wall-clock string as its primary result (policy B3).
/// </summary>
public sealed record AstronomyEventResult(
    EventType Type,
    JulianDayUt? Instant,
    NonOccurrenceReason Reason,
    ConvergenceDiagnostics Diagnostics,
    EventProvenance Provenance)
{
    /// <summary>True only when the event genuinely occurred and the solver converged.</summary>
    public bool Occurred => Instant is not null && Reason == NonOccurrenceReason.None && Diagnostics.IsUsable;

    public static AstronomyEventResult DidNotOccur(EventType type, NonOccurrenceReason reason,
        ConvergenceDiagnostics diagnostics, EventProvenance provenance) =>
        new(type, null, reason, diagnostics, provenance);
}

/// <summary>
/// Provenance for an event: the instant's scale, the ephemeris/kernel version, and the tzdata version when a timezone
/// was resolved, because a tzdata update can legally change a historical event's local time (policy C).
/// </summary>
public sealed record EventProvenance(
    string EphemerisSource,
    string KernelIdentifier,
    string? TimezoneDataVersion,
    string? TimeZoneId,
    string HorizonAssumption);

/// <summary>The observer context for an event search. Elevation is optional and, when absent, the result must state
/// "sea-level horizon" rather than silently assuming one (policy B1).</summary>
public sealed record EventObserver(ObserverLocation Location, double? ElevationMetres, string? TimeZoneId)
{
    public string HorizonAssumption => ElevationMetres is null
        ? "sea-level horizon"
        : $"horizon at {ElevationMetres.Value:0.#} m elevation, standard 34' refraction";
}
