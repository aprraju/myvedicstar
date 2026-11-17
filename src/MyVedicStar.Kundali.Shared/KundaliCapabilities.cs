namespace MyVedicStar.Kundali.Shared;

/// <summary>
/// The engine's published capability boundary (ADR-0016 section 3). This is authoritative data, not a comment:
/// the engine, Business layer, API and UI all read it, and a regression test asserts it verbatim, so removing or
/// editing an entry requires an ADR change. Callers must never interpret an absent capability as "supported but
/// empty"; absent means unsupported, and every unsupported capability is named here with its reason.
/// </summary>
public static class KundaliCapabilities
{
    /// <summary>Divisionals produced as definitive facts. D9 (Navamsa) is the smallest supported division; every
    /// supported divisional has a cut wide enough to absorb the Lagna accuracy budget in ADR-0016 section 4.</summary>
    public static readonly IReadOnlyList<int> SupportedDivisionalCharts = [1, 2, 3, 4, 7, 9, 10, 12, 16, 20, 24, 27, 30, 40, 45];

    /// <summary>Divisionals returned only provisional (with a boundary distance), never as definitive facts. D60
    /// (Shashtiamsa) is a half-degree cut and additionally needs an arc-second Lagna, which this provider set does
    /// not reach.</summary>
    public static readonly IReadOnlyList<int> ProvisionalDivisionalCharts = [60];

    /// <summary>Shadbala components that are computed. Chesta Bala is deliberately absent; see
    /// <see cref="UnsupportedShadbalaComponents"/>.</summary>
    public static readonly IReadOnlyList<string> SupportedShadbalaComponents =
        ["Sthana", "Dig", "Kala", "Naisargika", "Drik"];

    /// <summary>Chesta Bala needs a true apparent-velocity convention per graha, including the classical retrograde
    /// re-scaling, that this provider set does not define to the classical formula's satisfaction. Its absence makes
    /// a Shadbala total partial, never a classical required-rupas verdict.</summary>
    public static readonly IReadOnlyList<string> UnsupportedShadbalaComponents = ["Chesta"];

    /// <summary>Inclusive supported birth-date range. Instants outside it are refused, not extrapolated.</summary>
    public static readonly DateOnly MinimumSupportedBirthDate = new(1800, 1, 1);
    public static readonly DateOnly MaximumSupportedBirthDate = new(2200, 12, 31);

    /// <summary>Rahu/Ketu are the mean lunar node. True-node positions are not produced.</summary>
    public const string LunarNodeConvention = "MeanNode";

    /// <summary>Latitude convention for the ascendant: true sidereal (no refraction, no topocentric parallax).</summary>
    public const string AscendantConvention = "GeocentricTrueSidereal";

    public static bool IsSupportedDivisional(int divisions) => SupportedDivisionalCharts.Contains(divisions);
    public static bool IsProvisionalDivisional(int divisions) => ProvisionalDivisionalCharts.Contains(divisions);
    public static bool IsSupportedBirthDate(DateOnly dateOfBirth) =>
        dateOfBirth >= MinimumSupportedBirthDate && dateOfBirth <= MaximumSupportedBirthDate;

    /// <summary>The published boundary as returned by the API, so a client never has to hard-code it.</summary>
    public static KundaliCapabilityReport Report() => new(
        SupportedDivisionalCharts,
        ProvisionalDivisionalCharts,
        SupportedShadbalaComponents,
        UnsupportedShadbalaComponents,
        LunarNodeConvention,
        AscendantConvention,
        MinimumSupportedBirthDate,
        MaximumSupportedBirthDate,
        [
            "D60 and divisionals finer than D9 are returned provisional only, with a boundary distance; they are never asserted as definitive.",
            "Shadbala Chesta Bala is not computed; any Shadbala total is partial and is not a classical required-rupas verdict.",
            "Tight Nakshatra and pada boundaries are not asserted; a placement inside the accuracy budget is marked provisional.",
            "Rahu/Ketu use the mean lunar node; true-node positions are not produced.",
            "No arc-second astronomy, eclipse prediction, topocentric parallax or atmospheric refraction.",
            "Birth dates outside the supported range are refused rather than extrapolated."
        ]);
}

/// <summary>Immutable capability payload. Serialised as data so the UI can render "not supported, and why".</summary>
public sealed record KundaliCapabilityReport(
    IReadOnlyList<int> SupportedDivisionalCharts,
    IReadOnlyList<int> ProvisionalDivisionalCharts,
    IReadOnlyList<string> SupportedShadbalaComponents,
    IReadOnlyList<string> UnsupportedShadbalaComponents,
    string LunarNodeConvention,
    string AscendantConvention,
    DateOnly MinimumSupportedBirthDate,
    DateOnly MaximumSupportedBirthDate,
    IReadOnlyList<string> UnsupportedCapabilities);
