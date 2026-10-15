namespace MyVedicDay.Astronomy;

/// <summary>
/// Analytical ephemeris based on Meeus, "Astronomical Algorithms" (2nd ed.).
/// </summary>
/// <remarks>
/// The lunar longitude includes <see cref="DrikPanchangLunarOffsetDegrees"/>. The ELP-2000/82 series alone reproduces
/// the published (USNO) instants of the 2025 new and full moons within seconds, but Drik Panchang's Tithi, Karana and
/// Nakshatra times correspond to a Moon 28″ further ahead, i.e. about a minute earlier. With the offset, 24 of 26
/// published Drik tithi minutes for 2025 are reproduced exactly (none without it), so Panchang times agree with Drik
/// Panchang to the minute; lunar phases then come out about 55 seconds before the astronomical instant.
/// </remarks>
internal sealed class MeeusEphemeris : IEphemeris
{
    /// <summary>Added to the lunar longitude so Panchang element times follow Drik Panchang (28″).</summary>
    internal const double DrikPanchangLunarOffsetDegrees = 28d / 3600d;

    public double GetSunLongitude(double julianDayUt) =>
        SolarPosition.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(julianDayUt));

    public double GetMoonLongitude(double julianDayUt) =>
        AngleMath.NormalizeDegrees(LunarPosition.MeanEquinoxLongitude(DeltaT.ToJulianEphemerisDay(julianDayUt)) + DrikPanchangLunarOffsetDegrees);

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
