namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// The nine grahas of the classical Jyotisha set, in the order the product publishes them.
/// </summary>
/// <remarks>
/// This enum is the product's naming, not the engine's: Rahu and Ketu are the two ends of one mean lunar node, which the
/// engine exposes as <see cref="CelestialBody.MeanNode"/>. Keeping the mapping in one place stops "Ketu = MeanNode + 180"
/// from being re-derived at each call site, which is how a node convention drifts without anyone recording it.
/// </remarks>
public enum Graha
{
    Sun = 0,
    Moon,
    Mars,
    Mercury,
    Jupiter,
    Venus,
    Saturn,
    Rahu,
    Ketu,
}

/// <summary>Why a graha's longitude or state is withheld. Data, not a comment, so a caller can branch on it.</summary>
public static class GrahaReasonCodes
{
    /// <summary>
    /// The body is computable but has no independent reference value checked in, so its tolerance cannot be defended.
    /// Published as unsupported rather than shipped with a guessed budget.
    /// </summary>
    public const string UnvalidatedReference = "unvalidated-reference";

    /// <summary>The provider behind the port cannot produce this body at all.</summary>
    public const string NotImplemented = "not-implemented";

    /// <summary>A sidereal longitude cannot be formed without an ayanamsa, and none was resolvable.</summary>
    public const string AyanamsaUnavailable = "ayanamsa-unavailable";

    /// <summary>The graha is computable and its accuracy is bounded by a checked-in independent reference.</summary>
    public const string Validated = "validated";
}

/// <summary>
/// One body's output. <c>Reason</c> is null exactly when <c>SiderealLongitude</c> is present - so a caller never has to
/// guess whether an absent longitude means "not built" or "not proven".
/// </summary>
/// <param name="Graha">Which of the nine grahas this row describes.</param>
/// <param name="SiderealLongitude">Sidereal longitude of date in [0, 360), or null when withheld.</param>
/// <param name="Sign">Zero-based sign index, 0 = Aries, or null when withheld.</param>
/// <param name="DegreeWithinSign">Degrees elapsed within <paramref name="Sign"/>, or null when withheld.</param>
/// <param name="Retrograde">True when the body's longitude is decreasing. Present for the Sun and Moon (never retrograde)
/// and for the nodes (classically retrograde); the classical planets receive it with the same evidence as their
/// measured tolerance, because it is the first finite difference of the same quantity.</param>
/// <param name="MeanDailyMotionDegrees">Mean sidereal motion per day, so a consumer can state speed without a second
/// astronomy source.</param>
/// <param name="Status">One of <see cref="GrahaReasonCodes"/>: <c>validated</c>, or the reason the value is withheld.</param>
/// <param name="Reason">Explanatory detail for <paramref name="Status"/>. Null when the value is published.</param>
public sealed record GrahaPosition(
    Graha Graha,
    double? SiderealLongitude,
    int? Sign,
    double? DegreeWithinSign,
    bool? Retrograde,
    double? MeanDailyMotionDegrees,
    string Status,
    string? Reason);

/// <summary>The nine-graha result plus the conventions and envelope it was produced under.</summary>
public sealed record GrahaPositionsResult(
    IReadOnlyList<GrahaPosition> Grahas,
    GrahaConventions Conventions,
    GrahaAccuracyEnvelope Accuracy);

/// <summary>Everything needed to read the numbers: the frame, the ayanamsa, and how the nodes were derived.</summary>
public sealed record GrahaConventions(
    string Zodiac,
    string Ayanamsa,
    string AyanamsaDegrees,
    string NodeConvention,
    string EphemerisProvider,
    string HouseFrameNote);

/// <summary>
/// The published accuracy claim. <c>PerBodyArcseconds</c> holds a value only for a body with a checked-in independent
/// reference; a missing key means the body is not asserted, which is the honest state for this release.
/// </summary>
/// <param name="Basis">Where the tolerances came from, in full sentences, because "see fixtures" is not a defensible
/// claim.</param>
/// <param name="GlobalParityBudgetArcseconds">The provider-level parity budget used by the gate. This is a
/// provider-agreement figure, <b>not</b> an accuracy statement about any individual graha.</param>
public sealed record GrahaAccuracyEnvelope(
    IReadOnlyDictionary<string, double> PerBodyArcseconds,
    string GlobalParityBudgetArcseconds,
    string Basis);
