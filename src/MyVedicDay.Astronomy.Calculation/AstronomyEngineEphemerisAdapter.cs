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

/// <summary>
/// Adapts the provider's <see cref="IAstronomyEngine"/> to <see cref="IEphemerisEngine"/> (FR-06, AST-12 #77).
/// </summary>
/// <remarks>
/// <para>Composition needs one production type satisfying the port, and the provider deliberately does not reference
/// this product assembly - the dependency runs the other way, from product to provider. So the product owns the
/// last thin step: this adapter holds an engine and forwards the two port methods to it, one engine call each.</para>
/// <para><b>The equatorial method refuses.</b> Parallax is not part of the rich result, and a zero would move
/// moonrise/moonset silently. Rather than approximate a validated quantity, this throws
/// <see cref="AstronomyNotSupportedException"/> naming the dependency: the provider's equatorial/parallax surface is
/// AST-05 work. The migration record states this gap explicitly.</para>
/// </remarks>
public sealed class ProviderEngineAdapter(IAstronomyEngine engine) : IEphemerisEngine
{
    private readonly IAstronomyEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public double GetTropicalLongitude(CelestialBody body, double julianDayUt)
    {
        var result = _engine.GetPositions(new JulianDayUt(julianDayUt), new AstronomySettings(), [body]);
        var position = result.Bodies.FirstOrDefault(p => p.Body == body)
            ?? throw new AstronomyNotSupportedException(
                $"Body '{body}' was requested but the engine returned no position for it.");
        return position.Ecliptic.LongitudeDegrees;
    }

    public AstronomyEngineEquatorial GetEquatorial(CelestialBody body, double julianDayUt)
    {
        var result = _engine.GetPositions(new JulianDayUt(julianDayUt), new AstronomySettings(), [body]);
        var position = result.Bodies.FirstOrDefault(p => p.Body == body)
            ?? throw new AstronomyNotSupportedException(
                $"Body '{body}' was requested but the engine returned no position for it.");

        // The rich result carries equatorial coordinates per body; horizontal parallax is not published there.
        _ = position;
        throw new AstronomyNotSupportedException(
            $"The SPICE provider cannot answer an equatorial request for '{body}' with the parallax the lunar port "
            + "needs. Refusing keeps moonrise/moonset from changing without an evidence trail; the equatorial and "
            + "parallax surface is AST-05 work (see astronomy/FR-06-PROVIDER-MIGRATION.md).");
    }

    public AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings) =>
        _engine.GetPositions(time, settings);

    public AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings,
        IReadOnlyCollection<CelestialBody> bodies) =>
        _engine.GetPositions(time, settings, bodies);

    public AstronomyCapabilityReport GetCapabilities() => _engine.GetCapabilities();
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
