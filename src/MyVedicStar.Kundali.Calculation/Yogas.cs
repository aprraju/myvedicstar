namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Yoga and dosha assessment: named classical combinations, evaluated against a chart, each reporting its own verdict.
/// </summary>
/// <remarks>
/// <para><b>The defining constraint.</b> Present and withheld assessments coexist in one payload, and each carries its
/// own completeness and its own reason. There is deliberately no section-level "these results are partial" flag: a single
/// footer would let a caller skim a list of satisfied yogas and never learn that three others were never even tested.</para>
/// <para><b>Two different absences that must never be conflated.</b> A yoga that was <i>evaluated and failed</i> is a
/// finished claim about the chart - the combination is not present. A yoga that was <i>never evaluated</i> is not a claim
/// at all: the inputs it needs were unavailable. They look identical in a list of satisfied yogas, so they are separated
/// explicitly by <see cref="YogaStatus"/> and every unevaluated one is enumerated by name with its reason.</para>
/// <para><b>What is claimed.</b> The evaluation rules are implemented and tested as rules. No claim is made that the
/// catalogue is complete, nor that a verdict matches any published software - that needs an independent reference chart,
/// which does not exist for these combinations.</para>
/// </remarks>
public static class Yogas
{
    /// <summary>Identifies the exact rule catalogue. Persisted with every result so a catalogue change is visible.</summary>
    public const string RulesVersion = "yogas-classical-1";

    /// <summary>
    /// Verdict for one combination. <c>indeterminate</c> means the rule was not evaluated at all - never "probably not",
    /// and never a soft no.
    /// </summary>
    public static class Statuses
    {
        /// <summary>Every required condition held, and no cancellation applied.</summary>
        public const string Satisfied = "satisfied";

        /// <summary>Evaluated, and at least one required condition did not hold.</summary>
        public const string NotSatisfied = "not-satisfied";

        /// <summary>Not evaluated: a required input was unavailable. This is an absence of a claim, not a negative one.</summary>
        public const string Indeterminate = "indeterminate";
    }

    /// <summary>Where a finding applies. Transit-scoped findings are never mixed into a natal section.</summary>
    public static class Scopes
    {
        public const string Natal = "natal";
        public const string Transit = "transit";
    }

    /// <summary>One condition inside a rule, with the fact it was tested against.</summary>
    public sealed record Condition(string Text, bool Truth);

    /// <summary>
    /// One assessed combination.
    /// </summary>
    /// <param name="Completeness">Per-assessment, never section-level. False means some part of this rule could not be
    /// tested even though a verdict was produced - e.g. the rule ran from positions but its cancellation test needs a
    /// house frame.</param>
    /// <param name="Reason">Populated only for <c>indeterminate</c>. A satisfied or failed assessment has no reason: the
    /// conditions are the explanation.</param>
    public sealed record Assessment(
        string Code,
        string Name,
        string Scope,
        string Status,
        IReadOnlyList<Condition> Conditions,
        IReadOnlyList<string> Supporting,
        IReadOnlyList<string> Opposing,
        IReadOnlyList<string> Exceptions,
        IReadOnlyList<string> Cancellations,
        bool Complete,
        string? Reason,
        IReadOnlyList<string> UntestedComponents);

    /// <summary>The whole section: satisfied, failed and unevaluated together, plus what the caller can trust.</summary>
    public sealed record Result(
        string RulesVersion,
        IReadOnlyList<Assessment> Assessments,
        IReadOnlyList<Assessment> Indeterminate);

    /// <summary>
    /// Evaluates every rule in the catalogue. <paramref name="signByGraha"/> carries the grahas the engine actually
    /// published; a rule whose inputs are missing comes back <c>indeterminate</c> rather than being skipped.
    /// </summary>
    public static Result Evaluate(IReadOnlyDictionary<string, int> signByGraha)
    {
        ArgumentNullException.ThrowIfNull(signByGraha);

        var all = new List<Assessment>();
        foreach (var rule in Catalogue)
            all.Add(rule(signByGraha));

        return new Result(RulesVersion, all.Where(a => a.Status != Statuses.Indeterminate).ToArray(),
            all.Where(a => a.Status == Statuses.Indeterminate).ToArray());
    }

    /// <summary>
    /// The catalogue. Each entry is a rule that either produces a verdict or reports exactly which inputs it lacked.
    /// </summary>
    /// <remarks>
    /// Every rule here is <b>position-only</b>: it needs sign placements, which the engine can supply for all nine
    /// grahas. Rules needing houses, cusps or a validated ascendant are deliberately absent rather than approximated,
    /// because a house-dependent yoga evaluated from whole signs alone would be a different quantity.
    /// </remarks>
    /// <summary>Every code the catalogue can report, so a caller can enumerate what exists without running anything.</summary>
    public static IReadOnlyList<string> CatalogueCodes { get; } =
        ["gaja-kesari", "kemadruma", "chandra-mangala", "budha-aditya", "manglik", "kaal-sarp"];

