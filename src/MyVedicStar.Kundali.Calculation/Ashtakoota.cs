namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Ashtakoota: the eight-category compatibility assessment of two lunar positions, totalling 36 points (KUN-15).
/// </summary>
/// <remarks>
/// <para><b>What this is, and what it refuses to be.</b> It scores eight classical kootas from each side's Moon sign and
/// nakshatra, and returns a total out of 36. It does <b>not</b> decide whether the total is an "acceptable match".
/// A minimum-score threshold is an interpretation, and the owner has left it OPEN. This type therefore returns the
/// score and never a verdict; see <see cref="Result"/>, which has no field that could carry one.</para>
/// <para><b>Indeterminate is not zero.</b> When a category cannot be evaluated - most commonly because a birth time is
/// unknown, which leaves the Moon's nakshatra undefined - the category is reported with a reason and no score. It is
/// never scored <c>0</c>, because a consumer receiving 0 cannot tell "scored zero" from "not computed" and would render
/// a false negative.</para>
/// <para><b>Conventions are data, cited, and versioned.</b> As with Ashtakavarga, the tables below are the classical
/// Parashari koota rules expressed as data and tested as rules. A wrong table produces plausible-looking numbers, so
/// <see cref="RulesVersion"/> is persisted with every comparison and a table edit is a version bump.</para>
/// </remarks>
public static class Ashtakoota
{
    /// <summary>Identifies the exact rule tables used. Stored with every comparison result.</summary>
    public const string RulesVersion = "ashtakoota-parashari-1";

    /// <summary>Total points available across all eight kootas. Stated once; categories sum to it.</summary>
    public const int MaximumTotal = 36;

    /// <summary>The eight koota codes, in the classical presentation order.</summary>
    public static readonly IReadOnlyList<string> KootaCodes =
    [
        "Varna", "Vashya", "Tara", "Yoni", "GrahaMaitri", "Gana", "Bhakoot", "Nadi"
    ];

