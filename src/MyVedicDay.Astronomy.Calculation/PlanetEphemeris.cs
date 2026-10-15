namespace MyVedicDay.Astronomy;

/// <summary>The classical planets other than the Sun and the Moon.</summary>
public enum Planet
{
    Mercury = 1,
    Venus,
    Mars,
    Jupiter,
    Saturn,
}

/// <summary>
/// Geocentric positions of the planets. Adapter boundary, like <see cref="IEphemeris"/>: a higher-precision source (VSOP87
/// in full, Swiss Ephemeris) can replace the built-in orbital elements.
/// </summary>
/// <remarks>
/// Longitudes are referred to the J2000 ecliptic and equinox. Differences between bodies (elongations, conjunctions,
/// combustion) are unaffected by the frame; absolute sidereal positions additionally need precession to the date.
/// </remarks>
public interface IPlanetEphemeris
{
    /// <summary>Geocentric ecliptic longitude of <paramref name="planet"/> in degrees [0, 360).</summary>
    double GetGeocentricLongitude(Planet planet, double julianDayUt);

    /// <summary>
    /// Geocentric longitude of <paramref name="planet"/> minus that of the Sun, in degrees [−180, 180): positive east
    /// of the Sun (evening sky), negative west of it (morning sky).
    /// </summary>
    double GetElongation(Planet planet, double julianDayUt);

    /// <summary>Whether the planet's geocentric longitude is decreasing (apparent retrograde motion).</summary>
    bool IsRetrograde(Planet planet, double julianDayUt);
}

/// <summary>
/// Planet positions from the Keplerian elements and secular rates of Standish, "Keplerian Elements for Approximate
/// Positions of the Major Planets" (JPL), table 1, fitted to 1800–2050. Errors are about 20″ for Venus and the Earth,
/// 40″ for Mars and 400–600″ (0.1–0.2°) for Jupiter and Saturn, which is ample for combustion orbs of 8–17°.
/// </summary>
internal sealed class KeplerianPlanetEphemeris : IPlanetEphemeris
{
    private const double RetrogradeStepDays = 0.5;

    // a (AU), e, I (°), L (°), ϖ (°), Ω (°), each with its rate per Julian century.
    private static readonly OrbitalElements Earth = new(
        1.00000261, 0.00000562, 0.01671123, -0.00004392, -0.00001531, -0.01294668,
        100.46457166, 35999.37244981, 102.93768193, 0.32327364, 0.0, 0.0);

    private static readonly IReadOnlyDictionary<Planet, OrbitalElements> Elements = new Dictionary<Planet, OrbitalElements>
    {
        [Planet.Mercury] = new(
            0.38709927, 0.00000037, 0.20563593, 0.00001906, 7.00497902, -0.00594749,
            252.25032350, 149472.67411175, 77.45779628, 0.16047689, 48.33076593, -0.12534081),
        [Planet.Venus] = new(
            0.72333566, 0.00000390, 0.00677672, -0.00004107, 3.39467605, -0.00078890,
            181.97909950, 58517.81538729, 131.60246718, 0.00268329, 76.67984255, -0.27769418),
        [Planet.Mars] = new(
            1.52371034, 0.00001847, 0.09339410, 0.00007882, 1.84969142, -0.00813131,
            -4.55343205, 19140.30268499, -23.94362959, 0.44441088, 49.55953891, -0.29257343),
        [Planet.Jupiter] = new(
            5.20288700, -0.00011607, 0.04838624, -0.00013253, 1.30439695, -0.00183714,
            34.39644051, 3034.74612775, 14.72847983, 0.21252668, 100.47390909, 0.20469106),
        [Planet.Saturn] = new(
            9.53667594, -0.00125060, 0.05386179, -0.00050991, 2.48599187, 0.00193609,
            49.95424423, 1222.49362201, 92.59887831, -0.41897216, 113.66242448, -0.28867794),
    };

    public double GetGeocentricLongitude(Planet planet, double julianDayUt)
    {
        var (x, y) = Geocentric(planet, julianDayUt);
        return AngleMath.NormalizeDegrees(AngleMath.Atan2(y, x));
    }

    public double GetElongation(Planet planet, double julianDayUt)
    {
        var t = Centuries(julianDayUt);
        var earth = Earth.HeliocentricEcliptic(t);
        var sun = AngleMath.Atan2(-earth.Y, -earth.X);
        return AngleMath.NormalizeSignedDegrees(GetGeocentricLongitude(planet, julianDayUt) - sun);
    }

