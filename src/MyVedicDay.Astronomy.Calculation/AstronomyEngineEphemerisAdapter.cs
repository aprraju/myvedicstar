using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>The richer engine surface the adapter delegates to. Implemented by the SPICE provider in
/// MyVedicStar.Astronomy.Jpl.Provider; declared here so the product adapter depends on an abstraction.</summary>
public interface IEphemerisEngine
{
    /// <summary>Tropical longitude of a body at a Julian Day UT, mean equinox of date, in degrees.</summary>
    double GetTropicalLongitude(CelestialBody body, double julianDayUt);

    /// <summary>Apparent equatorial position of a body at a Julian Day UT.</summary>
    AstronomyEngineEquatorial GetEquatorial(CelestialBody body, double julianDayUt);
}

/// <summary>Equatorial position plus the parallax the port's lunar contract needs.</summary>
public readonly record struct AstronomyEngineEquatorial(double RightAscension, double Declination,
    double HorizontalParallax = 0.0);

/// <summary>
/// Thin adapter presenting the rich engine through the existing <see cref="IEphemeris"/> port (FR-06).
/// </summary>
/// <remarks>
/// This type contains <b>no astronomy</b>. Every value comes from <see cref="IEphemerisEngine"/>; the adapter only
/// maps the four port methods onto it. That is the property the AST-06 test asserts: if a caller replaces the engine,
/// all four port methods must change with it, which is only true if there is one calculation path.
/// </remarks>
internal sealed class AstronomyEngineEphemerisAdapter(IEphemerisEngine engine) : IEphemeris
{
    private readonly IEphemerisEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public double GetSunLongitude(double julianDayUt) =>
        _engine.GetTropicalLongitude(CelestialBody.Sun, julianDayUt);

    public double GetMoonLongitude(double julianDayUt) =>
        _engine.GetTropicalLongitude(CelestialBody.Moon, julianDayUt);

    public EquatorialCoordinates GetSunEquatorial(double julianDayUt)
    {
        var position = _engine.GetEquatorial(CelestialBody.Sun, julianDayUt);
        return new EquatorialCoordinates(position.RightAscension, position.Declination);
    }

    public LunarEquatorialCoordinates GetMoonEquatorial(double julianDayUt)
    {
        var position = _engine.GetEquatorial(CelestialBody.Moon, julianDayUt);
        return new LunarEquatorialCoordinates(position.RightAscension, position.Declination,
            position.HorizontalParallax);
    }
}
