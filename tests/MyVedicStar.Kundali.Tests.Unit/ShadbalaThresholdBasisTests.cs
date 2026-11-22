namespace MyVedicStar.Kundali.Tests.Unit;

using MyVedicStar.Kundali.Calculation;

/// <summary>
/// api#116. The Shadbala required-rupas table has been confirmed by the owner, and this file asserts the confirmed
/// state while keeping the unconfirmed-state gate exercised rather than deleted.
/// </summary>
/// <remarks>
/// <para><b>What changed on 2026-11-22.</b> The owner confirmed the seven values verbatim - Sun 5, Moon 6, Mars 5,
/// Mercury 7, Jupiter 6.5, Venus 5.5, Saturn 5 - and the values did not change. What changed is the recorded decision
/// and the rules version. These tests used to assert <c>owner-pending</c>; they now assert the confirmation, because a
/// test that is relaxed to accommodate a new fact stops being evidence of anything.</para>
/// <para><b>Why the gate is kept and not deleted.</b> The gate proves that an unconfirmed basis blocks a complete
/// result. A confirmation is reversible - an owner can withdraw or correct a value - so deleting the gate would leave
/// the next unconfirmed basis undetected. It is now driven by an explicit unconfirmed premise and by a synthetic
/// unconfirmed state, so the branch stays meaningful instead of becoming vacuously green.</para>
/// </remarks>
public sealed class ShadbalaThresholdBasisTests
{
    /// <summary>A complete chart: all seven scored planets, so nothing is missing for an unrelated reason.</summary>
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

    /// <summary>The owner's confirmation, verbatim (api#116, 2026-11-22).</summary>
    private static readonly (string Planet, double Rupas)[] OwnerConfirmedValues =
    [
        ("Sun", 5), ("Moon", 6), ("Mars", 5), ("Mercury", 7), ("Jupiter", 6.5), ("Venus", 5.5), ("Saturn", 5)
    ];

    [Fact]
    public void The_threshold_table_is_owner_confirmed_with_a_moved_rules_version()
    {
        var basis = Shadbala.ThresholdBasis;

        // The three facts that used to be asserted the other way round. Each one is now an assertion about the
        // confirmation rather than the absence of it, so this test can go red if the state regresses.
        Assert.True(basis.IsOwnerConfirmed,
            "The owner confirmed the required-rupas values on 2026-11-22 (api#116); the engine no longer records that.");
        Assert.Equal("owner-confirmed", basis.Status);
        Assert.DoesNotContain("owner-pending", basis.Status, StringComparison.Ordinal);

        // The version moved with the decision. A confirmation is a real decision, so a persisted result naming the old
        // version must remain distinguishable from one computed under a confirmed basis.
        Assert.Equal("shadbala-classical-owner-confirmed-1", Shadbala.RulesVersion);
        Assert.NotEqual("shadbala-classical-partial-1", Shadbala.RulesVersion);
        Assert.Contains("owner-confirmed", Shadbala.RulesVersion, StringComparison.Ordinal);

        // The note must name the deciding items and the date, so a reader is not left inferring who decided.
        Assert.Contains("api#116", basis.OwnedBy);
        Assert.Contains("2026-11-22", basis.Note);
    }

    [Fact]
    public void Every_confirmed_threshold_matches_the_owners_list_exactly()
    {
        var basis = Shadbala.ThresholdBasis;
        var result = Shadbala.Compute(Chart);

        // Both surfaces are checked, because they are the same fact told twice: the recorded decision and the number
        // the engine actually publishes. If they could diverge, the confirmation would be a statement about a table
        // nobody uses.
        foreach (var (planet, rupas) in OwnerConfirmedValues)
        {
            var decision = basis.For(planet);
            Assert.Equal(rupas, decision.Rupas);
            Assert.Equal("owner-confirmed", decision.Status);
            Assert.Equal("2026-11-22", decision.DecidedOn);
            Assert.Equal("api#116", decision.OwnedBy);

            var published = result.Planets.Single(entry => entry.Planet == planet);
            Assert.Equal(rupas, published.RequiredRupas);
        }

        // No confirmed list may omit a scored planet, and none may invent one: a value absent from the decision record
        // would be a threshold nobody confirmed being compared as though it had been.
        Assert.Equal(Shadbala.ScoredPlanets.Count, basis.Thresholds.Count);
        Assert.Equal(OwnerConfirmedValues.Length, basis.Thresholds.Count);
    }

    [Fact]
    public void The_decision_record_covers_every_threshold_rather_than_being_empty_when_confirmed()
    {
        var basis = Shadbala.ThresholdBasis;

        // The tempting shape after confirmation is to empty the list. That would make "the engine ships this value and
        // the owner confirmed it" and "the engine ships a value nobody has ever considered" indistinguishable - the
        // exact absence-reads-as-a-pass failure. The list is therefore present and populated in the confirmed state.
        Assert.NotEmpty(basis.Thresholds);
        Assert.All(basis.Thresholds, decision =>
        {
            Assert.Equal("owner-confirmed", decision.Status);
            Assert.NotNull(decision.DecidedOn);
            Assert.False(string.IsNullOrWhiteSpace(decision.OwnedBy));
        });

        Assert.Throws<InvalidOperationException>(() => basis.For("Rahu"));
    }

