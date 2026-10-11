using NodaTime;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Conversions between NodaTime instants and Julian Day numbers.
/// </summary>
public static class JulianDay
{
    /// <summary>Julian Day of the J2000.0 epoch (2000-01-01 12:00).</summary>
    public const double J2000 = 2451545.0;

    private const double UnixEpoch = 2440587.5;
    private const double DaysPerJulianCentury = 36525d;

    public static double FromInstant(Instant instant) =>
        UnixEpoch + (instant.ToUnixTimeTicks() / (double)NodaConstants.TicksPerDay);

    public static Instant ToInstant(double julianDay) =>
        Instant.FromUnixTimeTicks((long)Math.Round((julianDay - UnixEpoch) * NodaConstants.TicksPerDay));

    public static double FromUtcMidnight(LocalDate date) =>
        FromInstant(date.AtMidnight().InUtc().ToInstant());

    public static double CenturiesSinceJ2000(double julianDay) => (julianDay - J2000) / DaysPerJulianCentury;
}