    private static readonly IReadOnlyList<Func<IReadOnlyDictionary<string, int>, Assessment>> Catalogue =
    [
        GajaKesari,
        Kemadruma,
        ChandraMangala,
        BudhaAditya,
        Manglik,
        KaalSarp
    ];

    /// <summary>
    /// Gaja Kesari: Jupiter in a kendra (1/4/7/10) from the Moon.
    /// </summary>
    /// <remarks>Position-only. Needs only the Moon and Jupiter, and no house frame - the kendra is counted from the Moon's
    /// sign, not from the ascendant.</remarks>
    private static Assessment GajaKesari(IReadOnlyDictionary<string, int> signByGraha)
    {
        const string Code = "gaja-kesari";
        const string Name = "Gaja Kesari Yoga";

        if (!TrySigns(signByGraha, ["Moon", "Jupiter"], out var signs, out var missing))
            return Indeterminate(Code, Name, missing, "Moon and Jupiter");

        var moon = signs["Moon"];
        var house = ((signs["Jupiter"] - moon + 12) % 12) + 1;
        var inKendra = house is 1 or 4 or 7 or 10;

        return new Assessment(Code, Name, Scopes.Natal,
            inKendra ? Statuses.Satisfied : Statuses.NotSatisfied,
            [new Condition($"Jupiter occupies a kendra from the Moon (found {house}th)", inKendra)],
            inKendra ? [$"Jupiter {house}th from Moon"] : [],
            inKendra ? [] : [$"Jupiter {house}th from Moon, not a kendra"],
            [], [],
            Complete: true, Reason: null, UntestedComponents: []);
    }

    /// <summary>
    /// Kemadruma: no graha in the 2nd or 12th from the Moon, and none conjoined with it.
    /// </summary>
    /// <remarks>
    /// Position-only, but it needs <b>every</b> graha, because the claim is about absence - a graha the engine did not
    /// publish could be sitting in the 2nd from the Moon. So it is evaluated only when all seven classical grahas are
    /// present, and reports precisely which ones it was missing otherwise.
    /// </remarks>
    private static Assessment Kemadruma(IReadOnlyDictionary<string, int> signByGraha)
    {
        const string Code = "kemadruma";
        const string Name = "Kemadruma Yoga";

        string[] required = ["Moon", "Sun", "Mars", "Mercury", "Jupiter", "Venus", "Saturn"];
        if (!TrySigns(signByGraha, required, out var signs, out var missing))
            return Indeterminate(Code, Name, missing, "all seven classical grahas (the claim is an absence)");

        var moon = signs["Moon"];
        var occupied = required
            .Where(graha => graha != "Moon")
            .Where(graha =>
            {
                var house = ((signs[graha] - moon + 12) % 12) + 1;
                return house is 2 or 12;
            })
            .ToArray();

        var noneAdjacent = occupied.Length == 0;

        return new Assessment(Code, Name, Scopes.Natal,
            noneAdjacent ? Statuses.Satisfied : Statuses.NotSatisfied,
            [new Condition("No graha in the 2nd or 12th from the Moon", noneAdjacent)],
            noneAdjacent ? ["2nd and 12th from Moon are unoccupied"] : [],
            noneAdjacent ? [] : ["Occupied: " + string.Join(", ", occupied)],
            [], [],
            Complete: true, Reason: null, UntestedComponents: []);
    }

    /// <summary>Chandra Mangala: Moon and Mars conjoined in one sign.</summary>
    /// <remarks>Position-only, two grahas, no house frame.</remarks>
    private static Assessment ChandraMangala(IReadOnlyDictionary<string, int> signByGraha)
    {
        const string Code = "chandra-mangala";
        const string Name = "Chandra Mangala Yoga";

        if (!TrySigns(signByGraha, ["Moon", "Mars"], out var signs, out var missing))
            return Indeterminate(Code, Name, missing, "Moon and Mars");

        var conjoined = signs["Moon"] == signs["Mars"];

        return new Assessment(Code, Name, Scopes.Natal,
            conjoined ? Statuses.Satisfied : Statuses.NotSatisfied,
            [new Condition("Moon and Mars occupy the same sign", conjoined)],
            conjoined ? ["Moon and Mars conjoined"] : [],
            conjoined ? [] : ["Moon and Mars are in different signs"],
            [], [],
            Complete: true, Reason: null, UntestedComponents: []);
    }