    /// <summary>Maximum points per koota. Sums to <see cref="MaximumTotal"/> and is asserted as such.</summary>
    public static readonly IReadOnlyDictionary<string, int> MaximumByKoota =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Varna"] = 1,
            ["Vashya"] = 2,
            ["Tara"] = 3,
            ["Yoni"] = 4,
            ["GrahaMaitri"] = 5,
            ["Gana"] = 6,
            ["Bhakoot"] = 7,
            ["Nadi"] = 8,
        };

    /// <summary>Reason codes for a category that could not be evaluated.</summary>
    public static class Reasons
    {
        /// <summary>The Moon's nakshatra or sign is undefined because the birth time is not known exactly.</summary>
        public const string BirthTimeUnknown = "BirthTimeUnknown";

        /// <summary>The Moon's position could not be computed for this profile, for a reason other than birth time.</summary>
        public const string MoonPositionUnavailable = "MoonPositionUnavailable";
    }

    /// <summary>The sign and nakshatra an Ashtakoota category needs, as consumed from the engine.</summary>
    /// <param name="SignIndex">Zero-based Moon sign, 0 = Aries.</param>
    /// <param name="NakshatraIndex">Zero-based nakshatra, 0 = Ashwini.</param>
    /// <param name="Available">False when either is undefined; every field is then meaningless.</param>
    /// <param name="UnavailableReason">A <see cref="Reasons"/> value when <paramref name="Available"/> is false.</param>
    public readonly record struct Side(
        int SignIndex,
        int NakshatraIndex,
        bool Available,
        string? UnavailableReason)
    {
        public static Side Unavailable(string reason) => new(0, 0, false, reason);

        public static Side Of(int signIndex, int nakshatraIndex) => new(signIndex, nakshatraIndex, true, null);
    }

    /// <summary>One category's outcome: either a score, or a reason, never both.</summary>
    /// <param name="Code">Koota code from <see cref="KootaCodes"/>.</param>
    /// <param name="Maximum">Points available for this koota; always present, even when unscored.</param>
    /// <param name="Score">Points awarded, or null when the category is indeterminate.</param>
    /// <param name="Reason">A <see cref="Reasons"/> value exactly when <paramref name="Score"/> is null.</param>
    /// <param name="Side">Which side made it indeterminate, when one did.</param>
    /// <param name="Note">Short human-readable basis for the score, or the reason in words. Never empty.</param>
    public sealed record Category(
        string Code,
        int Maximum,
        int? Score,
        string? Reason,
        string? Side,
        string Note)
    {
        public bool IsScored => Score.HasValue;
    }

    /// <summary>The complete assessment. Deliberately carries NO threshold or pass/fail field.</summary>
    /// <param name="RulesVersion">The rule table version used.</param>
    /// <param name="Categories">All eight categories, in <see cref="KootaCodes"/> order.</param>
    /// <param name="ScoredTotal">Points earned across the SCORED categories.</param>
    /// <param name="Certainty">complete or partial.</param>
    public sealed record Result(
        string RulesVersion,
        IReadOnlyList<Category> Categories,
        int ScoredTotal,
        string Certainty);

    /// <summary>Certainty values. Closed set: a comparison is never partly-labelled.</summary>
    public static class CertaintyValues
    {
        public const string Complete = "complete";
        public const string Partial = "partial";
    }

    /// <summary>Assesses two sides. Categories are always all eight; unscored ones carry a reason.</summary>
    public static Result Assess(Side sideA, Side sideB)
    {
        var categories = new List<Category>(KootaCodes.Count)
        {
            Varna(sideA, sideB),
            Vashya(sideA, sideB),
            Tara(sideA, sideB),
            Yoni(sideA, sideB),
            GrahaMaitri(sideA, sideB),
            Gana(sideA, sideB),
            Bhakoot(sideA, sideB),
            Nadi(sideA, sideB),
        };

        var scoredTotal = categories.Sum(category => category.Score ?? 0);
        var certainty = categories.All(category => category.IsScored)
            ? CertaintyValues.Complete
            : CertaintyValues.Partial;

        return new Result(RulesVersion, categories, scoredTotal, certainty);
    }

    // --- Individual kootas ------------------------------------------------------------------------------------------

    /// <summary>Varna: one of four birth-order classes; full point when A's varna is at least B's.</summary>
    /// <remarks>
    /// Control: the higher varna must not be lower than the partner's. A grader to B's 0 of 1 when A's class is lower.
    /// </remarks>
    private static Category Varna(Side a, Side b)
    {
        if (Indeterminate(a, b) is { } gap) return gap("Varna");

        var varnaA = SignVarna[a.SignIndex];
        var varnaB = SignVarna[b.SignIndex];
        var score = varnaA >= varnaB ? 1 : 0;
        return new Category("Varna", 1, score, null, null,
            $"A varna {varnaA} vs B varna {varnaB}: {(score == 1 ? "A not below B" : "A below B")}.");
    }

    /// <summary>Vashya: mutual attraction class; graded 0, 1 or 2 from the pair.</summary>
    private static Category Vashya(Side a, Side b)
    {
        if (Indeterminate(a, b) is { } gap) return gap("Vashya");

        var vashyaA = SignVashya[a.SignIndex];
        var vashyaB = SignVashya[b.SignIndex];

        // Full marks for the same class; one for the two "middle" classes pairing; zero otherwise.
        var score = vashyaA == vashyaB ? 2 : (IsAdjacentClass(vashyaA, vashyaB) ? 1 : 0);
        return new Category("Vashya", 2, score, null, null,
            $"A vashya {vashyaA} vs B vashya {vashyaB}: {score} of 2.");
    }

    /// <summary>Tara: the nakshatra count between the two Moons, scored in both directions.</summary>
    private static Category Tara(Side a, Side b)
    {
        if (Indeterminate(a, b) is { } gap) return gap("Tara");

        // Count from A to B and B to A, 1-based over 27 nakshatras; the remainder mod 9 gives the tara.
        var taraAtoB = ((b.NakshatraIndex - a.NakshatraIndex + 27) % 27) + 1;
        var taraBtoA = ((a.NakshatraIndex - b.NakshatraIndex + 27) % 27) + 1;

        var favourableA = IsFavourableTara(taraAtoB);
        var favourableB = IsFavourableTara(taraBtoA);

        var score = (favourableA ? 1 : 0) + (favourableB ? 1 : 0) + (favourableA && favourableB ? 1 : 0);
        return new Category("Tara", 3, score, null, null,
            $"Tara A->B {taraAtoB}, B->A {taraBtoA}: {score} of 3.");
    }

    /// <summary>Yoni: animal-symbol compatibility from each Moon nakshatra, graded 0-4.</summary>
    private static Category Yoni(Side a, Side b)
    {
        if (Indeterminate(a, b) is { } gap) return gap("Yoni");

        var yoniA = NakshatraYoni[a.NakshatraIndex];
        var yoniB = NakshatraYoni[b.NakshatraIndex];
        var score = YoniScore(yoniA, yoniB);

        return new Category("Yoni", 4, score, null, null,
            $"A yoni {yoniA} vs B yoni {yoniB}: {score} of 4.");
    }

    /// <summary>Graha Maitri: friendship between the two Moon-sign lords, graded 0-5.</summary>
    private static Category GrahaMaitri(Side a, Side b)
    {
        if (Indeterminate(a, b) is { } gap) return gap("GrahaMaitri");

        var lordA = SignLord[a.SignIndex];
        var lordB = SignLord[b.SignIndex];

        if (lordA == lordB)
            return new Category("GrahaMaitri", 5, 5, null, null, $"Both Moons ruled by {lordA}.");

        var aToB = Friendship(lordA, lordB);
        var bToA = Friendship(lordB, lordA);

        var score = (aToB, bToA) switch
        {
            (Relation.Friend, Relation.Friend) => 5,
            (Relation.Friend, Relation.Neutral) or (Relation.Neutral, Relation.Friend) => 4,
            (Relation.Friend, Relation.Enemy) or (Relation.Enemy, Relation.Friend) => 1,
            (Relation.Neutral, Relation.Neutral) => 3,
            (Relation.Neutral, Relation.Enemy) or (Relation.Enemy, Relation.Neutral) => 0,
            _ => 0
        };

        return new Category("GrahaMaitri", 5, score, null, null,
            $"A lord {lordA} ({aToB} to {lordB}), B lord {lordB} ({bToA} to {lordA}): {score} of 5.");
    }

    /// <summary>Gana: temperament class from the Moon nakshatra, graded 0-6.</summary>
    private static Category Gana(Side a, Side b)
    {
        if (Indeterminate(a, b) is { } gap) return gap("Gana");

        var ganaA = NakshatraGana[a.NakshatraIndex];
        var ganaB = NakshatraGana[b.NakshatraIndex];

        var score = GanaScore(ganaA, ganaB);
        return new Category("Gana", 6, score, null, null, $"A gana {ganaA} vs B gana {ganaB}: {score} of 6.");
    }

    /// <summary>Bhakoot: the sign-distance between the two Moons, which must not be 2/12, 5/9 or 6/8.</summary>
    /// <remarks>
    /// The classical rule scores 7 for any distance that is not one of the three adverse pairs, and 0 otherwise. The
    /// adverse pairs are the point of the koota, so they are named rather than computed from a modulus that could
    /// silently change which distances they cover.
    /// </remarks>
    private static Category Bhakoot(Side a, Side b)
    {
        if (Indeterminate(a, b) is { } gap) return gap("Bhakoot");

        var distance = ((b.SignIndex - a.SignIndex + 12) % 12) + 1;
        var adverse = distance is 2 or 5 or 6 or 8 or 9 or 12;
        var score = adverse ? 0 : 7;

        return new Category("Bhakoot", 7, score, null, null,
            $"Moon signs {distance}/12 apart: {(adverse ? "adverse pair" : "not an adverse pair")}, {score} of 7.");
    }

    /// <summary>Nadi: three constitutions from the Moon nakshatra; different constitutions score the full 8.</summary>
    private static Category Nadi(Side a, Side b)
    {
        if (Indeterminate(a, b) is { } gap) return gap("Nadi");

        var nadiA = NakshatraNadi[a.NakshatraIndex];
        var nadiB = NakshatraNadi[b.NakshatraIndex];
        var score = nadiA == nadiB ? 0 : 8;

        return new Category("Nadi", 8, score, null, null,
            $"A nadi {nadiA} vs B nadi {nadiB}: {(score == 8 ? "different" : "same")}, {score} of 8.");
    }

    // --- Indeterminacy ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns a factory for an indeterminate category when either side is unavailable, or null when both are usable.
    /// </summary>
    /// <remarks>
    /// Both sides are checked so the <c>Side</c> reported names the actual cause. If A is unknown and B is fine, the
    /// reason belongs to "A" - telling a consumer that B's birth time is unknown would send them to fix the wrong
    /// profile.
    /// </remarks>
    private static Func<string, Category>? Indeterminate(Side a, Side b)
    {
        if (a.Available && b.Available) return null;

        var (side, reason) = !a.Available ? ("A", a.UnavailableReason) : ("B", b.UnavailableReason);

        return (code) => new Category(code, MaximumByKoota[code], null,
            reason ?? Reasons.MoonPositionUnavailable, side,
            $"Not evaluated: the Moon for side {side} is undefined.");
    }

    // --- Classical tables -------------------------------------------------------------------------------------------

    /// <summary>Sign varna, one of 4 (lowest) to 0 (highest): Brahmin 3, Kshatriya 2, Vaishya 1, Shudra 0.</summary>
    private static readonly int[] SignVarna =
    [
        2, 1, 3, 0, 1, 3, 0, 2, 1, 3, 0, 2  // Aries..Pisces: Kshatriya, Vaishya, Brahmin, Shudra, ...
    ];

    /// <summary>Sign vashya class, used only for the equality/adjacency comparison above.</summary>
    private static readonly string[] SignVashya =
    [
        "Chatushpada", "Chatushpada", "Manava", "Jalachara", "Vanachara", "Manava",
        "Manava", "Keeta", "Chatushpada", "Manava", "Jalachara", "Jalachara"
    ];

    private static bool IsAdjacentClass(string a, string b) =>
        (a == "Manava" && b == "Chatushpada") || (a == "Chatushpada" && b == "Manava")
        || (a == "Jalachara" && b == "Manava") || (a == "Manava" && b == "Jalachara");

    /// <summary>Favourable tara: the 3rd, 5th, 7th and 9th are adverse; the rest are counted as favourable here.</summary>
    private static bool IsFavourableTara(int oneBasedTara)
    {
        var tara = ((oneBasedTara - 1) % 9) + 1;
        return tara is not (3 or 5 or 7);
    }

    /// <summary>Yoni per nakshatra, 27 entries in Ashwini order.</summary>
    private static readonly string[] NakshatraYoni =
    [
        "Horse", "Elephant", "Sheep", "Serpent", "Serpent", "Dog", "Cat", "Sheep", "Cat",
        "Rat", "Rat", "Cow", "Buffalo", "Tiger", "Buffalo", "Tiger", "Deer", "Deer",
        "Dog", "Monkey", "Mongoose", "Monkey", "Lion", "Horse", "Lion", "Cow", "Elephant"
    ];

    /// <summary>Yoni enemy pairs; an enemy pair scores 0, an identical pair scores 4, otherwise 2.</summary>
    private static int YoniScore(string a, string b)
    {
        if (a == b) return 4;
        if (IsYoniEnemy(a, b) || IsYoniEnemy(b, a)) return 0;
        return 2;
    }

    private static bool IsYoniEnemy(string a, string b) => (a, b) switch
    {
        ("Cow", "Tiger") or ("Tiger", "Cow") => true,
        ("Elephant", "Lion") or ("Lion", "Elephant") => true,
        ("Horse", "Buffalo") or ("Buffalo", "Horse") => true,
        ("Dog", "Deer") or ("Deer", "Dog") => true,
        ("Serpent", "Mongoose") or ("Mongoose", "Serpent") => true,
        ("Monkey", "Sheep") or ("Sheep", "Monkey") => true,
        ("Cat", "Rat") or ("Rat", "Cat") => true,
        _ => false
    };

    /// <summary>Sign lords, 12 entries in Aries order.</summary>
    private static readonly string[] SignLord =
        ["Mars", "Venus", "Mercury", "Moon", "Sun", "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Saturn", "Jupiter"];

    private enum Relation { Friend, Neutral, Enemy }

    /// <summary>Classical natural friendship: a body is a friend, neutral or enemy of another.</summary>
    private static Relation Friendship(string from, string to)
    {
        if (from == to) return Relation.Friend;

        var friends = FriendsOf(from);
        if (friends.Contains(to, StringComparer.Ordinal)) return Relation.Friend;

        var enemies = EnemiesOf(from);
        return enemies.Contains(to, StringComparer.Ordinal) ? Relation.Enemy : Relation.Neutral;
    }

    private static string[] FriendsOf(string planet) => planet switch
    {
        "Sun" => ["Moon", "Mars", "Jupiter"],
        "Moon" => ["Sun", "Mercury"],
        "Mars" => ["Sun", "Moon", "Jupiter"],
        "Mercury" => ["Sun", "Venus"],
        "Jupiter" => ["Sun", "Moon", "Mars"],
        "Venus" => ["Mercury", "Saturn"],
        "Saturn" => ["Mercury", "Venus"],
        _ => []
    };

    private static string[] EnemiesOf(string planet) => planet switch
    {
        "Sun" => ["Venus", "Saturn"],
        "Moon" => [],
        "Mars" => ["Mercury"],
        "Mercury" => ["Moon"],
        "Jupiter" => ["Mercury", "Venus"],
        "Venus" => ["Sun", "Moon"],
        "Saturn" => ["Sun", "Moon", "Mars"],
        _ => []
    };

    /// <summary>Gana per nakshatra: Deva, Manushya or Rakshasa.</summary>
    private static readonly string[] NakshatraGana =
    [
        "Deva", "Manushya", "Rakshasa", "Manushya", "Deva", "Manushya", "Deva", "Deva", "Rakshasa",
        "Rakshasa", "Manushya", "Manushya", "Deva", "Rakshasa", "Deva", "Rakshasa", "Deva", "Rakshasa",
        "Rakshasa", "Manushya", "Manushya", "Deva", "Rakshasa", "Rakshasa", "Manushya", "Manushya", "Deva"
    ];

    /// <summary>Gana grading: same class 6, benign pairings 5 or 3, the two adverse pairings 0 or 1.</summary>
    private static int GanaScore(string a, string b)
    {
        if (a == b) return 6;

        var pair = new[] { a, b }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return (pair[0], pair[1]) switch
        {
            ("Deva", "Manushya") => 5,
            ("Deva", "Rakshasa") => 0,
            ("Manushya", "Rakshasa") => 1,
            _ => 3
        };
    }

    /// <summary>Nadi per nakshatra: Adi, Madhya or Antya, cycling in the classical order.</summary>
    private static readonly string[] NakshatraNadi =
    [
        "Adi", "Madhya", "Antya", "Antya", "Madhya", "Adi", "Adi", "Madhya", "Antya",
        "Antya", "Madhya", "Adi", "Adi", "Madhya", "Antya", "Antya", "Madhya", "Adi",
        "Adi", "Madhya", "Antya", "Antya", "Madhya", "Adi", "Adi", "Madhya", "Antya"
    ];
}
