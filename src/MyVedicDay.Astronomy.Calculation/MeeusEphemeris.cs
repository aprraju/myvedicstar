namespace MyVedicDay.Astronomy;

/// <summary>
/// Analytical ephemeris based on Meeus, "Astronomical Algorithms" (2nd ed.).
/// </summary>
internal sealed class MeeusEphemeris : IEphemeris
{
    public double GetSunLongitude(double julianDayUt) =>
        SolarPosition.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(julianDayUt));

    public double GetMoonLongitude(double julianDayUt) =>
        LunarPosition.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(julianDayUt));

    public EquatorialCoordinates GetSunEquatorial(double julianDayUt) =>
        SolarPosition.ApparentEquatorial(DeltaT.ToJulianEphemerisDay(julianDayUt));
}
