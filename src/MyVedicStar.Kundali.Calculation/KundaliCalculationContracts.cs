namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Reason codes for values the engine cannot return. Deliberately capability-specific rather than a single generic
/// code, because the three states mean genuinely different things to a caller and to whoever fixes them next.
/// </summary>
public static class KundaliCalculationReasons
{
    /// <summary>The birth time is not exact, so this time-dependent value is indeterminate. Not a gap in the
    /// engine - a fact about the input.</summary>
    public const string RequiresExactBirthTime = "requires-exact-birth-time";

    /// <summary>The engine can compute this but has no independent reference to validate it against, so the value is
    /// withheld rather than published unproven. This is a <b>validation</b> task, not a computation task.</summary>
    public const string UnvalidatedReference = "unvalidated-reference";

    /// <summary>Not built yet.</summary>
    public const string NotImplemented = "not-implemented";
}

/// <summary>One withheld value and why.</summary>
public sealed record KundaliCalculationGap(string Path, string Reason);

/// <summary>
/// Declared accuracy envelope of the provider behind the calculation. Published with every result so a consumer
/// never has to know which provider produced a number - it is told.
/// </summary>
public sealed record KundaliAccuracyEnvelope(
    string EphemerisProvider,
    string TropicalLongitudeToleranceArcseconds,
    string Basis);

/// <summary>Conventions under which the results were produced. Held once, at the root of the result.</summary>
public sealed record KundaliCalculationConventions(
    string Ayanamsa,
    string Zodiac,
    string EphemerisProvider,
    KundaliAccuracyEnvelope Accuracy);

/// <summary>Inputs the engine accepts. Plain values only - the engine never resolves a profile or settings itself.</summary>
public sealed record KundaliCalculationRequest(
    DateOnly BirthDate,
    MyVedicStar.VedicProfiles.Shared.BirthTimeMode BirthTimeMode,
    TimeOnly? BirthTime,
    decimal Latitude,
    decimal Longitude,
    string TimezoneId,
    string Ayanamsa);

/// <summary>
/// Engine output. Every nullable is explained by an entry in <see cref="Gaps"/>; a caller can therefore never see an
/// absent value without also being told which of the three reasons applies.
/// </summary>
public sealed record KundaliCalculationResult(
    KundaliCalculationConventions Conventions,
    double? SiderealSunLongitude,
    double? SiderealMoonLongitude,
    int? SunSign,
    int? MoonSign,
    IReadOnlyList<KundaliCalculationGap> Gaps);

