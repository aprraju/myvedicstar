using NodaTime;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Sunrise and sunset for a local calendar date at a location.
/// </summary>
public interface ISunEventCalculator
{
    /// <exception cref="SunEventNotFoundException">The sun does not rise on the date at the location.</exception>
    Instant CalculateSunrise(LocalDate date, GeoCoordinate location, DateTimeZone timeZone, SunriseDefinition definition);

    /// <exception cref="SunEventNotFoundException">The sun does not set on the date at the location.</exception>
    Instant CalculateSunset(LocalDate date, GeoCoordinate location, DateTimeZone timeZone, SunriseDefinition definition);
}

/// <summary>
/// Calculates sunrise and sunset for a local calendar date by iterating on the solar hour angle.
/// </summary>
internal sealed class SunEventCalculator(IEphemeris ephemeris) : ISunEventCalculator
{
    // Rate at which the sun's hour angle increases (one solar day per 360°).
    private const double HourAngleDegreesPerDay = 360d;
    private const double ToleranceDays = 0.1 / 86400d;
    private const int MaxIterations = 20;

    public Instant CalculateSunrise(LocalDate date, GeoCoordinate location, DateTimeZone timeZone, SunriseDefinition definition) =>
        CalculateEvent(date, location, timeZone, GetHorizonAltitude(definition), rising: true);

    public Instant CalculateSunset(LocalDate date, GeoCoordinate location, DateTimeZone timeZone, SunriseDefinition definition) =>
        CalculateEvent(date, location, timeZone, GetHorizonAltitude(definition), rising: false);

    private static double GetHorizonAltitude(SunriseDefinition definition) => definition switch
    {
        SunriseDefinition.UpperLimbWithRefraction => -0.8333,
        SunriseDefinition.DiscCentreWithRefraction => -0.5667,
        SunriseDefinition.DiscCentreWithoutRefraction => 0d,
        _ => throw new ArgumentOutOfRangeException(nameof(definition), definition, "Unknown sunrise definition."),
    };

    private Instant CalculateEvent(LocalDate date, GeoCoordinate location, DateTimeZone timeZone, double altitude, bool rising)
    {
        var eventName = rising ? "sunrise" : "sunset";

        // Start from local apparent noon (by longitude) ± 6 hours; the hour-angle iteration converges on the nearest event.
        var localNoon = JulianDay.FromUtcMidnight(date) + 0.5 - (location.Longitude / 360d);
        var julianDay = Refine(localNoon + (rising ? -0.25 : 0.25), location, altitude, rising)
            ?? throw new SunEventNotFoundException(date, eventName);

        // Civil time zones can be offset from solar time far enough that the event lands on the adjacent civil date.
        var eventDate = JulianDay.ToInstant(julianDay).InZone(timeZone).Date;
        if (eventDate != date)
        {
            var dayShift = Period.Between(eventDate, date, PeriodUnits.Days).Days;
            julianDay = Refine(julianDay + dayShift, location, altitude, rising)
                ?? throw new SunEventNotFoundException(date, eventName);

            if (JulianDay.ToInstant(julianDay).InZone(timeZone).Date != date)
            {
                throw new SunEventNotFoundException(date, eventName);
            }
        }

        return JulianDay.ToInstant(julianDay);
    }

    private double? Refine(double julianDay, GeoCoordinate location, double altitude, bool rising)
    {
        var sinLatitude = AngleMath.Sin(location.Latitude);
        var cosLatitude = AngleMath.Cos(location.Latitude);
        var sinAltitude = AngleMath.Sin(altitude);

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var sun = ephemeris.GetSunEquatorial(julianDay);
            var cosEventHourAngle = (sinAltitude - (sinLatitude * AngleMath.Sin(sun.Declination)))
                / (cosLatitude * AngleMath.Cos(sun.Declination));

            if (cosEventHourAngle is < -1d or > 1d)
            {
                return null;
            }

            var eventHourAngle = AngleMath.Acos(cosEventHourAngle);
            var targetHourAngle = rising ? -eventHourAngle : eventHourAngle;
            var currentHourAngle = SiderealTime.GreenwichMeanDegrees(julianDay) + location.Longitude - sun.RightAscension;

            var correction = AngleMath.NormalizeSignedDegrees(targetHourAngle - currentHourAngle) / HourAngleDegreesPerDay;
            julianDay += correction;

            if (Math.Abs(correction) < ToleranceDays)
            {
                return julianDay;
            }
        }

        return null;
    }
}
