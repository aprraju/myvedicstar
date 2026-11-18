namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Divisional (varga) chart mapping: maps a sidereal longitude in a source sign to the sign it occupies in a
/// higher-division chart.
/// </summary>
/// <remarks>
/// <para><b>Scope limit, stated up front.</b> Only the <b>cyclical equal-partition</b> families are implemented: the
/// divisional sign is obtained by taking the part index within the source sign and adding it to a starting sign
/// determined by the source sign's element or mobility. This covers D1, D2, D3, D7, D9, D10, D12 and D30.</para>
/// <para>Charts whose rule is not a cyclic shift - D16 (Kalamsa), D20 (Bhamsa), D24 (Siddhamsa), D27 (Bhamsa), D40
/// (Khavedamsa), D45 (Akshavedamsa) - use a different construction with their own starting-sign rules. They are
/// <b>not</b> implemented here, and the caller must report them as unsupported rather than falling back to a nearest
/// approximation. D60 (Shashtiamsa) is listed as provisional for the same reason.</para>
/// <para>No claim is made that any mapping here matches a published ephemeris: these are the classical construction
/// rules, tested as rules. Validating them against a computed chart is a separate task requiring a reference.</para>
/// </remarks>
public static class DivisionalCharts
{
    /// <summary>Degrees per sign.</summary>
    private const double DegreesPerSign = 30.0;

    /// <summary>Divisions whose rule is a cyclic shift from the source sign and which this class implements.</summary>
    public static readonly IReadOnlyList<int> ImplementedDivisions = [1, 2, 3, 7, 9, 10, 12, 30];

    /// <summary>True when the mapping rule for this division is implemented here.</summary>
    public static bool IsImplemented(int divisions) => ImplementedDivisions.Contains(divisions);

    /// <summary>
    /// The sign index (0 = Aries) of <paramref name="siderealLongitude"/> in the given divisional chart.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The division is not implemented, so no value is invented.</exception>
    public static int SignIn(int divisions, double siderealLongitude)
    {
        // Rejecting rather than approximating: a wrong divisional sign is indistinguishable from a right one once it
        // leaves this method, so the only safe behaviour is to refuse.
        if (!IsImplemented(divisions))
        {
            throw new ArgumentOutOfRangeException(nameof(divisions), divisions,
                "This divisional chart's mapping rule is not implemented.");
        }

        var normalized = Normalize(siderealLongitude);
        var sourceSign = (int)Math.Floor(normalized / DegreesPerSign) % 12;
        var withinSign = normalized - (sourceSign * DegreesPerSign);

        // Which equal part of the sign the longitude falls in. Clamped so a longitude of exactly the sign boundary
        // cannot produce an index one past the end through floating-point error.
        var part = Math.Min((int)Math.Floor(withinSign / (DegreesPerSign / divisions)), divisions - 1);

        return divisions switch
        {
            // D1 is the birth chart itself.
            1 => sourceSign,

            // D2 (Hora): the classical rule splits into only two signs - Leo for the first half of an odd sign and the
            // second half of an even sign, Cancer otherwise. Not a cyclic shift, handled explicitly.
            2 => HoraSign(sourceSign, part),

            // D3 (Drekkana): three parts, counted from the sign itself, the 5th and the 9th.
            3 => (sourceSign + (part * 4)) % 12,

            // D7 (Saptamsa): counted from the sign itself for odd signs, from the 7th for even.
            7 => (sourceSign + (IsOddSign(sourceSign) ? part : part + 6)) % 12,

            // D9 (Navamsa): counted from the sign itself for movable signs, the 9th for fixed, the 5th for dual.
            9 => (sourceSign + NavamsaStart(sourceSign) + part) % 12,

            // D10 (Dasamsa): from the sign itself for odd, from the 9th for even.
            10 => (sourceSign + (IsOddSign(sourceSign) ? part : part + 8)) % 12,

            // D12 (Dwadasamsa): twelve parts counted from the sign itself.
            12 => (sourceSign + part) % 12,

            // D30 (Trimsamsa): unequal parts by sign parity - five divisions in odd signs, five different ones in even.
            30 => TrimsamsaSign(sourceSign, withinSign),

            _ => throw new ArgumentOutOfRangeException(nameof(divisions), divisions,
                "This divisional chart's mapping rule is not implemented.")
        };
    }

    /// <summary>Sign name for a zero-based index.</summary>
    public static string NameOf(int signIndex) => Names[NormalizeIndex(signIndex)];

    private static readonly string[] Names =
    [
        "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo",
        "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"
    ];

    private static bool IsOddSign(int signIndex) => signIndex % 2 == 0;

    /// <summary>Movable (0), fixed (1) or dual (2) - the triplicity that drives the Navamsa start.</summary>
    private static int NavamsaStart(int signIndex) => (signIndex % 3) switch
    {
        0 => 0,   // movable: from the sign itself
        1 => 8,   // fixed: from the 9th
        _ => 4    // dual: from the 5th
    };

    private static int HoraSign(int sourceSign, int part) =>
        IsOddSign(sourceSign)
            ? (part == 0 ? 4 : 3)   // odd: Leo then Cancer
            : (part == 0 ? 3 : 4);  // even: Cancer then Leo

    /// <summary>
    /// Trimsamsa uses unequal parts: 5,5,8,7,5 degrees in odd signs and the reverse attribution in even signs.
    /// </summary>
    private static int TrimsamsaSign(int sourceSign, double withinSign)
    {
        // Part boundaries differ by parity, so the mapping is tabulated rather than computed from a formula that
        // would hide the asymmetry.
        (double Limit, int Sign)[] table = IsOddSign(sourceSign)
            ? [(5.0, 0), (10.0, 10), (18.0, 8), (25.0, 2), (30.0, 6)]    // Mars, Saturn, Jupiter, Mercury, Venus
            : [(5.0, 1), (12.0, 5), (20.0, 11), (25.0, 9), (30.0, 7)];   // Venus, Mercury, Jupiter, Saturn, Mars

        foreach (var (limit, sign) in table)
        {
            if (withinSign < limit)
                return sign;
        }

        return table[^1].Sign;
    }

    private static int NormalizeIndex(int signIndex)
    {
        var wrapped = signIndex % 12;
        return wrapped < 0 ? wrapped + 12 : wrapped;
    }

    private static double Normalize(double degrees)
    {
        var wrapped = degrees % 360.0;
        return wrapped < 0 ? wrapped + 360.0 : wrapped;
    }
}
