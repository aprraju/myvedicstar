namespace MyVedicDay.Astronomy;

/// <summary>
/// Mean lunar node (Rahu) longitude of date, mean equinox, from Meeus, "Astronomical Algorithms" 2nd ed., chapter 47
/// (the Ω term of the lunar arguments): Ω = 125.0445479 − 1934.1362891·T + 0.0020754·T² + T³/467441 − T⁴/60616000.
/// </summary>
/// <remarks>
/// <para><b>What the mean node is, and is not.</b> The mean node is the ascending intersection of the Moon's mean orbital
/// plane with the ecliptic: a smoothly retrograding point, not the osculating node. Vyotisha's Rahu is conventionally
/// this mean value, which is why <c>KundaliCapabilities.LunarNodeConvention</c> already says "MeanNode".</para>
/// <para><b>Accuracy.</b> The series is a definition, not a fit to observations, so its agreement with an
/// independent ephemeris is limited by that ephemeris's own definition: Swiss Ephemeris and the Astronomical Almanac
/// differ from it by less than an arcsecond in the supported range, but no such comparison is checked in yet, so this
/// body carries the <c>unvalidated-reference</c> status at the product boundary.</para>
/// <para><b>Ketu</b> is the descending node, exactly 180° from Rahu. Stated as an identity in one place rather than
/// recomputed, so the two can never disagree.</para>
/// </remarks>
internal static class MeanLunarNode
{
    /// <summary>Mean node longitude referred to the mean ecliptic and equinox of date, in degrees [0, 360).</summary>
    public static double MeanEquinoxLongitude(double julianEphemerisDay)
    {
        var t = JulianDay.CenturiesSinceJ2000(julianEphemerisDay);
        var omega = 125.0445479
                    - (1934.1362891 * t)
                    + (0.0020754 * t * t)
                    + (t * t * t / 467441d)
                    - (t * t * t * t / 60616000d);
        return AngleMath.NormalizeDegrees(omega);
    }

    /// <summary>Ketu's longitude: the descending node, 180° from Rahu by definition.</summary>
    public static double KetuLongitude(double rahuLongitude) => AngleMath.NormalizeDegrees(rahuLongitude + 180d);
}
