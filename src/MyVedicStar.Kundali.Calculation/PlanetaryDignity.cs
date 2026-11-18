namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Planetary dignity, combustion and Parashari aspects: the <b>rules</b>, independent of any chart.
/// </summary>
/// <remarks>
/// <para><b>This type computes no positions.</b> It answers questions of the form "given that Mars is at this
/// longitude in this sign, what is its dignity?" - the chart comes from elsewhere. That separation is deliberate:
/// the rules are testable against their own definitions, while a placement needs an ephemeris we do not yet have for
/// the seven non-luminary grahas.</para>
/// <para>Nothing here is validated against a published ephemeris; these are the classical definitions expressed as
/// code and tested as rules.</para>
/// </remarks>
public static class PlanetaryDignity
{
    /// <summary>Dignity states, ordered from strongest to weakest so a caller can compare without knowing the order.</summary>
    public static readonly IReadOnlyList<string> Dignities =
        ["Exalted", "Moolatrikona", "Own", "Friendly", "Neutral", "Enemy", "Debilitated"];

    /// <summary>
    /// Exaltation sign index (0 = Aries) and the degree of deep exaltation, per planet.
    /// </summary>
    /// <remarks>
    /// The degree matters: exaltation is not the whole sign, and a planet at the exact deep-exaltation degree is
    /// treated differently from one merely in the sign. Both are recorded so a caller can distinguish them.
    /// </remarks>
    private static readonly Dictionary<string, (int Sign, double Degree)> Exaltation = new(StringComparer.Ordinal)
    {
        ["Sun"] = (0, 10.0),      // Aries 10
        ["Moon"] = (1, 3.0),      // Taurus 3
        ["Mars"] = (9, 28.0),     // Capricorn 28
        ["Mercury"] = (5, 15.0),  // Virgo 15
        ["Jupiter"] = (3, 5.0),   // Cancer 5
        ["Venus"] = (11, 27.0),   // Pisces 27
        ["Saturn"] = (6, 20.0),   // Libra 20
        ["Rahu"] = (2, 20.0),     // Gemini 20 (node convention is a stated choice, not universal)
        ["Ketu"] = (8, 20.0)      // Sagittarius 20
    };

    /// <summary>Own signs. A planet in one of these is in its own sign regardless of degree.</summary>
    private static readonly Dictionary<string, int[]> OwnSigns = new(StringComparer.Ordinal)
    {
        ["Sun"] = [4],
        ["Moon"] = [3],
        ["Mars"] = [0, 7],
        ["Mercury"] = [2, 5],
        ["Jupiter"] = [8, 11],
        ["Venus"] = [1, 6],
        ["Saturn"] = [9, 10],
        // The nodes own no sign in the classical scheme; assigning one would be an invented rule, so they are absent
        // and reach the Debilitated/Friendly/Enemy path only through exaltation and friendship.
    };

    /// <summary>Moolatrikona sign and degree span per planet. Only signs that differ from the own sign matter.</summary>
    private static readonly Dictionary<string, (int Sign, double From, double To)> Moolatrikona =
        new(StringComparer.Ordinal)
    {
            ["Sun"] = (4, 0.0, 20.0),      // Leo 0-20
            ["Moon"] = (1, 4.0, 30.0),     // Taurus 4-30
            ["Mars"] = (0, 0.0, 12.0),     // Aries 0-12
            ["Mercury"] = (5, 16.0, 20.0), // Virgo 16-20
            ["Jupiter"] = (8, 0.0, 10.0),  // Sagittarius 0-10
            ["Venus"] = (6, 0.0, 15.0),    // Libra 0-15
            ["Saturn"] = (10, 0.0, 20.0)   // Aquarius 0-20
        };

    /// <summary>
    /// Natural friendship between planets, as a directed relation. <c>Friendly</c> and <c>Enemy</c> are the classical
    /// lists; anything unlisted is neutral.
    /// </summary>
    private static readonly Dictionary<string, (string[] Friends, string[] Enemies)> Friendships =
        new(StringComparer.Ordinal)
        {
            ["Sun"] = (["Moon", "Mars", "Jupiter"], ["Venus", "Saturn"]),
            ["Moon"] = (["Sun", "Mercury"], []),
            ["Mars"] = (["Sun", "Moon", "Jupiter"], ["Mercury"]),
            ["Mercury"] = (["Sun", "Venus"], ["Moon"]),
            ["Jupiter"] = (["Sun", "Moon", "Mars"], ["Mercury", "Venus"]),
            ["Venus"] = (["Mercury", "Saturn"], ["Sun", "Moon"]),
            ["Saturn"] = (["Mercury", "Venus"], ["Sun", "Moon", "Mars"]),
            ["Rahu"] = (["Venus", "Saturn", "Mercury"], ["Sun", "Moon", "Mars"]),
            ["Ketu"] = (["Mars", "Venus", "Saturn"], ["Sun", "Moon"])
        };

    /// <summary>Degrees per sign.</summary>
    private const double DegreesPerSign = 30.0;

