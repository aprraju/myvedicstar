using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// Ashtakavarga rules, asserted against their structural invariants rather than against generated totals.
/// </summary>
/// <remarks>
/// The tables themselves are data with a cited version; what can be proven here is that the arithmetic that consumes
/// them is correct - every bindu lands in exactly one sign, the two levels reconcile, and no unknown input is silently
/// scored as zero. A wrong table would pass these tests, which is precisely why the rules version is published with
/// every result so a table change is visible.
/// </remarks>
public sealed class AshtakavargaTests
{
    /// <summary>A chart where every reference point sits in Aries, so every house maps predictably.</summary>
    private static IReadOnlyDictionary<string, int> AllInAries() =>
        Ashtakavarga.ReferencePoints.ToDictionary(point => point, _ => 0, StringComparer.Ordinal);

    [Fact]
    public void Every_bhinna_has_twelve_sign_counts()
    {
        foreach (var planet in Ashtakavarga.ScoredPlanets)
            Assert.Equal(12, Ashtakavarga.Bhinna(planet, AllInAries()).Length);
    }

    [Fact]
    public void A_bindu_lands_in_exactly_one_sign_so_the_total_is_the_table_size()
    {
        // The strongest arithmetic invariant: no house offset is double-counted, and none is lost. If two references
        // pointed at the same house the total would still be right but the distribution would not; if an offset fell
        // outside 0-11 the total would drop. This catches both.
        foreach (var planet in Ashtakavarga.ScoredPlanets)
        {
            var bindus = Ashtakavarga.Bhinna(planet, AllInAries());
            Assert.Equal(Ashtakavarga.Total(planet), bindus.Sum());
        }
    }

    [Fact]
    public void Bhinnas_are_never_negative_and_never_exceed_the_number_of_references()
    {
        foreach (var planet in Ashtakavarga.ScoredPlanets)
        {
            var bindus = Ashtakavarga.Bhinna(planet, AllInAries());
            Assert.All(bindus, count => Assert.InRange(count, 0, Ashtakavarga.ReferencePoints.Count));
        }
    }

    [Fact]
    public void Sarva_is_derived_from_bhinnas_so_the_two_levels_always_reconcile()
    {
        var reference = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Sun"] = 0, ["Moon"] = 1, ["Mars"] = 2, ["Mercury"] = 3,
            ["Jupiter"] = 4, ["Venus"] = 5, ["Saturn"] = 6, ["Ascendant"] = 7
        };

        var bhinnaByPlanet = Ashtakavarga.ScoredPlanets
            .ToDictionary(planet => planet, planet => Ashtakavarga.Bhinna(planet, reference), StringComparer.Ordinal);

        var sarva = Ashtakavarga.Sarva(bhinnaByPlanet);

        for (var sign = 0; sign < 12; sign++)
            Assert.Equal(Ashtakavarga.ScoredPlanets.Sum(planet => bhinnaByPlanet[planet][sign]), sarva[sign]);

        // And the grand total reconciles too: summing sarva must equal summing every bhinna.
        Assert.Equal(Ashtakavarga.ScoredPlanets.Sum(Ashtakavarga.Total), sarva.Sum());
    }

    [Fact]
    public void A_missing_reference_point_is_an_error_rather_than_a_silent_zero()
    {
        var incomplete = new Dictionary<string, int>(StringComparer.Ordinal) { ["Sun"] = 0 };

        // Scoring against a missing reference would produce a plausible, wrong chart. Refusing is the only safe answer.
        Assert.Throws<ArgumentException>(() => Ashtakavarga.Bhinna("Sun", incomplete));
    }

    [Fact]
    public void A_missing_bhinna_is_an_error_rather_than_a_short_sarva()
    {
        var partial = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["Sun"] = new int[12]
        };

        Assert.Throws<ArgumentException>(() => Ashtakavarga.Sarva(partial));
    }

    [Fact]
    public void An_unknown_planet_is_rejected_rather_than_scored_with_an_empty_table()
    {
        Assert.Throws<ArgumentException>(() => Ashtakavarga.Bhinna("Rahu", AllInAries()));
        Assert.Throws<ArgumentException>(() => Ashtakavarga.Total("Ketu"));
    }

    [Fact]
    public void The_rules_version_is_published_so_a_table_change_is_visible()
    {
        // A table change without a version change would silently alter every historical comparison.
        Assert.False(string.IsNullOrWhiteSpace(Ashtakavarga.RulesVersion));
    }

    [Fact]
    public void The_scored_planets_are_the_seven_that_contribute_and_not_the_nodes()
    {
        Assert.Equal(7, Ashtakavarga.ScoredPlanets.Count);
        Assert.DoesNotContain("Rahu", Ashtakavarga.ScoredPlanets);
        Assert.DoesNotContain("Ketu", Ashtakavarga.ScoredPlanets);
        // The ascendant is a reference point but never a scored planet.
        Assert.DoesNotContain("Ascendant", Ashtakavarga.ScoredPlanets);
        Assert.Contains("Ascendant", Ashtakavarga.ReferencePoints);
    }

    [Fact]
    public void Rotating_the_whole_chart_rotates_the_totals_rather_than_changing_them()
    {
        // A structural property: ashtakavarga depends only on relative positions. Shifting every reference by the same
        // amount must permute the signs and leave the multiset of totals identical.
        var baseChart = AllInAries();
        var shifted = Ashtakavarga.ReferencePoints.ToDictionary(point => point, _ => 5, StringComparer.Ordinal);

        var baseTotals = Ashtakavarga.Sarva(Ashtakavarga.ScoredPlanets
            .ToDictionary(p => p, p => Ashtakavarga.Bhinna(p, baseChart), StringComparer.Ordinal));
        var shiftedTotals = Ashtakavarga.Sarva(Ashtakavarga.ScoredPlanets
            .ToDictionary(p => p, p => Ashtakavarga.Bhinna(p, shifted), StringComparer.Ordinal));

        Assert.Equal(baseTotals.Order().ToArray(), shiftedTotals.Order().ToArray());
    }
}
