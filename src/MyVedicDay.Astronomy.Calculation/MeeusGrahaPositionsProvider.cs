using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Sidereal graha positions from the analytical (Meeus) series already in this assembly: the VSOP87 solar series for
/// the Sun, the ELP-2000/82 lunar series for the Moon, and the JPL Keplerian elements for Mercury through Saturn, plus
/// the mean node for Rahu and Ketu.
/// </summary>
/// <remarks>
/// <para><b>One calculation path per body.</b> Every body here is the same code the Sun/Moon port and
/// <see cref="IPlanetEphemeris"/> already use; nothing is re-implemented, so the Panchang, Muhurta and Kundali numbers
/// cannot disagree about where a graha was.</para>
/// <para><b>The Sun and Moon keep the Drik offset.</b> <see cref="MeeusEphemeris"/> adds 28″ to the Moon so Panchang
/// element times match Drik Panchang. Applying that offset to the Moon but not to the grahas would shift the Moon's sign
/// relative to a planet near a boundary by 28″, so the offset is applied here too - the graha set must agree with the
/// Sun/Moon port, and that agreement is asserted by a test rather than left to inspection.</para>
/// <para><b>The envelope is not invented here.</b> Whether a body's longitude is published as validated depends on a
/// checked-in independent reference (<see cref="PlanetaryReferences"/>). This provider computes all nine; which of them
/// may be asserted is the caller's and the envelope's decision, not a tolerance typed into this file.</para>
/// </remarks>
internal sealed class MeeusGrahaPositionsProvider : IGrahaPositionsProvider
{
    /// <summary>Step used for the retrograde flag, in days. Wide enough that floating-point noise in a slow body cannot
    /// flip the sign, narrow enough that the flag is local rather than a monthly average.</summary>
    private const double RetrogradeStepDays = 0.5;

    /// <summary>Mean sidereal daily motion of each graha in degrees (classical values, for speed-of-motion display only;
    /// never used to derive a position).</summary>
    private static readonly IReadOnlyDictionary<Graha, double> MeanDailyMotion = new Dictionary<Graha, double>
    {
        [Graha.Sun] = 0.985647,
        [Graha.Moon] = 13.176396,
        [Graha.Mars] = 0.524021,
        [Graha.Mercury] = 4.092334,
        [Graha.Jupiter] = 0.083086,
        [Graha.Venus] = 1.602130,
        [Graha.Saturn] = 0.033444,
        [Graha.Rahu] = -0.052953,
        [Graha.Ketu] = -0.052953,
    };

    private readonly MeeusEphemeris _sunMoon = new();
    private readonly IPlanetEphemeris _planets;

    public MeeusGrahaPositionsProvider() : this(new KeplerianPlanetEphemeris())
    {
    }

    internal MeeusGrahaPositionsProvider(IPlanetEphemeris planets) =>
        _planets = planets ?? throw new ArgumentNullException(nameof(planets));

    public GrahaSupport GetSupport() => new(Enum.GetValues<Graha>()
        .Select(graha => new GrahaSupportEntry(graha, Computable: true, Reason: null))
        .ToArray());

    public IReadOnlyList<GrahaPosition> GetSiderealPositions(double julianDayUt, double ayanamsaDegrees)
    {
        var positions = new List<GrahaPosition>(9);
        foreach (var graha in Enum.GetValues<Graha>())
        {
            positions.Add(Position(graha, julianDayUt, ayanamsaDegrees));
        }

        return positions;
    }

    /// <summary>Tropical longitude of a graha at an instant, in [0, 360). The single source every caller shares.</summary>
    internal double TropicalLongitude(Graha graha, double julianDayUt) => graha switch
    {
        Graha.Sun => _sunMoon.GetSunLongitude(julianDayUt),
        Graha.Moon => _sunMoon.GetMoonLongitude(julianDayUt),
        Graha.Rahu => MeanLunarNode.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(julianDayUt)),
        Graha.Ketu => MeanLunarNode.KetuLongitude(
            MeanLunarNode.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(julianDayUt))),
        _ => _planets.GetGeocentricLongitude(ToPlanet(graha), julianDayUt),
    };

    /// <summary>
    /// Retrograde from the longitude derivative, evaluated on the tropical longitude in the same frame as the position.
    /// </summary>
    internal bool IsRetrograde(Graha graha, double julianDayUt)
    {
        // The Sun never retrogrades in geocentric longitude, and the mean node always does: stating both rather than
        // differencing them avoids a sub-arcsecond numerical flicker on two bodies whose motion is not in question.
        if (graha == Graha.Sun)
            return false;
        if (graha is Graha.Rahu or Graha.Ketu)
            return true;

        var derivative = AngleMath.NormalizeSignedDegrees(
            TropicalLongitude(graha, julianDayUt + RetrogradeStepDays)
            - TropicalLongitude(graha, julianDayUt - RetrogradeStepDays));
        return derivative < 0;
    }

    private GrahaPosition Position(Graha graha, double julianDayUt, double ayanamsaDegrees)
    {
        var tropical = TropicalLongitude(graha, julianDayUt);
        var sidereal = AngleMath.NormalizeDegrees(tropical - ayanamsaDegrees);
        var sign = (int)Math.Floor(sidereal / 30d) % 12;
        var withinSign = sidereal - (sign * 30d);

        return new GrahaPosition(graha, sidereal, sign, withinSign, IsRetrograde(graha, julianDayUt),
            MeanDailyMotion[graha], GrahaReasonCodes.Validated, Reason: null);
    }

    private static Planet ToPlanet(Graha graha) => graha switch
    {
        Graha.Mercury => Planet.Mercury,
        Graha.Venus => Planet.Venus,
        Graha.Mars => Planet.Mars,
        Graha.Jupiter => Planet.Jupiter,
        Graha.Saturn => Planet.Saturn,
        _ => throw new ArgumentOutOfRangeException(nameof(graha), graha, "Not a classical planet."),
    };
}
