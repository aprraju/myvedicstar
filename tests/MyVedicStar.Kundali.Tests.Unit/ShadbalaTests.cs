namespace MyVedicStar.Kundali.Tests.Unit;

using MyVedicStar.Kundali.Calculation;

public sealed class ShadbalaTests
{
    private static readonly IReadOnlyDictionary<string, double> Chart =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Sun"] = 10.0,
            ["Moon"] = 100.0,
            ["Mars"] = 200.0,
            ["Mercury"] = 250.0,
            ["Jupiter"] = 300.0,
            ["Venus"] = 60.0,
            ["Saturn"] = 330.0
        };

    [Fact]
    public void Every_scored_planet_is_scored()
    {
        var result = Shadbala.Compute(Chart);

        Assert.Equal(Shadbala.ScoredPlanets.Count, result.Planets.Count);
        Assert.Equal(Shadbala.ScoredPlanets, result.Planets.Select(planet => planet.Planet));
    }

    [Fact]
    public void Total_is_the_sum_of_the_produced_components()
    {
        // The reconciliation the contract requires: a total that does not match its parts is a defect, not a rounding
        // difference, so it is asserted exactly against the component list rather than against a hardcoded number.
        foreach (var planet in Shadbala.Compute(Chart).Planets)
        {
            Assert.Equal(planet.Components.Sum(component => component.Rupas), planet.TotalRupas, 10);
        }
    }

    [Fact]
    public void Only_supported_components_are_produced()
    {
        foreach (var planet in Shadbala.Compute(Chart).Planets)
        {
            Assert.Equal(
                Shadbala.SupportedComponents,
                planet.Components.Select(component => component.Component));
        }
    }

    [Fact]
    public void Kala_and_Chesta_are_withheld_with_reasons_never_silently_absent()
    {
        var result = Shadbala.Compute(Chart);

        // The whole integrity of this section is that the missing two components are named. If these assertions ever
        // read 'empty', the section has started presenting a partial total as complete.
        Assert.False(result.IsComplete);
        Assert.Contains("Kala", result.ComponentsWithheld);
        Assert.Contains("Chesta", result.ComponentsWithheld);

        foreach (var planet in result.Planets)
        {
            Assert.False(planet.IsComplete);
            Assert.Contains("Kala", planet.ComponentsWithheld);
            Assert.Contains("Chesta", planet.ComponentsWithheld);
            Assert.DoesNotContain("Kala", planet.Components.Select(component => component.Component));
            Assert.DoesNotContain("Chesta", planet.Components.Select(component => component.Component));
        }
    }

    [Fact]
    public void Withheld_components_carry_a_reason_code_each()
    {
        Assert.Equal(KundaliCalculationReasons.RequiresExactBirthTime, Shadbala.WithheldComponents["Kala"]);
        Assert.Equal(KundaliCalculationReasons.NotImplemented, Shadbala.WithheldComponents["Chesta"]);
    }

    [Fact]
    public void Threshold_ratio_divides_total_by_the_required_rupas()
    {
        foreach (var planet in Shadbala.Compute(Chart).Planets)
        {
            Assert.NotNull(planet.RequiredRupas);
            Assert.NotNull(planet.RatioToThreshold);
            Assert.Equal(planet.TotalRupas / planet.RequiredRupas!.Value, planet.RatioToThreshold!.Value, 10);
            Assert.Equal(planet.RequiredRupas!.Value - planet.TotalRupas, planet.RupasToThreshold!.Value, 10);
        }
    }

    [Fact]
    public void A_missing_planet_is_an_error_rather_than_a_zero()
    {
        // Scoring an absent planet at zero would produce a plausible, wrong strength. Refusing is the correct behaviour
        // and this test is what stops a future 'be lenient' refactor.
        var incomplete = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Sun"] = 10.0,
            ["Moon"] = 100.0
        };

        var error = Assert.Throws<ArgumentException>(() => Shadbala.Compute(incomplete));
        Assert.Contains("Missing sidereal longitude", error.Message);
    }

    [Fact]
    public void Dig_is_declared_a_placeholder_not_a_score()
    {
        // Dig Bala needs the unvalidated ascendant. The component is present with zero rupas and a basis that says it is
        // NOT the classical quantity - replacing it with something measured from the Sun or Moon would be a different
        // quantity wearing the same name.
        foreach (var planet in Shadbala.Compute(Chart).Planets)
        {
            var dig = planet.Components.Single(component => component.Component == "Dig");
            Assert.Equal(0.0, dig.Rupas);
            Assert.Contains("unvalidated ascendant", dig.Basis);
            Assert.Contains("placeholder", dig.Basis);
        }
    }

    [Fact]
    public void Naisargika_follows_the_classical_order()
    {
        // Classical order Sun > Moon > Venus > Jupiter > Mercury > Mars > Saturn. Asserted by NAME rather than by index,
        // because an index assertion silently passes if ScoredPlanets is ever reordered.
        var byPlanet = Shadbala.Compute(Chart).Planets.ToDictionary(
            planet => planet.Planet,
            planet => planet.Components.Single(c => c.Component == "Naisargika").Rupas,
            StringComparer.Ordinal);

        var classicalOrder = new[] { "Sun", "Moon", "Venus", "Jupiter", "Mercury", "Mars", "Saturn" };
        for (var i = 1; i < classicalOrder.Length; i++)
        {
            Assert.True(
                byPlanet[classicalOrder[i - 1]] > byPlanet[classicalOrder[i]],
                $"{classicalOrder[i - 1]} must be naturally stronger than {classicalOrder[i]}");
        }

        Assert.Equal(1.0, byPlanet["Sun"], 10);
    }

    [Fact]
    public void Naisargika_is_chart_independent()
    {
        // Natural strength is a fixed classical ordering; it must not move when positions move.
        var moved = Chart.ToDictionary(pair => pair.Key, pair => (pair.Value + 37.0) % 360.0, StringComparer.Ordinal);

        var before = Shadbala.Compute(Chart).Planets
            .Select(planet => planet.Components.Single(component => component.Component == "Naisargika").Rupas);
        var after = Shadbala.Compute(moved).Planets
            .Select(planet => planet.Components.Single(component => component.Component == "Naisargika").Rupas);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Sthana_is_zero_at_debilitation_and_maximal_opposite_it()
    {
        // The Sun is debilitated at 190 degrees and strongest at 10 degrees.
        var debilitated = Shadbala.Compute(With("Sun", 190.0)).Planets.Single(p => p.Planet == "Sun");
        var exalted = Shadbala.Compute(With("Sun", 10.0)).Planets.Single(p => p.Planet == "Sun");

        Assert.Equal(0.0, debilitated.Components.Single(c => c.Component == "Sthana").Rupas, 10);
        Assert.Equal(1.0, exalted.Components.Single(c => c.Component == "Sthana").Rupas, 10);
    }

    [Fact]
    public void Sthana_is_symmetric_about_the_debilitation_point()
    {
        // 190 +/- 60 must give the same positional strength; a sign or modulo error breaks this symmetry.
        var before = Shadbala.Compute(With("Sun", 130.0)).Planets.Single(p => p.Planet == "Sun");
        var after = Shadbala.Compute(With("Sun", 250.0)).Planets.Single(p => p.Planet == "Sun");

        Assert.Equal(
            before.Components.Single(c => c.Component == "Sthana").Rupas,
            after.Components.Single(c => c.Component == "Sthana").Rupas,
            10);
    }

    [Fact]
    public void Drik_counts_a_received_whole_sign_aspect()
    {
        // Jupiter aspects the 5th, 7th and 9th from itself. Put the Sun exactly 7 signs from Jupiter and require that
        // Jupiter appears among the Sun's Drik contributors.
        var chart = With("Jupiter", 0.0, sunLongitude: 180.0);
        var sun = Shadbala.Compute(chart).Planets.Single(p => p.Planet == "Sun");
        var drik = sun.Components.Single(c => c.Component == "Drik");

        Assert.Contains("Jupiter", drik.Basis);
    }

    [Fact]
    public void Drik_excludes_the_planet_aspecting_itself()
    {
        var sun = Shadbala.Compute(Chart).Planets.Single(p => p.Planet == "Sun");
        var basis = sun.Components.Single(c => c.Component == "Drik").Basis;

        Assert.DoesNotContain("Sun(", basis);
    }

    [Fact]
    public void Unit_and_rules_version_are_published()
    {
        var result = Shadbala.Compute(Chart);

        Assert.Equal("rupas", result.Unit);
        Assert.Equal(Shadbala.RulesVersion, result.RulesVersion);
        Assert.Equal(Shadbala.VirupasPerRupa, 60.0);
    }

    [Fact]
    public void Rotating_the_whole_chart_does_not_change_which_aspects_are_received()
    {
        // Aspects are whole-sign and relative, so a uniform rotation must leave every Drik score identical. An absolute
        // longitude comparison would break this and is a common way to get aspects subtly wrong.
        var rotated = Chart.ToDictionary(pair => pair.Key, pair => (pair.Value + 47.0) % 360.0, StringComparer.Ordinal);

        var before = Shadbala.Compute(Chart).Planets
            .Select(p => p.Components.Single(c => c.Component == "Drik").Rupas).ToArray();
        var after = Shadbala.Compute(rotated).Planets
            .Select(p => p.Components.Single(c => c.Component == "Drik").Rupas).ToArray();

        Assert.Equal(before, after);
    }

    private static Dictionary<string, double> With(string planet, double longitude, double? sunLongitude = null)
    {
        var chart = new Dictionary<string, double>(Chart, StringComparer.Ordinal)
        {
            [planet] = longitude
        };

        if (sunLongitude.HasValue)
            chart["Sun"] = sunLongitude.Value;

        return chart;
    }
}
