namespace MyVedicStar.Kundali.Tests.Unit;

using MyVedicStar.Kundali.Calculation;

/// <summary>
/// api#116. The Shadbala threshold table is unconfirmed by the owner, and this file makes that a loud failure at the
/// exact moment the unconfirmed number starts looking authoritative.
/// </summary>
/// <remarks>
/// <para><b>The defect this pins.</b> The required-rupas table ships as versioned config
/// (<c>shadbala-classical-partial-1</c>) with no owner confirmation. That is survivable only while Shadbala is
/// <c>complete: false</c> for every planet, because Kala and Chesta are withheld: a partial total falls below the
/// threshold for reasons unrelated to the chart, and every result says so in four places. The day <c>complete</c> can
/// be <c>true</c>, the ratio stops being visibly provisional and starts looking like a settled comparison - using a
/// number nobody confirmed.</para>
/// <para><b>Why the test is written to keep failing.</b> A test that merely asserts the current version string would go
/// green today and stay green through the change that breaks the guarantee. So this file asserts the guarantee itself:
/// <i>either</i> the results are incomplete <i>or</i> the threshold basis carries an owner confirmation. Today the
/// first half holds. When Kala and Chesta land and completeness becomes reachable, the first half stops holding and the
/// only way to keep the suite green is to record a real owner confirmation - which is the decision api#116 exists to
/// force, not a formatting fix.</para>
/// <para><b>What it deliberately does not do.</b> It does not assert that the numbers are correct. Nobody has confirmed
/// them, and an assertion here would be this agent inventing the owner's decision. It pins the table as <i>unconfirmed
/// data</i> and names the issues that own the decision.</para>
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

    [Fact]
    public void The_threshold_table_is_unconfirmed_and_the_rules_version_says_so()
    {
        // The version string is the only machine-readable statement that these numbers are provisional. If someone
        // renames it to something that reads as settled - dropping the "partial", say - while the values are still
        // unconfirmed, the provenance signal is gone even though nothing else changed.
        Assert.Equal("shadbala-classical-partial-1", Shadbala.RulesVersion);
        Assert.Contains("partial", Shadbala.RulesVersion, StringComparison.Ordinal);

        // And the basis itself must be reachable as a stated fact rather than a comment, so a caller can display it.
        Assert.Equal("owner-pending", Shadbala.ThresholdBasis.Status);
        Assert.False(Shadbala.ThresholdBasis.IsOwnerConfirmed,
            "The required-rupas table is asserted as owner-confirmed, but it is not: api#116 is open. If the owner " +
            "has since confirmed it, that is a real decision - record the version bump and the confirmation, do not " +
            "flip this bit.");
        Assert.Contains("116", Shadbala.ThresholdBasis.OwnedBy);
    }

    [Fact]
    public void Every_published_threshold_is_pinned_at_its_current_unconfirmed_value()
    {
        // Pinned, not endorsed. Pinning is what makes a change to an unconfirmed number visible in review instead of
        // arriving inside an unrelated diff. If the owner corrects a value, this list changes with the version string
        // and an owner reference - that pairing is the point.
        var expected = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Sun"] = 5.0,
            ["Moon"] = 6.0,
            ["Mars"] = 5.0,
            ["Mercury"] = 7.0,
            ["Jupiter"] = 6.5,
            ["Venus"] = 5.5,
            ["Saturn"] = 5.0
        };

        var result = Shadbala.Compute(Chart);
        foreach (var planet in result.Planets)
        {
            Assert.True(expected.TryGetValue(planet.Planet, out var value),
                $"{planet.Planet} publishes a threshold that is not in the pinned set.");
            Assert.Equal(value, planet.RequiredRupas);
        }

        Assert.Equal(expected.Count, result.Planets.Count);
    }

    [Fact]
    public void The_ratio_is_published_but_never_as_authoritative_while_the_basis_is_unconfirmed()
    {
        var result = Shadbala.Compute(Chart);

        foreach (var planet in result.Planets)
        {
            // The ratio is published so a consumer charting progress has a number; withholding it would push callers
            // to compute it themselves from a partial total - the same error, unlabelled. What must never happen is a
            // ratio presented with no completeness signal.
            Assert.NotNull(planet.RatioToThreshold);
            Assert.False(planet.IsComplete);
            Assert.NotEmpty(planet.ComponentsWithheld);
        }

        Assert.False(result.IsComplete);
    }

    /// <summary>
    /// The blocker: this passes today only because completeness is unreachable, and must fail the day it is.
    /// </summary>
    /// <remarks>
    /// This is a disjunction on purpose, and it is the whole point of the file. When Kala and Chesta are implemented,
    /// <see cref="Shadbala.Result.IsComplete"/> becomes obtainable, this assertion stops being satisfied by the first
    /// branch, and the suite goes red until <see cref="Shadbala.ThresholdBasis.IsOwnerConfirmed"/> is genuinely true.
    /// A "complete" Shadbala compared against an unconfirmed table is a confirmed-looking answer built on a number
    /// nobody approved, which is the failure api#116 was filed to prevent.
    /// </remarks>
    [Fact]
    public void Completeness_is_impossible_or_the_threshold_basis_is_owner_confirmed()
    {
        var result = Shadbala.Compute(Chart);

        var incompleteResultsExist =
            !result.IsComplete || result.Planets.Any(planet => !planet.IsComplete);

        Assert.True(incompleteResultsExist || Shadbala.ThresholdBasis.IsOwnerConfirmed,
            "Shadbala can now return a complete result while " + Shadbala.RulesVersion + " carries no owner " +
            "confirmation. The ratio is no longer visibly provisional, so the unconfirmed required-rupas table is " +
            "now a merge blocker: owner confirmation of the values is required, and the version string must move with " +
            "it. See api#116 and the tolerance decision in #144. Do not resolve this by widening the assertion or by " +
            "deleting the completeness check - that is the silent switch this test exists to prevent.");
    }

    [Fact]
    public void The_withheld_components_are_what_makes_completeness_unreachable_today()
    {
        // Names the mechanism rather than asserting a bare boolean: completeness is unreachable because Kala and
        // Chesta are withheld, so implementing them is precisely the change that triggers the blocker above.
        Assert.Contains("Kala", Shadbala.WithheldComponents.Keys);
        Assert.Contains("Chesta", Shadbala.WithheldComponents.Keys);
        Assert.DoesNotContain("Kala", Shadbala.SupportedComponents);
        Assert.DoesNotContain("Chesta", Shadbala.SupportedComponents);

        Assert.All(Shadbala.WithheldComponents.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }
}