    [Fact]
    public void The_thresholds_are_not_unconfirmed_and_nothing_claims_they_are()
    {
        // The self-contradicting shape this guards: IsOwnerConfirmed true beside a list of entries still labelled
        // unconfirmed. The type no longer has an UnconfirmedThreshold at all, and this asserts no decision claims that
        // status while the basis says confirmed.
        var basis = Shadbala.ThresholdBasis;

        Assert.True(basis.IsOwnerConfirmed);
        Assert.DoesNotContain(basis.Thresholds, decision => decision.Status == "owner-pending");
        Assert.DoesNotContain("unconfirmed by the owner", basis.Note, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The gate, retained: an unconfirmed basis must still block completeness, and a confirmed one must permit it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate was a disjunction over the live state. Both branches are now exercised <b>explicitly</b>, because with a
    /// confirmed basis the disjunction is satisfied by its second branch and would never evaluate the first - a test
    /// that can no longer fail on the thing it was written for.
    /// </para>
    /// <para>
    /// <b>Premise 1 (the unconfirmed branch still works).</b> A synthetic unconfirmed state plus a complete result must
    /// fail the gate. This is the same predicate the gate applies, evaluated against an unconfirmed basis.
    /// </para>
    /// <para>
    /// <b>Premise 2 (the confirmed branch permits completeness).</b> A confirmed basis plus a complete result must pass,
    /// which is the state the product is in now and is why the owner confirmation mattered.
    /// </para>
    /// <para>
    /// <b>Why not retire the gate.</b> The confirmation is reversible; a withdrawn or corrected value returns the
    /// product to an unconfirmed basis. Retiring the gate would mean nobody notices. It is kept and both branches are
    /// asserted, which is stronger than the original disjunction because neither branch can silently stop working.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_unconfirmed_basis_blocks_completeness_and_a_confirmed_one_permits_it()
    {
        var live = Shadbala.ThresholdBasis;

        // The gate predicate, stated once so both premises run through the same function rather than two paraphrases.
        static bool GateHolds(Shadbala.ThresholdBasisState basis, bool anyCompletePlanet)
            => !anyCompletePlanet || basis.IsOwnerConfirmed;

        // Premise 1: unconfirmed + complete => must block.
        var unconfirmed = live with
        {
            Status = Shadbala.DecisionStatus.OwnerPending,
            IsOwnerConfirmed = false,
            Thresholds = live.Thresholds
                .Select(decision => decision with { Status = Shadbala.DecisionStatus.OwnerPending, DecidedOn = null })
                .ToArray()
        };

        Assert.False(unconfirmed.IsOwnerConfirmed);
        Assert.False(GateHolds(unconfirmed, anyCompletePlanet: true),
            "An unconfirmed threshold basis permitted a complete result. That is the silent switch api#116 exists to "
            + "prevent, and it must fail here even though the live basis is currently confirmed.");

        // Premise 2: confirmed + complete => permitted.
        Assert.True(live.IsOwnerConfirmed);
        Assert.True(GateHolds(live, anyCompletePlanet: true));

        // And the live state today still reports incompleteness for its own reasons, which is a different fact from
        // the threshold basis and must not be confused with it.
        var result = Shadbala.Compute(Chart);
        Assert.False(result.IsComplete);
        Assert.All(result.Planets, planet => Assert.False(planet.IsComplete));
    }

    [Fact]
    public void The_blocking_message_still_names_every_value_and_is_exercised_while_unreachable()
    {
        var live = Shadbala.ThresholdBasis;

        // Build the message from an unconfirmed copy: with a confirmed basis no production path produces it, so a test
        // that only read the live state would never exercise the message and it would rot silently until the day it was
        // needed. Testing it here is what keeps it honest.
        var unconfirmed = live with
        {
            Status = Shadbala.DecisionStatus.OwnerPending,
            IsOwnerConfirmed = false,
            Thresholds = live.Thresholds
                .Select(decision => decision with { Status = Shadbala.DecisionStatus.OwnerPending, DecidedOn = null })
                .ToArray()
        };

        var message = unconfirmed.BlockingMessage(Shadbala.RulesVersion);

        // Not vacuous: the message must name every value. An empty list would make this loop pass without checking
        // anything, so the count is asserted before the loop.
        Assert.Equal(Shadbala.ScoredPlanets.Count, unconfirmed.Thresholds.Count);
        foreach (var decision in unconfirmed.Thresholds)
        {
            Assert.Contains(decision.Planet, message, StringComparison.Ordinal);
            Assert.Contains(decision.Rupas.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                message, StringComparison.Ordinal);
            Assert.Contains(decision.OwnedBy, message, StringComparison.Ordinal);
        }

        // The tolerance decision is named because it is the same owner's and is NOT settled by this confirmation.
        Assert.Contains("#144", message, StringComparison.Ordinal);
        Assert.Contains("api#116", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_blocking_message_is_generated_from_the_recorded_values_not_written_by_hand()
    {
        var basis = Shadbala.ThresholdBasis;
        var message = basis.BlockingMessage(Shadbala.RulesVersion);

        // Cross-check against the numbers the engine publishes, so the message cannot become a second source of truth
        // for the same fact.
        var result = Shadbala.Compute(Chart);
        foreach (var planet in result.Planets)
        {
            var decision = basis.For(planet.Planet);
            Assert.Equal(planet.RequiredRupas, decision.Rupas);
            Assert.Contains(decision.Rupas.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_withheld_components_are_unaffected_by_the_threshold_confirmation()
    {
        // The confirmation settles the THRESHOLD. It does not settle the component set, and Kala and Chesta stay
        // withheld with reasons. Implemented when those land, not by this decision.
        Assert.Contains("Kala", Shadbala.WithheldComponents.Keys);
        Assert.Contains("Chesta", Shadbala.WithheldComponents.Keys);
        Assert.DoesNotContain("Kala", Shadbala.SupportedComponents);
        Assert.DoesNotContain("Chesta", Shadbala.SupportedComponents);

        Assert.All(Shadbala.WithheldComponents.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }
}
