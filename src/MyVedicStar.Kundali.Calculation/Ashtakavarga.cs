namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Ashtakavarga: per-planet (bhinnashtakavarga) and aggregate (sarvashtakavarga) benefic-point tables.
/// </summary>
/// <remarks>
/// <para><b>Scope limit, stated up front.</b> The contribution tables below are the classical Parashari benefic-place
/// tables, encoded as data. They are versioned and cited because a wrong table produces plausible-looking numbers -
/// which is the most dangerous kind of error in an interpretation-heavy system.</para>
/// <para>The defining structural invariant is that the two levels agree: for every sign, the sum of the eight bhinnas
/// equals the sarva value. A mismatch is a calculation defect, not a presentation issue, so it is asserted rather than
/// tolerated.</para>
/// <para>No claim is made that these tables match a published ephemeris or any particular software. They are the
/// classical rules expressed as data and tested as rules.</para>
/// </remarks>
public static class Ashtakavarga
{
    /// <summary>Identifies the exact table set used. Persisted with every result so a later table change is visible.</summary>
    public const string RulesVersion = "ashtakavarga-parashari-1";

    /// <summary>The seven planets that contribute bindus. The nodes do not, and the Sun and Moon are not scored.</summary>
    public static readonly IReadOnlyList<string> ScoredPlanets =
        ["Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn"];

    /// <summary>
    /// Bhinnashtakavarga tables: for each scored planet, the houses counted from each of the eight reference points
    /// (the seven planets plus the ascendant) that receive a bindu.
    /// </summary>
    /// <remarks>
    /// Houses are one-based offsets from the reference sign, matching the classical presentation (1st = same sign).
    /// Storing them as house offsets rather than absolute signs keeps the table independent of any chart.
    /// </remarks>
    private static readonly Dictionary<string, Dictionary<string, int[]>> Tables = new(StringComparer.Ordinal)
    {
        ["Sun"] = new(StringComparer.Ordinal)
        {
            ["Sun"] = [1, 2, 4, 7, 8, 9, 10, 11],
            ["Moon"] = [3, 6, 10, 11],
            ["Mars"] = [1, 2, 4, 7, 8, 9, 10, 11],
            ["Mercury"] = [3, 5, 6, 9, 10, 11, 12],
            ["Jupiter"] = [5, 6, 9, 11],
            ["Venus"] = [6, 7, 12],
            ["Saturn"] = [1, 2, 4, 7, 8, 9, 10, 11],
            ["Ascendant"] = [3, 4, 6, 10, 11, 12]
        },
        ["Moon"] = new(StringComparer.Ordinal)
        {
            ["Sun"] = [3, 6, 7, 8, 10, 11],
            ["Moon"] = [1, 3, 6, 7, 9, 10, 11],
            ["Mars"] = [2, 3, 5, 6, 9, 10, 11],
            ["Mercury"] = [1, 3, 4, 5, 7, 8, 10, 11],
            ["Jupiter"] = [1, 2, 4, 7, 8, 10, 11],
            ["Venus"] = [3, 4, 5, 7, 9, 10, 11],
            ["Saturn"] = [3, 5, 6, 11],
            ["Ascendant"] = [3, 6, 10, 11]
        },
        ["Mars"] = new(StringComparer.Ordinal)
        {
            ["Sun"] = [3, 5, 6, 10, 11],
            ["Moon"] = [3, 6, 11],
            ["Mars"] = [1, 2, 4, 7, 8, 10, 11],
            ["Mercury"] = [3, 5, 6, 11],
            ["Jupiter"] = [6, 10, 11, 12],
            ["Venus"] = [6, 8, 11, 12],
            ["Saturn"] = [1, 4, 7, 8, 9, 10, 11],
            ["Ascendant"] = [1, 3, 6, 10, 11]
        },
        ["Mercury"] = new(StringComparer.Ordinal)
        {
            ["Sun"] = [5, 6, 9, 11, 12],
            ["Moon"] = [2, 4, 6, 8, 10, 11],
            ["Mars"] = [1, 2, 4, 7, 8, 9, 10, 11],
            ["Mercury"] = [1, 3, 5, 6, 9, 10, 11, 12],
            ["Jupiter"] = [6, 8, 11, 12],
            ["Venus"] = [1, 2, 3, 4, 5, 8, 9, 11],
            ["Saturn"] = [1, 2, 4, 7, 8, 9, 10, 11],
            ["Ascendant"] = [1, 2, 4, 6, 8, 10, 11]
        },
        ["Jupiter"] = new(StringComparer.Ordinal)
        {
            ["Sun"] = [1, 2, 3, 4, 7, 8, 9, 10, 11],
            ["Moon"] = [2, 5, 7, 9, 11],
            ["Mars"] = [1, 2, 4, 7, 8, 10, 11],
            ["Mercury"] = [1, 2, 4, 5, 6, 9, 10, 11],
            ["Jupiter"] = [1, 2, 3, 4, 7, 8, 10, 11],
            ["Venus"] = [2, 5, 6, 9, 10, 11],
            ["Saturn"] = [3, 5, 6, 12],
            ["Ascendant"] = [1, 2, 4, 5, 6, 7, 9, 10, 11]
        },
        ["Venus"] = new(StringComparer.Ordinal)
        {
            ["Sun"] = [8, 11, 12],
            ["Moon"] = [1, 2, 3, 4, 5, 8, 9, 11, 12],
            ["Mars"] = [3, 4, 6, 8, 9, 11, 12],
            ["Mercury"] = [3, 5, 6, 9, 11],
            ["Jupiter"] = [5, 8, 9, 10, 11],
            ["Venus"] = [1, 2, 3, 4, 5, 8, 9, 10, 11],
            ["Saturn"] = [3, 4, 5, 8, 9, 10, 11],
            ["Ascendant"] = [1, 2, 3, 4, 5, 8, 9, 11]
        },
        ["Saturn"] = new(StringComparer.Ordinal)
        {
            ["Sun"] = [1, 2, 4, 7, 8, 10, 11],
            ["Moon"] = [3, 6, 11],
            ["Mars"] = [3, 5, 6, 10, 11, 12],
            ["Mercury"] = [6, 8, 9, 10, 11, 12],
            ["Jupiter"] = [5, 6, 11, 12],
            ["Venus"] = [6, 11, 12],
            ["Saturn"] = [3, 5, 6, 11],
            ["Ascendant"] = [1, 3, 4, 6, 10, 11]
        }
    };

