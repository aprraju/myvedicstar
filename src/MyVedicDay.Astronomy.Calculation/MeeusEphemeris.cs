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

    public LunarEquatorialCoordinates GetMoonEquatorial(double julianDayUt)
    {
        var julianEphemerisDay = DeltaT.ToJulianEphemerisDay(julianDayUt);
        var moon = LunarPosition.Position(julianEphemerisDay);
        var longitude = moon.Longitude + Nutation.InLongitude(julianEphemerisDay);
        var obliquity = Nutation.TrueObliquity(julianEphemerisDay);

        // Meeus 13.3 and 13.4: ecliptic to equatorial coordinates.
        var rightAscension = AngleMath.Atan2(
            (AngleMath.Sin(longitude) * AngleMath.Cos(obliquity)) - (AngleMath.Tan(moon.Latitude) * AngleMath.Sin(obliquity)),
            AngleMath.Cos(longitude));
        var declination = AngleMath.Asin(
            (AngleMath.Sin(moon.Latitude) * AngleMath.Cos(obliquity))
            + (AngleMath.Cos(moon.Latitude) * AngleMath.Sin(obliquity) * AngleMath.Sin(longitude)));

        return new LunarEquatorialCoordinates(
            AngleMath.NormalizeDegrees(rightAscension), declination, LunarPosition.HorizontalParallax(moon.DistanceKm));
    }
}
