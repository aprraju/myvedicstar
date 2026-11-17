using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Jpl.Provider;

/// <summary>The reference centre a position is measured from.</summary>
public enum PositionCenter { Geocentric, Heliocentric, Topocentric }

/// <summary>Apparent (light-time and aberration corrected) or geometric (instantaneous) coordinates.</summary>
public enum PositionConvention { Apparent, Geometric }

/// <summary>
/// A fully explicit planetary request. Every axis FR-02 names is a required argument: there is no defaulted
/// convention a consumer could inherit silently (FR-02 AC-3). Construct it with <see cref="Create"/> so the
/// compiler cannot omit a frame.
/// </summary>
public sealed record PlanetaryRequest
{
    private PlanetaryRequest() { }

    public required CelestialBody Body { get; init; }
    public required PositionCenter Center { get; init; }
    public required PositionConvention Convention { get; init; }
    public required ZodiacConvention Zodiac { get; init; }
    /// <summary>Required when <see cref="Zodiac"/> is Sidereal; ignored (and validated as absent) otherwise.</summary>
    public AyanamsaKind? Ayanamsa { get; init; }
    /// <summary>Required when <see cref="Center"/> is Topocentric.</summary>
    public ObserverLocation? Observer { get; init; }
    public required NodeConvention NodeConvention { get; init; }

    /// <summary>Builds a request and rejects incomplete or contradictory frame specifications immediately, so the
    /// error is a structured refusal at the call site rather than a surprising value later.</summary>
    public static PlanetaryRequest Create(CelestialBody body, PositionCenter center, PositionConvention convention,
        ZodiacConvention zodiac, NodeConvention nodeConvention, AyanamsaKind? ayanamsa = null,
        ObserverLocation? observer = null)
    {
        if (zodiac == ZodiacConvention.Sidereal && ayanamsa is null)
            throw new AstronomyNotSupportedException(
                "A sidereal request must name its ayanamsa; there is no implicit default.");
        if (zodiac == ZodiacConvention.Tropical && ayanamsa is not null)
            throw new AstronomyNotSupportedException(
                "A tropical request must not carry an ayanamsa; that combination is ambiguous.");
        if (center == PositionCenter.Topocentric && observer is null)
            throw new AstronomyNotSupportedException(
                "A topocentric request must supply an observer; there is no implicit location.");
        if (center != PositionCenter.Topocentric && observer is not null)
            throw new AstronomyNotSupportedException(
                "An observer was supplied for a non-topocentric request; that combination is ambiguous.");
        if (!AstronomyCapabilities.IsSupportedBody(body))
            throw new AstronomyNotSupportedException($"Body '{body}' is not supported by this engine.");

        return new PlanetaryRequest
        {
            Body = body, Center = center, Convention = convention, Zodiac = zodiac,
            Ayanamsa = ayanamsa, Observer = observer, NodeConvention = nodeConvention
        };
    }
}

/// <summary>One body's computed state, with every frame axis it was computed under echoed back so a consumer can
/// never mistake which convention produced a number.</summary>
public sealed record PlanetaryPosition(
    CelestialBody Body,
    PositionCenter Center,
    PositionConvention Convention,
    ZodiacConvention Zodiac,
    double LongitudeDegrees,
    double LatitudeDegrees,
    double DistanceAu,
    double SpeedDegreesPerDay,
    bool IsRetrograde,
    NodeConvention NodeConvention,
    AyanamsaKind? Ayanamsa);
