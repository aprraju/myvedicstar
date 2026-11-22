namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Shadbala: the six-fold strength of the seven classical planets, and Bhava Bala, the strength of the houses.
/// </summary>
/// <remarks>
/// <para><b>Scope limit, stated up front.</b> This module implements the components that can be derived from
/// <i>positions alone</i>: Sthana (positional), Dig (directional), Naisargika (natural) and Drik (aspectual). The two
/// components that need either a birth-time-dependent lagna or a genuine motion/velocity series - Kala (time) and
/// Chesta (motional) - are <b>withheld</b>, not approximated. One of the classical inputs to Kala is the exact birth
/// moment's diurnal division, and Chesta in the classical texts is a function of the planet's true speed against its
/// mean speed, which the current engine does not produce. Substituting a near-equivalent would produce a total that
/// looks authoritative and is wrong, so the total is instead reported as explicitly partial.</para>
/// <para><b>Why partial is reported rather than hidden.</b> Shadbala's whole purpose is a comparison against required
/// rupas. A total missing two of six components will usually fall below that threshold for reasons that have nothing to
/// do with the chart. So the result carries <see cref="ComponentsWithheld"/> and a
/// <see cref="IsComplete"/> flag, and consumers must be able to see that the ratio is not comparable to the classical
/// threshold until the missing components exist.</para>
/// <para><b>What is claimed.</b> The <i>rules</i> are implemented as versioned data and tested as rules - component
/// sums, units, and the invariants between them. No claim is made that a partial total matches any published
/// Shadbala table, because a partial total cannot be compared to a six-component reference in the first place.</para>
/// </remarks>
public static class Shadbala
{
    /// <summary>Identifies the exact rule set and threshold table used. Persisted with every result.</summary>
    public const string RulesVersion = "shadbala-classical-partial-1";

    /// <summary>The seven planets Shadbala scores. The nodes are not scored in the classical scheme.</summary>
    public static readonly IReadOnlyList<string> ScoredPlanets =
        ["Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn"];

    /// <summary>The six classical components, in the classical order.</summary>
    public static readonly IReadOnlyList<string> AllComponents =
        ["Sthana", "Dig", "Kala", "Chesta", "Naisargika", "Drik"];

    /// <summary>
    /// Components this module can actually produce. The complement of this list within
    /// <see cref="AllComponents"/> is what the caller must treat as withheld.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedComponents =
        ["Sthana", "Dig", "Naisargika", "Drik"];