    /// <summary>Budha Aditya: Sun and Mercury conjoined in one sign.</summary>
    /// <remarks>Position-only. Note that Mercury is never far from the Sun, so this is common; it is still a real
    /// combination and the verdict is reported as such rather than weighted.</remarks>
    private static Assessment BudhaAditya(IReadOnlyDictionary<string, int> signByGraha)
    {
        const string Code = "budha-aditya";
        const string Name = "Budha Aditya Yoga";

        if (!TrySigns(signByGraha, ["Sun", "Mercury"], out var signs, out var missing))
            return Indeterminate(Code, Name, missing, "Sun and Mercury");

        var conjoined = signs["Sun"] == signs["Mercury"];

        return new Assessment(Code, Name, Scopes.Natal,
            conjoined ? Statuses.Satisfied : Statuses.NotSatisfied,
            [new Condition("Sun and Mercury occupy the same sign", conjoined)],
            conjoined ? ["Sun and Mercury conjoined"] : [],
            conjoined ? [] : ["Sun and Mercury are in different signs"],
            [], [],
            Complete: true, Reason: null, UntestedComponents: []);
    }

    /// <summary>
    /// Manglik: Mars in 1, 2, 4, 7, 8 or 12 <b>from the ascendant</b>.
    /// </summary>
    /// <remarks>
    /// <b>Never evaluated today, and deliberately so.</b> This is the owner-approved Lagna-only definition, and it needs a
    /// validated ascendant, which this engine withholds. The alternative - counting from the Moon instead - is a
    /// <i>different</i> definition, not a fallback, and presenting it under this name would be the exact substitution the
    /// module forbids. So it is enumerated as indeterminate with its reason, so a caller sees the claim exists and that we
    /// have not made it.
    /// </remarks>
    private static Assessment Manglik(IReadOnlyDictionary<string, int> signByGraha)
    {
        _ = signByGraha;
        return new Assessment("manglik", "Manglik Dosha", Scopes.Natal, Statuses.Indeterminate,
            [], [], [], [], [],
            Complete: false,
            Reason: KundaliCalculationReasons.UnvalidatedReference,
            UntestedComponents: ["Ascendant (house frame)"]);
    }

    /// <summary>
    /// Kaal Sarp: all seven classical grahas on one side of the Rahu-Ketu axis.
    /// </summary>
    /// <remarks>
    /// Position-only in principle, and it needs all nine grahas including both nodes. Reported indeterminate while any of
    /// them is unpublished, because a single graha on the other side of the axis falsifies the yoga and we cannot see it.
    /// </remarks>
    private static Assessment KaalSarp(IReadOnlyDictionary<string, int> signByGraha)
    {
        const string Code = "kaal-sarp";
        const string Name = "Kaal Sarp Yoga";

        string[] required = ["Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn", "Rahu", "Ketu"];
        if (!TrySigns(signByGraha, required, out var signs, out var missing))
            return Indeterminate(Code, Name, missing, "all nine grahas including both nodes");

        // The axis divides the zodiac into two arcs. Every graha must fall in the same one.
        var rahu = signs["Rahu"];
        var satu = required.Where(graha => graha != "Rahu" && graha != "Ketu")
            .Select(graha => ((signs[graha] - rahu + 12) % 12))
            .ToArray();
        var inFirstHalf = satu.All(offset => offset < 6);
        var inSecondHalf = satu.All(offset => offset >= 6);
        var allOneSide = inFirstHalf || inSecondHalf;

        return new Assessment(Code, Name, Scopes.Natal,
            allOneSide ? Statuses.Satisfied : Statuses.NotSatisfied,
            [new Condition("All seven classical grahas lie on one side of the Rahu-Ketu axis", allOneSide)],
            allOneSide ? ["All grahas on one side of the nodal axis"] : [],
            allOneSide ? [] : ["Grahas fall on both sides of the nodal axis"],
            [], [],
            Complete: true, Reason: null, UntestedComponents: []);
    }

    /// <summary>Pulls the signs a rule needs, reporting exactly which were missing rather than a boolean.</summary>
    private static bool TrySigns(IReadOnlyDictionary<string, int> signByGraha, string[] required,
        out Dictionary<string, int> signs, out IReadOnlyList<string> missing)
    {
        signs = new Dictionary<string, int>(StringComparer.Ordinal);
        var gaps = new List<string>();

        foreach (var graha in required)
        {
            if (signByGraha.TryGetValue(graha, out var sign))
                signs[graha] = sign;
            else
                gaps.Add(graha);
        }

        missing = gaps;
        return gaps.Count == 0;
    }

    private static Assessment Indeterminate(string code, string name, IReadOnlyList<string> missing, string needs) =>
        new(code, name, Scopes.Natal, Statuses.Indeterminate, [], [], [], [], [],
            Complete: false,
            Reason: KundaliCalculationReasons.UnvalidatedReference,
            UntestedComponents: missing.Select(graha => $"Placement of {graha}").ToArray());
}
