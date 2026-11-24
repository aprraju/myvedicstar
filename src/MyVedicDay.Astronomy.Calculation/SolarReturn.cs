namespace MyVedicDay.Astronomy;

/// <summary>
/// The solar-return instant: the moment the Sun's sidereal longitude returns to its natal value (Varshaphal, api#210).
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>computation</b>, not a convention. It answers exactly one question - "at what instant does the Sun's
/// sidereal longitude equal this value again, in this year" - and it takes the ayanamsa as a parameter rather than
/// choosing one. Which ayanamsa a Varshaphal chart should use, and whether the year boundary is the return itself or a
/// civil-date rule, are doctrinal decisions this type deliberately does not make: picking one here would encode an
/// interpretation into what is only arithmetic, and the caller could no longer tell the two apart.
/// </para>
/// <para>
/// The search reuses <see cref="AngularCrossingFinder"/>, whose contract is a monotonically increasing angle. The Sun's
/// sidereal longitude is monotonic on the scale of a year (it advances by very nearly 360 degrees between successive
/// returns), so the crossing it is asked to find is unique within the bracket, and the bracket starts a few days before
/// the anniversary so the solution cannot be the previous year's return.
/// </para>
/// </remarks>
public static class SolarReturn
{
    /// <summary>
    /// Julian Day (UT) of the instant the Sun returns to <paramref name="natalSiderealLongitudeDegrees"/> in
    /// <paramref name="year"/>, or null when no crossing exists in the searched window.
    /// </summary>
    /// <param name="ephemeris">Source of tropical solar longitude. Sidereal is derived by subtracting the ayanamsa,
    /// which is the same relationship the rest of the engine uses - there is one conversion, not a second one here.</param>
    /// <param name="ayanamsa">Ayanamsa to subtract. Supplied by the caller: this method does not choose a school.</param>
    /// <param name="natalSiderealLongitudeDegrees">The natal sidereal solar longitude, degrees [0, 360).</param>
    /// <param name="year">Civil year in which the return is sought.</param>
    /// <remarks>
    /// The bracket begins on 1 January of <paramref name="year"/> minus a few days: the earliest a sidereal solar return
    /// can fall is mid-January, so the window always contains the year's own crossing and never the previous one.
    /// </remarks>
    public static double? Find(double natalSiderealLongitudeDegrees, int year, IEphemeris ephemeris, IAyanamsa ayanamsa)
    {
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(ayanamsa);
        if (natalSiderealLongitudeDegrees is < 0 or >= 360)
            throw new ArgumentOutOfRangeException(nameof(natalSiderealLongitudeDegrees),
                "Sidereal longitude must be in [0, 360).");

        var siderealAt = SiderealSun(ephemeris, ayanamsa);
        var yearStart = JulianDay.FromUtcMidnight(new NodaTime.LocalDate(year, 1, 1));
        var yearEnd = JulianDay.FromUtcMidnight(new NodaTime.LocalDate(year + 1, 1, 1));

        // The crossing finder requires an estimate within half a revolution of the answer, and 1 January can be nearer
        // the PREVIOUS year's return than this one's. So the estimate is placed by the angle the Sun must still travel,
        // using the mean rate: the Sun advances ~0.9856 deg/day, so this lands within days of the answer rather than
        // half a year from it.
        var travelled = AngleMath.NormalizeDegrees(siderealAt(yearStart) - natalSiderealLongitudeDegrees);
        var estimate = yearStart + ((360.0 - travelled) / 0.98564736);

        try
        {
            var crossing = AngularCrossingFinder.FindCrossing(siderealAt, natalSiderealLongitudeDegrees, estimate);

            // A crossing outside the requested year is not this year's return: report absence rather than a plausible
            // number from the neighbouring year, which the caller has no way to distinguish.
            return crossing >= yearStart && crossing < yearEnd ? crossing : null;
        }
        catch (MyVedicDay.Shared.Exceptions.CalculationException)
        {
            // No monotonically-crossing solution in the window: report absence rather than a number the caller cannot
            // distinguish from a real answer.
            return null;
        }
    }

    /// <summary>
    /// The sidereal solar longitude at an instant, as this module defines it: tropical mean-equinox-of-date minus the
    /// ayanamsa of date, normalised to [0, 360). Exposed so a caller can verify a computed return by re-running the
    /// engine at that instant rather than trusting the solver.
    /// </summary>
    public static double SiderealLongitudeAt(double julianDayUt, IEphemeris ephemeris, IAyanamsa ayanamsa)
    {
        ArgumentNullException.ThrowIfNull(ephemeris);
        ArgumentNullException.ThrowIfNull(ayanamsa);
        return AngleMath.NormalizeDegrees(ephemeris.GetSunLongitude(julianDayUt) - ayanamsa.GetDegrees(julianDayUt));
    }

    private static Func<double, double> SiderealSun(IEphemeris ephemeris, IAyanamsa ayanamsa) =>
        julianDayUt => SiderealLongitudeAt(julianDayUt, ephemeris, ayanamsa);
}
