namespace MyVedicStar.Kundali.Tests.Unit;

using MyVedicStar.Kundali.Calculation;

public sealed class YogaTests
{
    /// <summary>Sign indices: 0 Aries .. 11 Pisces.</summary>
    private static Dictionary<string, int> Chart(
        int sun, int moon, int mars, int mercury, int jupiter, int venus, int saturn,
        int rahu = 0, int ketu = 6) =>
        new(StringComparer.Ordinal)
        {
            ["Sun"] = sun, ["Moon"] = moon, ["Mars"] = mars, ["Mercury"] = mercury,
            ["Jupiter"] = jupiter, ["Venus"] = venus, ["Saturn"] = saturn,
            ["Rahu"] = rahu, ["Ketu"] = ketu
        };

    private static Yogas.Assessment Get(Yogas.Result result, string code) =>
        result.Assessments.Concat(result.Indeterminate).Single(a => a.Code == code);

    // ---- the two absences must never be conflated -----------------------------------------------------------

    [Fact]
    public void Evaluated_and_failed_is_distinguishable_from_never_evaluated()
    {
        // The whole point of the section. A yoga that failed is a finished claim about this chart; one that was never
        // evaluated is not a claim at all. Both are absent from the satisfied list, so they are separated explicitly.
        var result = Yogas.Evaluate(Chart(0, 0, 0, 0, 5, 0, 0));

        var failed = Get(result, "gaja-kesari");
        var never = Get(result, "manglik");

        Assert.Equal(Yogas.Statuses.NotSatisfied, failed.Status);
        Assert.Null(failed.Reason);
        Assert.True(failed.Complete);

        Assert.Equal(Yogas.Statuses.Indeterminate, never.Status);
        Assert.NotNull(never.Reason);
        Assert.False(never.Complete);
    }

    [Fact]
    public void Every_indeterminate_assessment_is_enumerated_by_name_with_a_reason()
    {
        // An empty satisfied list must never read as a clean bill of health. Everything not evaluated is listed
        // individually, so a caller counting satisfied yogas also sees what was not looked at.
        var result = Yogas.Evaluate(Chart(0, 0, 0, 0, 5, 0, 0));

        Assert.NotEmpty(result.Indeterminate);
        foreach (var assessment in result.Indeterminate)
        {
            Assert.False(string.IsNullOrWhiteSpace(assessment.Code));
            Assert.False(string.IsNullOrWhiteSpace(assessment.Name));
            Assert.False(string.IsNullOrWhiteSpace(assessment.Reason));
            Assert.NotEmpty(assessment.UntestedComponents);
        }
    }

    [Fact]
    public void A_missing_graha_names_itself_rather_than_failing_silently()
    {
        // Kemadruma claims an ABSENCE, so it needs every graha. Drop Saturn and it must say so by name.
        var chart = Chart(0, 6, 2, 4, 8, 10, 11);
        chart.Remove("Saturn");

        var result = Yogas.Evaluate(chart);
        var kemadruma = Get(result, "kemadruma");

        Assert.Equal(Yogas.Statuses.Indeterminate, kemadruma.Status);
        Assert.Contains("Saturn", kemadruma.UntestedComponents.Single());
    }

    [Fact]
    public void An_absence_claim_is_not_evaluated_on_partial_positions()
    {
        // Guards the tempting shortcut: with only Sun and Moon present, nothing is in the 2nd from the Moon, so a naive
        // implementation would report Kemadruma satisfied. That would be a confident claim from data we do not have.
        var chart = new Dictionary<string, int>(StringComparer.Ordinal) { ["Sun"] = 0, ["Moon"] = 6 };

        var kemadruma = Get(Yogas.Evaluate(chart), "kemadruma");

        Assert.Equal(Yogas.Statuses.Indeterminate, kemadruma.Status);
        Assert.NotEqual(Yogas.Statuses.Satisfied, kemadruma.Status);
    }

    // ---- the rules themselves -------------------------------------------------------------------------------

    [Fact]
    public void Gaja_Kesari_holds_when_Jupiter_is_in_a_kendra_from_the_Moon()
    {
        // Moon in sign 0, Jupiter in sign 3 => 4th from the Moon, a kendra.
        var result = Yogas.Evaluate(Chart(0, 0, 0, 0, 3, 0, 0));

        Assert.Equal(Yogas.Statuses.Satisfied, Get(result, "gaja-kesari").Status);
    }

    [Fact]
    public void Gaja_Kesari_fails_when_Jupiter_is_in_a_non_kendra()
    {
        // Moon 0, Jupiter 1 => 2nd from the Moon, not a kendra.
        var result = Yogas.Evaluate(Chart(0, 0, 0, 0, 1, 0, 0));
        var assessment = Get(result, "gaja-kesari");

        Assert.Equal(Yogas.Statuses.NotSatisfied, assessment.Status);
        Assert.NotEmpty(assessment.Opposing);
    }

    [Fact]
    public void Kemadruma_holds_when_nothing_occupies_the_second_or_twelfth_from_the_Moon()
    {
        // Moon in 6. Nobody else in 7 (2nd) or 5 (12th).
        var result = Yogas.Evaluate(Chart(sun: 0, moon: 6, mars: 1, mercury: 2, jupiter: 3, venus: 4, saturn: 9));

        Assert.Equal(Yogas.Statuses.Satisfied, Get(result, "kemadruma").Status);
    }

