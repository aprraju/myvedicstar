using NodaTime;

namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Chara dasha: sign-based periods driven by the Lagna (ascendant), not by the moon.
/// </summary>
/// <remarks>
/// <para><b>Scope limit, stated up front.</b> Classical Chara dasha (Jaimini) determines each sign's period length by
/// counting to its lord's position in the chart. That requires an ascendant and all nine planetary positions, and the
/// ascendant is currently withheld as <c>unvalidated-reference</c> because we have no independent reference for it.
/// Computing a Chara timeline on top of an unvalidated ascendant would dress an unproven number in the appearance of a
/// proven one.</para>
/// <para>So this type implements only the part that does not depend on the ascendant: <b>the equal-sign progression</b>
/// - the sequence of signs traversed from a given starting sign, and their start/end instants when each sign is given
/// the same duration. That progression is well defined and is the skeleton every Chara variant shares; the
/// variable-length rule that differentiates the variants is deliberately not asserted.</para>
/// <para>Callers must therefore report Chara as <c>unvalidated-reference</c> until an ascendant reference exists. The
/// equal-period timeline here is not presented as a Chara dasha.</para>
/// </remarks>
public static class CharaDasha
{
    /// <summary>Year length for Chara periods, held equal to the other systems.</summary>
    public const double DaysPerDashaYear = VimshottariDasha.DaysPerDashaYear;

    /// <summary>The twelve signs, in zodiacal order.</summary>
    private static readonly string[] Signs =
    [
        "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo",
        "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"
    ];

    /// <summary>The twelve sign names, in zodiacal order.</summary>
    public static IReadOnlyList<string> SignNames { get; } = Signs;

    /// <summary>
    /// Builds an equal-duration sign progression: twelve periods of one year each, starting at
    /// <paramref name="startSignIndex"/>.
    /// </summary>
    /// <param name="startSignIndex">Zero-based sign index, 0 = Aries.</param>
    /// <param name="birthInstant">The instant the progression begins.</param>
    /// <param name="yearsPerSign">Duration of each sign, in dasha years. Equal by construction.</param>
    /// <returns>Twelve contiguous periods covering twelve signs.</returns>
    /// <remarks>
    /// Equal durations are a simplification, not the classical rule. It is exposed so the sign progression and instant
    /// arithmetic can be tested, and so a future variable-length implementation has a shape to replace.
    /// </remarks>
    public static IReadOnlyList<CharaPeriod> BuildSignProgression(int startSignIndex, Instant birthInstant,
        double yearsPerSign = 1.0)
    {
        if (startSignIndex is < 0 or > 11)
            throw new ArgumentOutOfRangeException(nameof(startSignIndex), startSignIndex,
                "Sign index must be between 0 and 11.");

        if (yearsPerSign <= 0)
            throw new ArgumentOutOfRangeException(nameof(yearsPerSign), yearsPerSign,
                "Sign duration must be positive.");

        var result = new List<CharaPeriod>(Signs.Length);
        var cursor = birthInstant;

        for (var step = 0; step < Signs.Length; step++)
        {
            var end = cursor + Duration.FromSeconds(yearsPerSign * DaysPerDashaYear * 86400.0);
            result.Add(new CharaPeriod(Signs[(startSignIndex + step) % Signs.Length], cursor, end));
            cursor = end;
        }

        return result;
    }

    /// <summary>The period containing the instant, or null when it lies outside the progression.</summary>
    public static CharaPeriod? PeriodAt(IReadOnlyList<CharaPeriod> periods, Instant at)
    {
        ArgumentNullException.ThrowIfNull(periods);

        foreach (var period in periods)
        {
            if (at >= period.StartUtc && at < period.EndUtc)
                return period;
        }

        return null;
    }
}

/// <summary>One equal-duration sign period. Not a classical Chara dasha period; see the type remarks.</summary>
public sealed record CharaPeriod(string Sign, Instant StartUtc, Instant EndUtc);