    public bool IsRetrograde(Planet planet, double julianDayUt) =>
        AngleMath.NormalizeSignedDegrees(
            GetGeocentricLongitude(planet, julianDayUt + RetrogradeStepDays) - GetGeocentricLongitude(planet, julianDayUt - RetrogradeStepDays)) < 0;

    private static (double X, double Y) Geocentric(Planet planet, double julianDayUt)
    {
        var t = Centuries(julianDayUt);
        var body = ElementsOf(planet).HeliocentricEcliptic(t);
        var earth = Earth.HeliocentricEcliptic(t);
        return (body.X - earth.X, body.Y - earth.Y);
    }

    private static OrbitalElements ElementsOf(Planet planet) =>
        Elements.TryGetValue(planet, out var elements)
            ? elements
            : throw new ArgumentOutOfRangeException(nameof(planet), planet, "Unknown planet.");

    // Terrestrial Time: the planets move slowly enough that ΔT matters only at the arc-second level, but it is cheap.
    private static double Centuries(double julianDayUt) => JulianDay.CenturiesSinceJ2000(DeltaT.ToJulianEphemerisDay(julianDayUt));

    /// <summary>Keplerian elements at J2000 with their rates per century (Standish table 1).</summary>
    private sealed record OrbitalElements(
        double SemiMajorAxis, double SemiMajorAxisRate,
        double Eccentricity, double EccentricityRate,
        double Inclination, double InclinationRate,
        double MeanLongitude, double MeanLongitudeRate,
        double PerihelionLongitude, double PerihelionLongitudeRate,
        double NodeLongitude, double NodeLongitudeRate)
    {
        /// <summary>Heliocentric ecliptic coordinates (AU) in the J2000 frame, by Standish's procedure.</summary>
        public (double X, double Y, double Z) HeliocentricEcliptic(double t)
        {
            var a = SemiMajorAxis + (SemiMajorAxisRate * t);
            var e = Eccentricity + (EccentricityRate * t);
            var inclination = Inclination + (InclinationRate * t);
            var meanLongitude = MeanLongitude + (MeanLongitudeRate * t);
            var perihelion = PerihelionLongitude + (PerihelionLongitudeRate * t);
            var node = NodeLongitude + (NodeLongitudeRate * t);

            var argumentOfPerihelion = perihelion - node;
            var meanAnomaly = AngleMath.NormalizeSignedDegrees(meanLongitude - perihelion);
            var eccentricAnomaly = SolveKepler(meanAnomaly, e);

            var xOrbit = a * (AngleMath.Cos(eccentricAnomaly) - e);
            var yOrbit = a * Math.Sqrt(1 - (e * e)) * AngleMath.Sin(eccentricAnomaly);

            var (cosW, sinW) = (AngleMath.Cos(argumentOfPerihelion), AngleMath.Sin(argumentOfPerihelion));
            var (cosO, sinO) = (AngleMath.Cos(node), AngleMath.Sin(node));
            var (cosI, sinI) = (AngleMath.Cos(inclination), AngleMath.Sin(inclination));

            return (
                (((cosW * cosO) - (sinW * sinO * cosI)) * xOrbit) + (((-sinW * cosO) - (cosW * sinO * cosI)) * yOrbit),
                (((cosW * sinO) + (sinW * cosO * cosI)) * xOrbit) + (((-sinW * sinO) + (cosW * cosO * cosI)) * yOrbit),
                (sinW * sinI * xOrbit) + (cosW * sinI * yOrbit));
        }

        /// <summary>Kepler's equation M = E − e·sin E by Newton iteration, in degrees.</summary>
        private static double SolveKepler(double meanAnomaly, double e)
        {
            var eStar = e * 180d / Math.PI;
            var eccentricAnomaly = meanAnomaly + (eStar * AngleMath.Sin(meanAnomaly));
            for (var iteration = 0; iteration < 20; iteration++)
            {
                var deltaM = meanAnomaly - (eccentricAnomaly - (eStar * AngleMath.Sin(eccentricAnomaly)));
                var deltaE = deltaM / (1 - (e * AngleMath.Cos(eccentricAnomaly)));
                eccentricAnomaly += deltaE;
                if (Math.Abs(deltaE) < 1e-8)
                {
                    break;
                }
            }

            return eccentricAnomaly;
        }
    }
}
