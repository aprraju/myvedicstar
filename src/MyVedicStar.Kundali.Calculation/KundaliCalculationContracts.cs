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
/// <param name="GrahaPositions">The publishable nine-graha section. Null only when the birth time is not exact, in which
/// case <see cref="Gaps"/> carries the matching reason. Present-and-withheld grahas are described inside it with their own
/// reason codes, so a partial section is still explained.</param>
public sealed record KundaliCalculationResult(
    KundaliCalculationConventions Conventions,
    double? SiderealSunLongitude,
    double? SiderealMoonLongitude,
    int? SunSign,
    int? MoonSign,
    IReadOnlyList<KundaliVimshottariPeriod>? VimshottariMahadashas,
    IReadOnlyList<KundaliYoginiPeriod>? YoginiPeriods,
    IReadOnlyList<KundaliCharaPeriod>? CharaSignPeriods,
    IReadOnlyList<KundaliCalculationGap> Gaps,
    KundaliGrahaPositions? GrahaPositions = null);

/// <summary>
/// One Vimshottari mahadasha period. Both ends are UTC instants; presenting them in the profile's zone is the
/// caller's concern.
/// </summary>
/// <param name="IsRemainder">True for the birth period, which began before birth and so is not a full term.</param>
/// <summary>One Yogini period. <c>Ruler</c> is the planet; the period itself is named separately.</summary>
public sealed record KundaliYoginiPeriod(string Name, string Ruler, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, bool IsRemainder);

/// <summary>One equal-duration sign period from the Chara progression. Not a validated Chara dasha period.</summary>
public sealed record KundaliCharaPeriod(string Sign, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
public sealed record KundaliVimshottariPeriod(string Lord, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    bool IsRemainder);


/// <summary>
/// Reason codes for graha positions specifically. Deliberately narrower than <see cref="KundaliCalculationReasons"/> so a
/// consumer of the graha section is told <b>why</b> a graha is absent rather than only that it is.
/// </summary>
public static class KundaliGrahaReasons
{
    /// <summary>Computed, but no independent reference exists to bound its accuracy. Withheld rather than shipped with a
    /// guessed tolerance.</summary>
    public const string UnvalidatedReference = "unvalidated-reference";

    /// <summary>The provider behind the port cannot produce this graha.</summary>
    public const string NotImplemented = "not-implemented";

    /// <summary>No ayanamsa was resolvable, so no sidereal longitude can be formed.</summary>
    public const string AyanamsaUnavailable = "ayanamsa-unavailable";

    /// <summary>The birth time is not exact, so the positions are indeterminate.</summary>
    public const string RequiresExactBirthTime = "requires-exact-birth-time";
}

/// <summary>One graha's publishable position. <c>SiderealLongitude</c> is null exactly when <c>Reason</c> is non-null.</summary>
public sealed record KundaliGrahaPosition(
    string Graha,
    double? SiderealLongitude,
    string? Sign,
    decimal? DegreeWithinSign,
    bool? Retrograde,
    double? MeanDailyMotionDegrees,
    string? Reason);

/// <summary>The nine-graha section, with the accuracy envelope it was produced under.</summary>
public sealed record KundaliGrahaPositions(
    IReadOnlyList<KundaliGrahaPosition> Grahas,
    string NodeConvention,
    string AccuracySummary)
{
    /// <summary>Grahas with a published longitude. A caller counting the chart's placements reads this, not the list.</summary>
    public IReadOnlyList<KundaliGrahaPosition> Published =>
        Grahas.Where(graha => graha.SiderealLongitude.HasValue).ToArray();

    /// <summary>Grahas withheld, with reasons, so an absence is never silent.</summary>
    public IReadOnlyList<KundaliGrahaPosition> Withheld =>
        Grahas.Where(graha => !graha.SiderealLongitude.HasValue).ToArray();
}
