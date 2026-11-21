using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// The privacy-safe telemetry surface for the astronomy engine (AST-10, api#75).
/// </summary>
/// <remarks>
/// <para>
/// Every recorded attribute is drawn from a closed set declared here. There is deliberately <b>no</b> attribute for a
/// caller-supplied identifier, a latitude/longitude, an elevation, a birth date, a name or a free-text reason: a
/// birth-profile field has nowhere to go even if a future caller tries to pass one. The log-scrubbing test asserts
/// that at runtime, and this type is what makes the assertion pass rather than a careful reviewer.
/// </para>
/// <para>
/// Numbers that ARE recorded are deliberately coarse: a date is recorded as the civil year only, because the exact
/// moment of a birth is itself identifying. Duration and kernel identifiers are engine facts, not person facts.
/// </para>
/// </remarks>
public static class AstronomyObservability
{
    /// <summary>Activity source name for calculation spans.</summary>
    public const string ActivitySourceName = "MyVedicStar.Astronomy";

    /// <summary>Meter name for the engine's counters and histograms.</summary>
    public const string MeterName = "MyVedicStar.Astronomy";

    /// <summary>The closed set of attribute names this surface may emit. Anything outside it is a defect.</summary>
    public static IReadOnlyList<string> AllowedAttributeNames { get; } =
    [
        "astronomy.engine.version",
        "astronomy.ephemeris.source",
        "astronomy.kernel.identifier",
        "astronomy.provider",
        "astronomy.body",
        "astronomy.year",
        "astronomy.outcome",
        "astronomy.cache.result",
        "astronomy.error.code",
        "astronomy.duration.ms",
        "astronomy.delta_t.seconds"
    ];

    /// <summary>The meter that owns the instruments below. Declared first: a static field initializer may only rely on
    /// fields declared before it, and an instrument created against a null meter fails type initialization.</summary>
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>The activity source spans are created from. One instance, so a listener sees one name.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    /// <summary>Duration of a calculation, in milliseconds, tagged by body and outcome only.</summary>
    public static readonly Histogram<double> CalculationDuration = Meter.CreateHistogram<double>(
        "astronomy.calculation.duration", unit: "ms",
        description: "Wall-clock duration of one astronomy calculation, by body and outcome.");

    /// <summary>Count of refused or failed calculations, by stable error code.</summary>
    public static readonly Counter<long> CalculationErrors = Meter.CreateCounter<long>(
        "astronomy.calculation.errors", unit: "{error}",
        description: "Calculations refused or failed, by stable error code. Never carries an input value.");

    /// <summary>Cache lookups, by hit/miss. Capacity and hit rate come from the cache's own statistics snapshot.</summary>
    public static readonly Counter<long> CacheLookups = Meter.CreateCounter<long>(
        "astronomy.cache.lookups", unit: "{lookup}",
        description: "Astronomy result-cache lookups, by result.");

    /// <summary>
    /// The civil year of an instant, the finest date granularity this surface records. A month or a day of a birth is
    /// a piece of the person, so only the year leaves the process.
    /// </summary>
    public static int CoarseYear(UtInstant time)
    {
        // Julian Day 1721426 = 1 January 1 (proleptic Gregorian, noon). Years of 365.25 days keep the value stable
        // across leap years; this is a privacy quantiser, never an input to a calculation.
        var year = (int)Math.Floor((time.JulianDay - 1721426.0) / 365.25) + 1;
        return year;
    }
}
