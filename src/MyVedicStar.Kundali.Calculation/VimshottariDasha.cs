using NodaTime;

namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Vimshottari dasha: the moon's nakshatra at birth selects a starting lord and a fraction of that lord's period is
/// already expended, so the first period is a remainder rather than a full term.
/// </summary>
/// <remarks>
/// <para>Pure arithmetic over an already-computed sidereal moon longitude and the birth instant. It performs no
/// astronomy of its own and reads no clock, so the same inputs always give the same periods.</para>
/// <para>Everything is expressed as UTC instants. Local rendering is the client's job - a dasha boundary is a moment
/// in time, not a wall-clock reading in an arbitrary zone.</para>
/// </remarks>
public static class VimshottariDasha
{
    /// <summary>
    /// Year length used for period durations: 365.25 days, per the approved convention for this project.
    /// </summary>
    /// <remarks>
    /// Stated explicitly and used for every period. It is deliberately not 365.2425 (the Gregorian mean year) and not
    /// 360: those produce dasha boundaries that drift by days over a lifetime, so the constant is a documented
    /// decision rather than an incidental one.
    /// </remarks>
    public const double DaysPerDashaYear = 365.25;

    /// <summary>Width of one nakshatra in degrees: 27 equal divisions of the circle.</summary>
    private const double DegreesPerNakshatra = 360.0 / 27.0;

    /// <summary>
    /// The Vimshottari lord sequence and each lord's full period in dasha years. The order is fixed by the tradition
    /// and the cycle repeats every 120 years.
    /// </summary>
    private static readonly (string Lord, double Years)[] Periods =
    [
        ("Ketu", 7),
        ("Venus", 20),
        ("Sun", 6),
        ("Moon", 10),
        ("Mars", 7),
        ("Rahu", 18),
        ("Jupiter", 16),
        ("Saturn", 19),
        ("Mercury", 17)
    ];

    /// <summary>Total of one full Vimshottari cycle, in dasha years.</summary>
    public const double CycleYears = 120.0;

    /// <summary>
    /// Builds the mahadasha sequence covering the birth instant onward, one full cycle.
    /// </summary>
    /// <param name="siderealMoonLongitude">Sidereal (nirayana) moon longitude in degrees, [0, 360).</param>
    /// <param name="birthInstant">The exact birth instant.</param>
    /// <returns>Nine consecutive periods, the first being a partial remainder.</returns>
    public static IReadOnlyList<VimshottariPeriod> BuildMahadashas(double siderealMoonLongitude,
        Instant birthInstant)
    {
        var normalized = Normalize(siderealMoonLongitude);

        // Which nakshatra, and how far through it the moon had travelled. The elapsed fraction is the fraction of the
        // lord's period already spent before birth.
        var nakshatraIndex = (int)Math.Floor(normalized / DegreesPerNakshatra);
        var offsetWithinNakshatra = normalized - (nakshatraIndex * DegreesPerNakshatra);
        var elapsedFraction = offsetWithinNakshatra / DegreesPerNakshatra;

        // Nakshatra lords follow the same nine-lord sequence starting at Ketu.
        var startLordIndex = nakshatraIndex % Periods.Length;

        var periods = new List<VimshottariPeriod>(Periods.Length);
        var cursor = birthInstant;

        for (var step = 0; step < Periods.Length; step++)
        {
            var (lord, years) = Periods[(startLordIndex + step) % Periods.Length];

            // Only the first period is shortened, and only by the already-elapsed fraction of it.
            var remainingYears = step == 0 ? years * (1.0 - elapsedFraction) : years;
            var end = cursor + Duration.FromSeconds(remainingYears * DaysPerDashaYear * 86400.0);

            periods.Add(new VimshottariPeriod(lord, cursor, end, step == 0));
            cursor = end;
        }

        return periods;
    }

    /// <summary>
    /// The period containing <paramref name="asOf"/>, or null when the instant falls outside the built cycle.
    /// </summary>
    /// <remarks>
    /// Null is returned rather than clamping to the nearest period: a query outside the covered span has no dasha, and
    /// reporting the closest one would state something false about the person's chart.
    /// </remarks>
    public static VimshottariPeriod? PeriodAt(IReadOnlyList<VimshottariPeriod> periods, Instant asOf)
    {
        ArgumentNullException.ThrowIfNull(periods);

        foreach (var period in periods)
        {
            if (asOf >= period.StartUtc && asOf < period.EndUtc)
                return period;
        }

        return null;
    }

    private static double Normalize(double degrees)
    {
        var wrapped = degrees % 360.0;
        return wrapped < 0 ? wrapped + 360.0 : wrapped;
    }
}

/// <summary>
/// One Vimshottari period. <paramref name="IsRemainder"/> marks the birth period, whose start precedes the birth
/// instant and which is therefore not a full term.
/// </summary>
public sealed record VimshottariPeriod(string Lord, Instant StartUtc, Instant EndUtc, bool IsRemainder);