    /// <summary>The reference points a bhinna is counted from: the seven planets plus the ascendant.</summary>
    private static readonly string[] References =
        ["Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn", "Ascendant"];

    /// <summary>Shaper than ScoredPlanets: the parts of the table a caller may enumerate.</summary>
    public static IReadOnlyList<string> ReferencePoints { get; } = References;

    /// <summary>
    /// Bhinnashtakavarga for one scored planet: twelve bindu counts, one per sign, indexed from Aries.
    /// </summary>
    /// <param name="planet">The scored planet.</param>
    /// <param name="signByReference">Sign index (0 = Aries) for each reference point, including the ascendant.</param>
    /// <exception cref="ArgumentException">An unknown planet, or a missing reference sign.</exception>
    public static int[] Bhinna(string planet, IReadOnlyDictionary<string, int> signByReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planet);
        ArgumentNullException.ThrowIfNull(signByReference);

        if (!Tables.TryGetValue(planet, out var table))
            throw new ArgumentException($"'{planet}' is not a scored Ashtakavarga planet.", nameof(planet));

        var bindus = new int[12];

        foreach (var reference in References)
        {
            // A missing reference is an error, not a zero: silently scoring against 0 would produce a plausible but
            // wrong chart, which is worse than refusing.
            if (!signByReference.TryGetValue(reference, out var referenceSign))
            {
                throw new ArgumentException(
                    $"Missing sign for reference point '{reference}'; cannot score {planet}.", nameof(signByReference));
            }

            foreach (var house in table[reference])
            {
                // One-based classical houses; the 12th wraps to the same sign as the reference.
                var target = ((referenceSign + house - 1) % 12 + 12) % 12;
                bindus[target]++;
            }
        }

        return bindus;
    }

    /// <summary>
    /// Sarvashtakavarga: the twelve sign totals, each the sum of that sign's bindu across the seven scored planets.
    /// </summary>
    /// <remarks>
    /// Derived from the bhinnas rather than computed independently, so the two levels cannot disagree. That is the
    /// reconciliation the contract requires: an independent implementation would be able to drift, and a reader could
    /// not tell which number was wrong.
    /// </remarks>
    public static int[] Sarva(IReadOnlyDictionary<string, int[]> bhinnaByPlanet)
    {
        ArgumentNullException.ThrowIfNull(bhinnaByPlanet);

        var totals = new int[12];
        foreach (var planet in ScoredPlanets)
        {
            if (!bhinnaByPlanet.TryGetValue(planet, out var bindus))
            {
                throw new ArgumentException(
                    $"Missing bhinna for '{planet}'; sarva would be silently short.", nameof(bhinnaByPlanet));
            }

            if (bindus.Length != 12)
                throw new ArgumentException($"Bhinna for '{planet}' must have 12 entries.", nameof(bhinnaByPlanet));

            for (var sign = 0; sign < 12; sign++)
                totals[sign] += bindus[sign];
        }

        return totals;
    }

    /// <summary>Total bindu count for one planet across all signs. A fixed value per planet, so it can be asserted.</summary>
    public static int Total(string planet)
    {
        if (!Tables.TryGetValue(planet, out var table))
            throw new ArgumentException($"'{planet}' is not a scored Ashtakavarga planet.", nameof(planet));

        return References.Sum(reference => table[reference].Length);
    }
}