    /// <summary>
    /// Dignity of a planet at a sidereal longitude.
    /// </summary>
    /// <remarks>
    /// Precedence is deliberate and follows the classical order of strength: exaltation beats moolatrikona beats own
    /// sign beats friendship. Checking them in another order would silently promote a friendly planet over an own-sign
    /// one.
    /// </remarks>
    public static string Of(string planet, double siderealLongitude)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planet);

        var normalized = Normalize(siderealLongitude);
        var sign = (int)Math.Floor(normalized / DegreesPerSign) % 12;
        var degree = normalized - (sign * DegreesPerSign);

        if (Exaltation.TryGetValue(planet, out var exalted) && exalted.Sign == sign)
            return "Exalted";

        if (Moolatrikona.TryGetValue(planet, out var moola) && moola.Sign == sign &&
            degree >= moola.From && degree < moola.To)
            return "Moolatrikona";

        if (OwnSigns.TryGetValue(planet, out var own) && own.Contains(sign))
            return "Own";

        // Debilitation is not a separate table: it is the sign opposite exaltation, which is why it is derived here
        // rather than listed. Two tables could disagree; a derivation cannot.
        if (Exaltation.TryGetValue(planet, out var exalt) && (exalt.Sign + 6) % 12 == sign)
            return "Debilitated";

        // No friendship data means no natural relationship can be asserted, so the honest answer is neutral rather
        // than a guess in either direction.
        if (!Friendships.TryGetValue(planet, out var relations))
            return "Neutral";

        var ruler = SignRuler(sign);
        if (relations.Friends.Contains(ruler, StringComparer.Ordinal))
            return "Friendly";
        if (relations.Enemies.Contains(ruler, StringComparer.Ordinal))
            return "Enemy";

        return "Neutral";
    }

    /// <summary>The classical ruler of a sign. Used for friendship-based dignity only.</summary>
    public static string SignRuler(int signIndex) => NormalizeIndex(signIndex) switch
    {
        0 => "Mars", 1 => "Venus", 2 => "Mercury", 3 => "Moon",
        4 => "Sun", 5 => "Mercury", 6 => "Venus", 7 => "Mars",
        8 => "Jupiter", 9 => "Saturn", 10 => "Saturn", _ => "Jupiter"
    };

    /// <summary>
    /// Whether two bodies are combust: within the Sun's combustion orb for that planet.
    /// </summary>
    /// <remarks>
    /// The orbs are stated per planet because they genuinely differ; a single orb would be wrong for every planet
    /// that is not the Sun's own. Mercury and Venus have two values by retrograde state, which is why
    /// <paramref name="isRetrograde"/> is a parameter rather than an assumption.
    /// </remarks>
    public static bool IsCombust(string planet, double planetLongitude, double sunLongitude, bool isRetrograde)
    {
        if (string.Equals(planet, "Sun", StringComparison.Ordinal))
            return false;

        var orb = CombustionOrb(planet, isRetrograde);
        if (orb is null)
            return false;

        var separation = Math.Abs(AngularSeparation(planetLongitude, sunLongitude));
        return separation <= orb.Value;
    }

    /// <summary>Combustion orb in degrees, or null when the body is never called combust.</summary>
    public static double? CombustionOrb(string planet, bool isRetrograde) => planet switch
    {
        "Moon" => 12.0,
        "Mars" => 17.0,
        "Mercury" => isRetrograde ? 12.0 : 14.0,
        "Jupiter" => 11.0,
        "Venus" => isRetrograde ? 8.0 : 10.0,
        "Saturn" => 15.0,
        // The nodes are shadow bodies and are not assigned a combustion orb.
        _ => null
    };

    /// <summary>Shortest angular separation between two longitudes, always in [0, 180].</summary>
    public static double AngularSeparation(double first, double second)
    {
        var raw = Math.Abs(Normalize(first) - Normalize(second));
        return raw > 180.0 ? 360.0 - raw : raw;
    }

    /// <summary>
    /// Whether <paramref name="from"/> casts a Parashari aspect on the given sign index.
    /// </summary>
    /// <remarks>
    /// Whole-sign Parashari: every planet aspects the 7th sign from itself; Mars additionally the 4th and 8th,
    /// Jupiter the 5th and 9th, Saturn the 3rd and 10th. No orb is applied because whole-sign aspects have none -
    /// which is exactly why this cannot be re-expressed as a degree-based aspect without changing the method.
    /// </remarks>
    public static bool Aspects(string planet, int fromSign, int toSign)
    {
        if (fromSign is < 0 or > 11)
            throw new ArgumentOutOfRangeException(nameof(fromSign), fromSign, "Sign index must be 0-11.");

        var offset = (NormalizeIndex(toSign) - NormalizeIndex(fromSign) + 12) % 12;

        if (offset == 6)
            return true;

        return planet switch
        {
            "Mars" => offset is 3 or 7,
            "Jupiter" => offset is 4 or 8,
            "Saturn" => offset is 2 or 9,
            _ => false
        };
    }

    /// <summary>Every sign index a planet aspects, ascending. Derived from <see cref="Aspects"/>, never a second table.</summary>
    public static IReadOnlyList<int> AspectedSigns(string planet, int fromSign) =>
        Enumerable.Range(0, 12).Where(to => Aspects(planet, fromSign, to)).ToArray();

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
