using NodaTime;

namespace MyVedicDay.Astronomy;

/// <summary>Lunar month (masa) in the Amanta convention: the month runs from one new moon to the next.</summary>
/// <remarks>
/// A month is named after the sidereal sign the Sun occupies at its starting new moon (Sun in Meena → Chaitra). A
/// month without a Sankranti is Adhika (intercalary) and repeats the following month's name. Purnimanta calendars
/// give the waning half the next month's name; festival rules are written in Amanta terms, so dates are unaffected.
/// </remarks>
public enum LunarMonth
{
    Chaitra = 1,
    Vaishakha,
    Jyeshtha,
    Ashadha,
    Shravana,
    Bhadrapada,
    Ashwin,
    Kartika,
    Margashirsha,
    Pausha,
    Magha,
    Phalguna,
}

/// <summary>Sidereal zodiac sign (Lahiri ayanamsa); also the solar month the Sun is in.</summary>
public enum Rashi
{
    Mesha = 1,
    Vrishabha,
    Mithuna,
    Karka,
    Simha,
    Kanya,
    Tula,
    Vrischika,
    Dhanu,
    Makara,
    Kumbha,
    Meena,
}

/// <summary>A Sankranti: the Sun entering a sidereal sign.</summary>
public readonly record struct Sankranti(Rashi Sign, Instant At);

/// <summary>
/// Lunar months (Amanta, with Adhika detection), the Sun's sidereal sign and Sankrantis, from solar and lunar
/// longitudes with the Lahiri ayanamsa. Shared by every calendar-dependent feature (festivals, Muhurta).
/// </summary>
public interface ILunisolarCalendar
{
    /// <summary>The Amanta lunar month containing <paramref name="instant"/>, and whether it is Adhika (intercalary).</summary>
    (LunarMonth Month, bool IsAdhika) GetLunarMonth(Instant instant);

    /// <summary>The sidereal sign the Sun occupies at <paramref name="instant"/>.</summary>
    Rashi GetSunSign(Instant instant);

    /// <summary>Every Sankranti in [<paramref name="from"/>, <paramref name="to"/>), in order.</summary>
    IReadOnlyList<Sankranti> FindSankrantis(Instant from, Instant to);
}

/// <summary>
/// <see cref="ILunisolarCalendar"/> over the shared ephemeris and the Lahiri ayanamsa (as the Drik Panchang uses).
/// </summary>
internal sealed class LunisolarCalendar(IEphemeris ephemeris, LahiriAyanamsa ayanamsa) : ILunisolarCalendar
{
    // Mean rates in degrees per day, used only to seed the crossing search.
    private const double MeanElongationRate = 12.19;
    private const double MeanSunRate = 0.9856;
    private const double SynodicMonthDays = 29.53;
    private const double SignSpan = 30d;

    /// <summary>The Amanta lunar month containing <paramref name="instant"/>, and whether it is Adhika.</summary>
    public (LunarMonth Month, bool IsAdhika) GetLunarMonth(Instant instant)
    {
        var julianDay = JulianDay.FromInstant(instant);
        var startNewMoon = AngularCrossingFinder.FindCrossing(Elongation, 0d, julianDay - (Elongation(julianDay) / MeanElongationRate));
        if (startNewMoon > julianDay)
        {
            startNewMoon = AngularCrossingFinder.FindCrossing(Elongation, 0d, startNewMoon - SynodicMonthDays);
        }

        var endNewMoon = AngularCrossingFinder.FindCrossing(Elongation, 0d, startNewMoon + SynodicMonthDays);

        var startSign = SignIndex(startNewMoon);
        var month = (LunarMonth)(((startSign + 1) % 12) + 1);
        return (month, startSign == SignIndex(endNewMoon));
    }

    /// <summary>The sign the Sun occupies at <paramref name="instant"/>.</summary>
    public Rashi GetSunSign(Instant instant) => (Rashi)(SignIndex(JulianDay.FromInstant(instant)) + 1);

    /// <summary>Every Sankranti in [<paramref name="from"/>, <paramref name="to"/>), in order.</summary>
    public IReadOnlyList<Sankranti> FindSankrantis(Instant from, Instant to)
    {
        var fromJulianDay = JulianDay.FromInstant(from);
        var toJulianDay = JulianDay.FromInstant(to);
        var result = new List<Sankranti>();

        var julianDay = fromJulianDay;
        var target = ((SignIndex(julianDay) + 1) % 12) * SignSpan;
        while (true)
        {
            var estimate = julianDay + (AngleMath.NormalizeDegrees(target - SiderealSun(julianDay)) / MeanSunRate);
            var crossing = AngularCrossingFinder.FindCrossing(SiderealSun, target, estimate);
            if (crossing >= toJulianDay)
            {
                return result;
            }

            if (crossing >= fromJulianDay)
            {
                result.Add(new Sankranti((Rashi)((int)(target / SignSpan) + 1), JulianDay.ToInstant(crossing)));
            }

            julianDay = crossing;
            target = AngleMath.NormalizeDegrees(target + SignSpan);
        }
    }

    private double Elongation(double julianDay) =>
        AngleMath.NormalizeDegrees(ephemeris.GetMoonLongitude(julianDay) - ephemeris.GetSunLongitude(julianDay));

    private double SiderealSun(double julianDay) =>
        AngleMath.NormalizeDegrees(ephemeris.GetSunLongitude(julianDay) - ayanamsa.GetDegrees(julianDay));

    private int SignIndex(double julianDay) => (int)Math.Floor(SiderealSun(julianDay) / SignSpan) % 12;
}
