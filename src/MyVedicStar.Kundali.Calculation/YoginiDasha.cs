using NodaTime;

namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Yogini dasha: eight planetary periods totalling 36 years, selected by the moon's nakshatra.
/// </summary>
/// <remarks>
/// <para>The eight Yogini periods are Mangala 1, Pingala 2, Dhanya 3, Bhramari 4, Bhadrika 5, Ulka 6, Siddha 7 and
/// Sankata 8 years. The cycle therefore repeats every 36 years, dividing the 120-year Vimshottari cycle exactly three
/// times - which is why the two systems are traditionally paired.</para>
/// <para>The starting period is chosen by <c>(nakshatraNumber + 3) mod 8</c>, counting the nakshatra from one. The
/// offset of 3 is part of the definition, not a tuning constant: Ashwini (1) begins in Mangala.</para>
/// </remarks>
public static class YoginiDasha
{
    /// <summary>Total length of one Yogini cycle, in dasha years.</summary>
    public const double CycleYears = 36.0;

    /// <summary>Year length for Yogini periods. Held equal to Vimshottari so the two timelines never disagree on
    /// the length of a year.</summary>
    public const double DaysPerDashaYear = VimshottariDasha.DaysPerDashaYear;

    /// <summary>Degrees in one nakshatra.</summary>
    private const double DegreesPerNakshatra = 360.0 / 27.0;

    /// <summary>The eight Yogini periods in cycle order, with their ruling planet and length in years.</summary>
    private static readonly (string Name, string Ruler, double Years)[] Periods =
    [
        ("Mangala", "Moon", 1),
        ("Pingala", "Sun", 2),
        ("Dhanya", "Jupiter", 3),
        ("Bhramari", "Mars", 4),
        ("Bhadrika", "Mercury", 5),
        ("Ulka", "Saturn", 6),
        ("Siddha", "Venus", 7),
        ("Sankata", "Rahu", 8)
    ];

    /// <summary>The period names, in cycle order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Periods.Select(period => period.Name)];

    /// <summary>
    /// Builds the Yogini period sequence from birth.
    /// </summary>
    /// <param name="siderealMoonLongitude">Sidereal moon longitude in degrees, [0, 360).</param>
    /// <param name="birthInstant">The exact birth instant.</param>
    /// <returns>Eight consecutive periods, the first being a remainder of its full term.</returns>
    public static IReadOnlyList<YoginiPeriod> BuildPeriods(double siderealMoonLongitude, Instant birthInstant)
    {
        var normalized = Normalize(siderealMoonLongitude);

        var nakshatraIndex = (int)Math.Floor(normalized / DegreesPerNakshatra);
        var offsetWithin = normalized - (nakshatraIndex * DegreesPerNakshatra);
        var elapsedFraction = offsetWithin / DegreesPerNakshatra;

        // Nakshatra counted from one, offset by three, then folded into the eight-period cycle.
        var startIndex = ((nakshatraIndex + 1) + 3 - 1) % Periods.Length;

        var result = new List<YoginiPeriod>(Periods.Length);
        var cursor = birthInstant;

        for (var step = 0; step < Periods.Length; step++)
        {
            var (name, ruler, years) = Periods[(startIndex + step) % Periods.Length];

            var remainingYears = step == 0 ? years * (1.0 - elapsedFraction) : years;
            var end = cursor + Duration.FromSeconds(remainingYears * DaysPerDashaYear * 86400.0);

            result.Add(new YoginiPeriod(name, ruler, cursor, end, step == 0 && elapsedFraction > 0));
            cursor = end;
        }

        return result;
    }

    /// <summary>The period containing the instant, or null when it lies outside the built cycle.</summary>
    public static YoginiPeriod? PeriodAt(IReadOnlyList<YoginiPeriod> periods, Instant at)
    {
        ArgumentNullException.ThrowIfNull(periods);

        foreach (var period in periods)
        {
            if (at >= period.StartUtc && at < period.EndUtc)
                return period;
        }

        return null;
    }

    /// <summary>Full length of a named period, in dasha years.</summary>
    public static double YearsOf(string name)
    {
        foreach (var (periodName, _, years) in Periods)
        {
            if (string.Equals(periodName, name, StringComparison.Ordinal))
                return years;
        }

        throw new ArgumentException($"'{name}' is not a Yogini period.", nameof(name));
    }

    private static double Normalize(double degrees)
    {
        var wrapped = degrees % 360.0;
        return wrapped < 0 ? wrapped + 360.0 : wrapped;
    }
}

/// <summary>One Yogini period. <paramref name="Ruler"/> is the planet, which differs from the period's name.</summary>
public sealed record YoginiPeriod(string Name, string Ruler, Instant StartUtc, Instant EndUtc, bool IsRemainder);