    [Fact]
    public void Kemadruma_is_cancelled_when_a_graha_occupies_the_second_from_the_Moon()
    {
        // Moon in 6, Venus in 7 => 2nd from the Moon. The cancellation is reported as opposing evidence, not merely as
        // a failed boolean, so the caller can see why.
        var result = Yogas.Evaluate(Chart(sun: 0, moon: 6, mars: 1, mercury: 2, jupiter: 3, venus: 7, saturn: 9));
        var assessment = Get(result, "kemadruma");

        Assert.Equal(Yogas.Statuses.NotSatisfied, assessment.Status);
        Assert.Contains("Venus", assessment.Opposing.Single());
    }

    [Fact]
    public void Chandra_Mangala_holds_only_when_Moon_and_Mars_share_a_sign()
    {
        Assert.Equal(Yogas.Statuses.Satisfied,
            Get(Yogas.Evaluate(Chart(0, 6, 6, 0, 3, 0, 0)), "chandra-mangala").Status);
        Assert.Equal(Yogas.Statuses.NotSatisfied,
            Get(Yogas.Evaluate(Chart(0, 6, 7, 0, 3, 0, 0)), "chandra-mangala").Status);
    }

    [Fact]
    public void Budha_Aditya_holds_only_when_Sun_and_Mercury_share_a_sign()
    {
        Assert.Equal(Yogas.Statuses.Satisfied,
            Get(Yogas.Evaluate(Chart(4, 6, 6, 4, 3, 0, 0)), "budha-aditya").Status);
        Assert.Equal(Yogas.Statuses.NotSatisfied,
            Get(Yogas.Evaluate(Chart(4, 6, 6, 5, 3, 0, 0)), "budha-aditya").Status);
    }

    [Fact]
    public void Manglik_is_never_satisfied_because_it_needs_an_unvalidated_ascendant()
    {
        // The owner-approved definition is Lagna-only. Counting from the Moon instead is a different definition, not a
        // fallback, so this must never return satisfied or not-satisfied while the ascendant is withheld.
        var result = Yogas.Evaluate(Chart(0, 6, 0, 0, 3, 0, 0));
        var manglik = Get(result, "manglik");

        Assert.Equal(Yogas.Statuses.Indeterminate, manglik.Status);
        Assert.Equal(KundaliCalculationReasons.UnvalidatedReference, manglik.Reason);
        Assert.Contains(manglik.UntestedComponents, component => component.Contains("Ascendant"));
    }

    [Fact]
    public void Kaal_Sarp_holds_when_all_grahas_lie_on_one_side_of_the_nodal_axis()
    {
        // Rahu 0, Ketu 6. Grahas in 0..5 are one side.
        var result = Yogas.Evaluate(Chart(sun: 1, moon: 2, mars: 0, mercury: 3, jupiter: 4, venus: 5, saturn: 9,
            rahu: 0, ketu: 6));
        var assessment = Get(result, "kaal-sarp");

        Assert.Equal(Yogas.Statuses.NotSatisfied, assessment.Status);
        Assert.NotEmpty(assessment.Opposing);
    }

    [Fact]
    public void Kaal_Sarp_is_withheld_when_a_node_or_graha_is_missing()
    {
        var chart = Chart(1, 2, 0, 3, 4, 5, 9);
        chart.Remove("Rahu");

        Assert.Equal(Yogas.Statuses.Indeterminate, Get(Yogas.Evaluate(chart), "kaal-sarp").Status);
    }

    // ---- contract shape -------------------------------------------------------------------------------------

    [Fact]
    public void Satisfied_and_failed_assessments_are_not_duplicated_in_the_indeterminate_list()
    {
        var result = Yogas.Evaluate(Chart(0, 0, 0, 0, 3, 0, 0));

        var codes = result.Assessments.Select(a => a.Code).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(result.Assessments, a => a.Status == Yogas.Statuses.Indeterminate);
        Assert.All(result.Indeterminate, a => Assert.Equal(Yogas.Statuses.Indeterminate, a.Status));
    }

    [Fact]
    public void Every_evaluated_assessment_carries_its_rule_set_version_at_section_level()
    {
        var result = Yogas.Evaluate(Chart(0, 0, 0, 0, 3, 0, 0));

        Assert.Equal(Yogas.RulesVersion, result.RulesVersion);
        Assert.NotEmpty(Yogas.RulesVersion);
    }

    [Fact]
    public void Every_assessment_states_its_scope_and_never_uses_transit()
    {
        // Transit-scoped findings must never appear in the natal section.
        var result = Yogas.Evaluate(Chart(0, 0, 0, 0, 3, 0, 0));

        Assert.All(result.Assessments.Concat(result.Indeterminate),
            a => Assert.Equal(Yogas.Scopes.Natal, a.Scope));
    }

    [Fact]
    public void An_empty_chart_yields_no_satisfied_yoga_and_enumerates_every_withheld_one()
    {
        // The honest worst case: no positions at all. Nothing may be satisfied, and every rule must appear as
        // indeterminate with a reason - the section must not look like an empty clean chart.
        var result = Yogas.Evaluate(new Dictionary<string, int>(StringComparer.Ordinal));

        Assert.Empty(result.Assessments);
        Assert.Equal(6, result.Indeterminate.Count);
        Assert.All(result.Indeterminate, a =>
        {
            Assert.Equal(Yogas.Statuses.Indeterminate, a.Status);
            Assert.False(string.IsNullOrWhiteSpace(a.Reason));
            Assert.NotEmpty(a.UntestedComponents);
        });
    }
}
