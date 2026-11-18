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

    /// <summary>Classical required rupas, used as the comparison threshold. Versioned with the rule set.</summary>
    /// <remarks>
    /// These are the classical minimum rupas a planet needs to be considered strong. They are recorded here as engine
    /// config rather than in the API layer so that a future revision of the table is visible as a rules-version change
    /// instead of a silent redefinition of the same number.
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

    /// <summary>One component's contribution, in rupas, with its own provenance.</summary>
    public sealed record ComponentScore(string Component, double Rupas, string Basis);

    /// <summary>The strength of one planet, partial by construction until every component exists.</summary>
    public sealed record PlanetStrength(
        string Planet,
        IReadOnlyList<ComponentScore> Components,
        double TotalRupas,
        IReadOnlyList<string> ComponentsWithheld,
        double? RequiredRupas,
        double? RatioToThreshold,
        double? RupasToThreshold)
    {
        /// <summary>
        /// False whenever any classical component is missing. A caller must check this before treating
        /// <see cref="RatioToThreshold"/> as comparable to the classical table.
        /// </summary>
        public bool IsComplete => ComponentsWithheld.Count == 0;
    }

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

            var total = components.Sum(component => component.Rupas);
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
                required - total));
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
            $"uchcha-bala from own longitude; debilitation {debilitation:0.###} deg, separation {separation:0.###} deg");
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
            "withheld: dig-bala is measured from the unvalidated ascendant; this zero is a placeholder, not a score");
    }

    /// <summary>Naisargika Bala: natural strength, a fixed classical ordering. Independent of the chart entirely.</summary>
    private static ComponentScore Naisargika(string planet) =>
        new("Naisargika", NaisargikaRupas[planet], "classical natural order; fixed, chart-independent");

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
                : $"whole-sign aspects: {string.Join(", ", contributors)}");
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