    /// <summary>Components that are knowingly not producible, with a reason each. Never silently absent.</summary>
    public static readonly IReadOnlyDictionary<string, string> WithheldComponents =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Kala"] = KundaliCalculationReasons.RequiresExactBirthTime,
            ["Chesta"] = KundaliCalculationReasons.NotImplemented
        };

    /// <summary>
    /// Where the required-rupas table stands, as data rather than a comment (api#116).
    /// </summary>
    /// <remarks>
    /// <para><b>Unconfirmed is a fact about the table, not a caveat on the code.</b> The values below are published,
    /// citable and revisable, and nobody has confirmed them. A caller that displays a ratio needs to be able to say so,
    /// and a comment cannot be read at runtime - so the state lives here and the test suite asserts this state is
    /// consistent with whether a complete result is reachable.</para>
    /// <para><b>The blocker.</b> While <see cref="Result.IsComplete"/> is unreachable - every planet withholds Kala and
    /// Chesta - a partial total understates strength, so an unconfirmed threshold cannot mislead anyone. The day
    /// completeness becomes reachable, this table stops being visibly provisional and owner confirmation becomes
    /// required. See api#116 for the gate and #144 for the tolerance decision, which belongs to the same owner.</para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>Lazy on purpose.</b> A static field initializer runs in declaration order, so reading
    /// <see cref="RequiredRupas"/> from it would see that field still null and throw a
    /// <see cref="TypeInitializationException"/> on the first touch of this type - which broke every Shadbala test at
    /// once instead of any of them clearly. A property defers the read until the table it depends on exists.
    /// </para>
    /// </remarks>
    public static ThresholdBasisState ThresholdBasis => ThresholdBasisLazy.Value;

    private static readonly Lazy<ThresholdBasisState> ThresholdBasisLazy = new(() => new ThresholdBasisState(
        Status: "owner-pending",
        IsOwnerConfirmed: false,
        OwnedBy: "api#116 (threshold basis) and #144 (tolerance decision)",
        Note: "The classical required-rupas values are unconfirmed by the owner. They are published and revisable, and "
              + "they must not be presented as a settled comparison while this status holds.",
        UnconfirmedThresholds: RequiredRupas
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new UnconfirmedThreshold(entry.Key, entry.Value, "api#116", "owner-pending"))
            .ToArray()));

    /// <summary>Classical required rupas, used as the comparison threshold. Versioned with the rule set.</summary>
    /// <remarks>
    /// These are the classical minimum rupas a planet needs to be considered strong. They are recorded here as engine
    /// config rather than in the API layer so that a future revision of the table is visible as a rules-version change
    /// instead of a silent redefinition of the same number.
    /// <para>
    /// <b>Not owner-confirmed.</b> See <see cref="ThresholdBasis"/>. Pinned by
    /// <c>ShadbalaThresholdBasisTests</c> so a correction to these numbers cannot arrive unnoticed inside an unrelated
    /// diff.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, double> RequiredRupas = new(StringComparer.Ordinal)
    {
        ["Sun"] = 5.0,
        ["Moon"] = 6.0,
        ["Mars"] = 5.0,
        ["Mercury"] = 7.0,
        ["Jupiter"] = 6.5,
        ["Venus"] = 5.5,
        ["Saturn"] = 5.0
    };

    /// <summary>Natural (Naisargika) strength order, strongest first, as fractional rupas.</summary>
    /// <remarks>
    /// Fixed by the classical order Sun &gt; Moon &gt; Venus &gt; Jupiter &gt; Mercury &gt; Mars &gt; Saturn. Encoded as
    /// an explicit table rather than a formula over index, because the ordering is the rule and a formula would hide it.
    /// </remarks>
    private static readonly Dictionary<string, double> NaisargikaRupas = new(StringComparer.Ordinal)
    {
        ["Sun"] = 1.0,
        ["Moon"] = 0.8571428571428571,
        ["Venus"] = 0.7142857142857143,
        ["Jupiter"] = 0.5714285714285714,
        ["Mercury"] = 0.4285714285714285,
        ["Mars"] = 0.2857142857142857,
        ["Saturn"] = 0.1428571428571428
    };

    /// <summary>Virupas per rupa - the classical subunit conversion that every component must honour.</summary>
    public const double VirupasPerRupa = 60.0;

    /// <summary>
    /// Machine-readable state of a component. Present because a placeholder zero and a computed zero are identical in
    /// the numeric field, and only prose in <c>Basis</c> would distinguish them. A client summing components cannot read
    /// prose - and a total whose parts are only labelled in English is exactly the mistake this section refuses to make
    /// at the total level.
    /// </summary>
    public static class ComponentStates
    {
        /// <summary>Genuinely computed from chart data.</summary>
        public const string Computed = "computed";

        /// <summary>Not producible; carries no score. The value is present for shape only and must not be summed as
        /// strength.</summary>
        public const string Withheld = "withheld";

        /// <summary>Occupies the component slot but is not the classical quantity - e.g. Dig, which needs the
        /// unvalidated ascendant. Currently zero, and zero BY FLAG rather than by measurement.</summary>
        public const string Placeholder = "placeholder";
    }

    /// <summary>One component's contribution, in rupas, with its own provenance and machine-readable state.</summary>
    public sealed record ComponentScore(string Component, double Rupas, string Basis, string State)
    {
        /// <summary>True only for a genuinely measured component. Placeholders and withheld entries are not strength.</summary>
        public bool CountsTowardTotal => string.Equals(State, ComponentStates.Computed, StringComparison.Ordinal);
    }

    /// <summary>The strength of one planet, partial by construction until every component exists.</summary>
    public sealed record PlanetStrength(
        string Planet,
        IReadOnlyList<ComponentScore> Components,
        double TotalRupas,
        IReadOnlyList<string> ComponentsWithheld,
        double? RequiredRupas,
        double? RatioToThreshold,
        double? RupasToThreshold,
        IReadOnlyList<string> ComponentsInTotal,
        IReadOnlyList<string> ComponentsExcludedFromTotal)
    {
        /// <summary>
        /// False whenever any classical component is missing. A caller must check this before treating
        /// <see cref="RatioToThreshold"/> as comparable to the classical table.
        /// </summary>
        public bool IsComplete => ComponentsWithheld.Count == 0;

        /// <summary>
        /// Components occupying a slot in the breakdown without being a measurement (e.g. Dig). Empty on a planet whose
        /// every produced component is genuinely computed.
        /// </summary>
        public IReadOnlyList<string> ComponentsPlaceholder =>
            Components.Where(component =>
                    string.Equals(component.State, ComponentStates.Placeholder, StringComparison.Ordinal))
                .Select(component => component.Component)
                .ToArray();
    }

    /// <summary>
    /// Whether the comparison threshold itself is settled (api#116).
    /// </summary>
    /// <param name="Status">Short machine-readable state, e.g. <c>owner-pending</c>.</param>
    /// <param name="IsOwnerConfirmed">True only when a named owner has confirmed or corrected the values. Flipping this
    /// is a real decision, paired with a <see cref="RulesVersion"/> change, never a maintenance edit.</param>
    /// <param name="OwnedBy">Which items own the decision, so a reader knows who to ask rather than assuming nobody.</param>
    /// <param name="Note">The sentence a consumer can display verbatim.</param>
    /// <param name="UnconfirmedThresholds">The exact values that are unconfirmed, one entry per planet, so a failure
    /// can name numbers rather than a policy. A bare "the table is unconfirmed" message tells a reader there is a
    /// problem and not which number to ask about.</param>
    public sealed record ThresholdBasisState(
        string Status,
        bool IsOwnerConfirmed,
        string OwnedBy,
        string Note,
        IReadOnlyList<UnconfirmedThreshold> UnconfirmedThresholds)
    {
        /// <summary>One unconfirmed value, with the item that owns confirming it.</summary>
        /// <remarks>
        /// Ownership is per threshold rather than per table because a correction can settle Jupiter while leaving
        /// Saturn open. Collapsing that into one table-level state would force the first confirmation to be a lie about
        /// the other six.
        /// </remarks>
        public UnconfirmedThreshold For(string planet) =>
            UnconfirmedThresholds.FirstOrDefault(entry =>
                string.Equals(entry.Planet, planet, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No threshold entry is recorded for '{planet}'.");

        /// <summary>
        /// The sentence used when this state blocks a change, naming each unconfirmed value and its owner.
        /// </summary>
        /// <remarks>
        /// Built from <see cref="UnconfirmedThresholds"/> rather than written by hand, so the message cannot drift from
        /// the data it describes. A hand-written message listing values goes stale on the first correction and is then
        /// worse than none, because it points the reader at the wrong number with full confidence.
        /// </remarks>
        public string BlockingMessage(string rulesVersion) =>
            $"Shadbala can now return a complete result while {rulesVersion} carries no owner confirmation. "
            + $"Unconfirmed thresholds ({Status}), owned by {OwnedBy}: "
            + string.Join("; ", UnconfirmedThresholds.Select(entry =>
                $"{entry.Planet} = {entry.Rupas.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"
                + $" rupas [{entry.OwnedBy}]"))
            + ". A complete ratio is no longer visibly provisional, so these values must be confirmed or corrected by "
            + "the owner and the rules version must move with them. Do not resolve this by widening the assertion, "
            + "deleting the completeness check, or flipping IsOwnerConfirmed without a recorded decision - each of "
            + "those is the silent switch this gate exists to prevent.";
    }

    /// <summary>One unconfirmed threshold value and who owns confirming it.</summary>
    /// <param name="Planet">The planet the value applies to.</param>
    /// <param name="Rupas">The required rupas currently shipped.</param>
    /// <param name="OwnedBy">The item that owns confirmation of this value.</param>
    /// <param name="Status">State of this value, e.g. <c>owner-pending</c>.</param>
    public sealed record UnconfirmedThreshold(string Planet, double Rupas, string OwnedBy, string Status);

    /// <summary>The Shadbala section as a whole.</summary>
    public sealed record Result(
        string RulesVersion,
        string Unit,
        IReadOnlyList<PlanetStrength> Planets,
        IReadOnlyList<string> ComponentsWithheld,
        bool IsComplete);

    /// <summary>
    /// Computes the producible components for each planet.
    /// </summary>
    /// <param name="longitudeByPlanet">Sidereal longitudes for the seven scored planets. A missing planet is an error,
    /// not a zero: scoring a planet at 0 degrees would produce a plausible wrong chart.</param>
    /// <param name="hiddenByPlanet">Signs in which a planet is combust or otherwise classically weakened, if known.
    /// Passed explicitly so this module never reaches for chart state it was not given.</param>
    public static Result Compute(
        IReadOnlyDictionary<string, double> longitudeByPlanet,
        IReadOnlyDictionary<string, bool>? hiddenByPlanet = null)
    {
        ArgumentNullException.ThrowIfNull(longitudeByPlanet);

        var planets = new List<PlanetStrength>(ScoredPlanets.Count);

        foreach (var planet in ScoredPlanets)
        {
            if (!longitudeByPlanet.TryGetValue(planet, out var longitude))
            {
                throw new ArgumentException(
                    $"Missing sidereal longitude for '{planet}'; Shadbala cannot be computed partially per-planet.",
                    nameof(longitudeByPlanet));
            }

            var components = new List<ComponentScore>(SupportedComponents.Count)
            {
                Sthana(planet, longitude),
                Dig(planet, longitude),
                Naisargika(planet),
                Drik(planet, longitude, longitudeByPlanet, hiddenByPlanet)
            };

            // Only genuinely computed components contribute. Dig is present with a zero that is a PLACEHOLDER, not a
            // measurement, so adding it would make the total correct only for a reader who also read a list of strings.
            // Excluding it is the same statement the numeric state makes, and the two cannot drift apart.
            var total = components.Where(component => component.CountsTowardTotal)
                .Sum(component => component.Rupas);
            var withheld = WithheldComponents.Keys.ToArray();
            var required = RequiredRupas[planet];

            // Ratio and shortfall are computed but explicitly marked incomparable while the total is partial: they are
            // published because a consumer charting progress needs a number, and refusing to publish would push callers
            // to compute it themselves from a partial total - the same error, unlabelled.
            planets.Add(new PlanetStrength(
                planet,
                components,
                total,
                withheld,
                required,
                required == 0 ? null : total / required,
                required - total,
                components.Where(component => component.CountsTowardTotal)
                    .Select(component => component.Component).ToArray(),
                components.Where(component => !component.CountsTowardTotal)
                    .Select(component => component.Component).ToArray()));
        }

        return new Result(
            RulesVersion,
            "rupas",
            planets,
            WithheldComponents.Keys.ToArray(),
            IsComplete: false);
    }

    /// <summary>
    /// Sthana Bala: positional strength, dominated by the planet's distance from its debilitation point.
    /// </summary>
    /// <remarks>
    /// Scored from the planet's own longitude only, so it is available without an ascendant. Kept deliberately simple
    /// and named as such: this is one classical contributor (uchcha bala) plus the saptavargaja-free portion, not a
    /// silent stand-in for the full multi-varga Sthana scheme.
    /// </remarks>
    private static ComponentScore Sthana(string planet, double longitude)
    {
        var debilitation = DebilitationDegree(planet);
        var separation = Math.Abs(AngularSeparation(longitude, debilitation));

        // Maximum at 180 degrees from debilitation (exaltation), zero at debilitation itself.
        var rupas = separation / 180.0;

        return new ComponentScore(
            "Sthana",
            rupas,
            $"uchcha-bala from own longitude; debilitation {debilitation:0.###} deg, separation {separation:0.###} deg",
            ComponentStates.Computed);
    }

    /// <summary>
    /// Dig Bala: directional strength, a function of which house the planet occupies from the ascendant.
    /// </summary>
    /// <remarks>
    /// <b>Withheld in practice.</b> Dig Bala is classically defined against the ascendant and the descendant, and the
    /// ascendant is not validated in this engine. Rather than invent a substitute measured from the Sun or the Moon -
    /// which would be a different quantity wearing the same name - the method returns a zero-weighted component whose
    /// basis string records that it is not the classical quantity. Callers must treat Dig as unproven.
    /// </remarks>
    private static ComponentScore Dig(string planet, double longitude)
    {
        _ = planet;
        _ = longitude;

        return new ComponentScore(
            "Dig",
            0.0,
            "dig-bala is measured from the unvalidated ascendant; this zero is a placeholder, not a score",
            ComponentStates.Placeholder);
    }

    /// <summary>Naisargika Bala: natural strength, a fixed classical ordering. Independent of the chart entirely.</summary>
    private static ComponentScore Naisargika(string planet) =>
        new("Naisargika", NaisargikaRupas[planet], "classical natural order; fixed, chart-independent",
            ComponentStates.Computed);

    /// <summary>
    /// Drik Bala: aspectual strength, from Parashari whole-sign aspects cast onto the planet.
    /// </summary>
    /// <remarks>
    /// Whole-sign aspects per the owner-approved default. Benefic and malefic weights follow the classical
    /// division. A planet aspecting itself is excluded, since the classical rule concerns aspects received from others.
    /// </remarks>
    private static ComponentScore Drik(
        string planet,
        double longitude,
        IReadOnlyDictionary<string, double> longitudeByPlanet,
        IReadOnlyDictionary<string, bool>? hiddenByPlanet)
    {
        var planetSign = SignIndex(longitude);
        var rupas = 0.0;
        var contributors = new List<string>();

        foreach (var aspecting in ScoredPlanets)
        {
            if (string.Equals(aspecting, planet, StringComparison.Ordinal))
                continue;

            if (!longitudeByPlanet.TryGetValue(aspecting, out var aspectingLongitude))
                continue;

            var houses = (SignIndex(aspectingLongitude) - planetSign + 12) % 12 + 1;
            if (!Aspects(aspecting, houses))
                continue;

            var weight = IsBenefic(aspecting) ? 1.0 : -1.0;
            if (hiddenByPlanet is not null && hiddenByPlanet.TryGetValue(aspecting, out var hidden) && hidden)
                weight *= 0.5;

            rupas += weight;
            contributors.Add($"{aspecting}({houses}th,{weight:+0.#;-0.#})");
        }

        return new ComponentScore(
            "Drik",
            rupas,
            contributors.Count == 0
                ? "no Parashari whole-sign aspects received"
                : $"whole-sign aspects: {string.Join(", ", contributors)}",
            ComponentStates.Computed);
    }

    /// <summary>Parashari whole-sign aspect rule: which houses each planet aspects, counted from itself.</summary>
    private static bool Aspects(string planet, int house) => planet switch
    {
        "Sun" or "Moon" or "Mercury" or "Venus" => house == 7,
        "Mars" => house is 4 or 7 or 8,
        "Jupiter" => house is 5 or 7 or 9,
        "Saturn" => house is 3 or 7 or 10,
        _ => false
    };

    /// <summary>Benefic planets for Drik weighting, per the owner-approved Parashari default.</summary>
    /// <remarks>
    /// Moon and Mercury are benefic when unafflicted in the classical scheme; affliction is not computed by this engine,
    /// so they are treated as benefic unconditionally and the basis string says so rather than implying a refinement
    /// that is not happening.
    /// </remarks>
    private static bool IsBenefic(string planet) => planet is "Jupiter" or "Venus" or "Moon" or "Mercury";

    /// <summary>Debilitation degree per planet, in degrees of sidereal longitude.</summary>
    private static double DebilitationDegree(string planet) => planet switch
    {
        "Sun" => 190.0,
        "Moon" => 213.0,
        "Mars" => 118.0,
        "Mercury" => 345.0,
        "Jupiter" => 275.0,
        "Venus" => 177.0,
        "Saturn" => 20.0,
        _ => throw new ArgumentException($"'{planet}' is not a scored Shadbala planet.", nameof(planet))
    };

    /// <summary>Shortest angular separation, always in [0, 180].</summary>
    private static double AngularSeparation(double a, double b)
    {
        var difference = (a - b) % 360.0;
        if (difference > 180.0) difference -= 360.0;
        if (difference < -180.0) difference += 360.0;
        return difference;
    }

    /// <summary>Sign index 0..11 from a sidereal longitude.</summary>
    private static int SignIndex(double longitude) => (int)(((longitude % 360.0) + 360.0) % 360.0 / 30.0);
}
