using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Coordinates;

/// <summary>
/// Ayanamsa definitions. Each is stated as an explicit, independently documented formula so a value can be checked
/// against its source rather than trusted (FR-03 AC-3).
/// </summary>
/// <remarks>
/// Definitions used, with their provenance:
/// <list type="bullet">
/// <item><b>Lahiri</b> (Chitrapaksha): the ayanamsa adopted by the Indian Calendar Reform Committee (1956). Defined
/// here as the accumulated precession from a fixed zero point chosen so that the value is 23 degrees 15 arcminutes
/// 00.658 arcseconds at 1956-03-21, which is the published Lahiri value for that epoch.</item>
/// <item><b>Raman</b>: B. V. Raman's definition, differing from Lahiri by a constant offset of 1 degree 23 arcminutes
/// 15 arcseconds (the published difference at the same epoch).</item>
/// <item><b>Krishnamurti</b> (KP): K. S. Krishnamurti's definition, differing from Lahiri by a constant offset of
/// 0 degrees 6 arcminutes 5.5 arcseconds.</item>
/// </list>
/// <para>Each is a required, explicit setting: <see cref="For"/> takes the kind and never defaults. The engine
/// refuses an undefined kind rather than falling back.</para>
/// </remarks>
public static class Ayanamsa
{
    /// <summary>Lahiri ayanamsa at the 1956 definition epoch, in degrees (23d15'00.658").</summary>
    public const double Lahiri1956Degrees = 23.0 + (15.0 / 60.0) + (0.658 / 3600.0);

    /// <summary>Raman minus Lahiri, in degrees (1d23'15").</summary>
    public const double RamanOffsetFromLahiriDegrees = 1.0 + (23.0 / 60.0) + (15.0 / 3600.0);

    /// <summary>Krishnamurti minus Lahiri, in degrees (0d6'5.5").</summary>
    public const double KrishnamurtiOffsetFromLahiriDegrees = (6.0 / 60.0) + (5.5 / 3600.0);

    /// <summary>Julian Day (TT) of the Lahiri definition epoch, 1956-03-21 00:00 TT.</summary>
    public static readonly double LahiriEpochJulianDayTt = 2435553.5;

    /// <summary>Ayanamsa in degrees for a Julian Day TT under the named definition. Throws for an undefined kind
    /// rather than inventing a value.</summary>
    public static double For(AyanamsaKind kind, double julianDayTt)
    {
        var lahiri = LahiriDegrees(julianDayTt);
        return kind switch
        {
            AyanamsaKind.Lahiri => lahiri,
            AyanamsaKind.Raman => lahiri + RamanOffsetFromLahiriDegrees,
            AyanamsaKind.Krishnamurti => lahiri + KrishnamurtiOffsetFromLahiriDegrees,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined ayanamsa.")
        };
    }

    /// <summary>Lahiri ayanamsa: the precession accumulated between the definition epoch and the requested instant,
    /// added to the published 1956 value.</summary>
    public static double LahiriDegrees(double julianDayTt) =>
        Lahiri1956Degrees + PrecessionNutation.PrecessionInLongitudeDegrees(julianDayTt)
                            - PrecessionNutation.PrecessionInLongitudeDegrees(LahiriEpochJulianDayTt);

    /// <summary>Converts a tropical longitude of date to sidereal under the named ayanamsa.</summary>
    public static double ToSidereal(double tropicalLongitudeOfDateDegrees, AyanamsaKind kind, double julianDayTt) =>
        AngleMath.Normalize(tropicalLongitudeOfDateDegrees - For(kind, julianDayTt));
}
