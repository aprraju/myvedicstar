using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// Ashtakoota rules (KUN-15), asserted against their structural properties and their refusal to judge.
/// </summary>
/// <remarks>
/// <para>The koota tables are cited data with a version. What is provable here is the arithmetic that consumes them:
/// the eight categories always sum to 36, a category is either scored or reasoned (never both, never neither), and an
/// unknown Moon never becomes a zero.</para>
/// <para><b>The absence of a verdict is itself asserted.</b> The minimum acceptable-match threshold is an open owner
/// decision, so a test proves the result type carries no such field. A future threshold must arrive deliberately as
/// API data with provenance; this test is the tripwire that makes it an explicit change.</para>
/// </remarks>
public sealed class AshtakootaTests
{
    private static Ashtakoota.Side Side(int signIndex, int nakshatraIndex) =>
        Ashtakoota.Side.Of(signIndex, nakshatraIndex);

    [Fact]
    public void The_eight_maxima_sum_to_thirty_six()
    {
        Assert.Equal(8, Ashtakoota.KootaCodes.Count);
        Assert.Equal(Ashtakoota.MaximumTotal, Ashtakoota.MaximumByKoota.Values.Sum());
        Assert.Equal(36, Ashtakoota.MaximumTotal);
    }

    [Fact]
    public void Every_assessment_returns_all_eight_categories_in_classical_order()
    {
        var result = Ashtakoota.Assess(Side(0, 0), Side(6, 13));

        Assert.Equal(Ashtakoota.KootaCodes, result.Categories.Select(category => category.Code));
        Assert.Equal(Ashtakoota.RulesVersion, result.RulesVersion);
    }

    [Fact]
    public void A_category_is_either_scored_or_reasoned_and_never_both()
    {
        // Exhaustive over a spread of pairs so an indeterminate path is exercised alongside scored ones.
        for (var a = 0; a < 27; a += 3)
        {
            for (var b = 0; b < 27; b += 5)
            {
                var result = Ashtakoota.Assess(Side(a % 12, a), Side(b % 12, b));

                Assert.All(result.Categories, category =>
                {
                    if (category.IsScored)
                    {
                        Assert.Null(category.Reason);
                        Assert.InRange(category.Score!.Value, 0, category.Maximum);
                    }
                    else
                    {
                        Assert.NotNull(category.Reason);
                        Assert.False(string.IsNullOrWhiteSpace(category.Note));
                    }
                });
            }
        }
    }

    [Fact]
    public void A_scored_total_never_exceeds_the_scored_maximum()
    {
        for (var a = 0; a < 27; a += 2)
        {
            for (var b = 0; b < 27; b += 2)
            {
                var result = Ashtakoota.Assess(Side(a % 12, a), Side(b % 12, b));
                var scoredMax = result.Categories.Where(category => category.IsScored)
                    .Sum(category => category.Maximum);

                Assert.InRange(result.ScoredTotal, 0, scoredMax);
                Assert.InRange(scoredMax, 0, Ashtakoota.MaximumTotal);
            }
        }
    }

    [Fact]
    public void An_unknown_moon_is_indeterminate_for_every_moon_dependent_category_and_never_zero()
    {
        var result = Ashtakoota.Assess(
            Ashtakoota.Side.Unavailable(Ashtakoota.Reasons.BirthTimeUnknown),
            Side(3, 7));

        // All eight categories depend on the Moon, so all eight must be indeterminate - NOT scored 0, which would tell
        // a consumer the couple scored badly when in fact nothing was computed.
        Assert.All(result.Categories, category =>
        {
            Assert.False(category.IsScored);
            Assert.Equal(Ashtakoota.Reasons.BirthTimeUnknown, category.Reason);
            Assert.Equal("A", category.Side);
        });

        Assert.Equal(0, result.ScoredTotal);
        Assert.Equal(Ashtakoota.CertaintyValues.Partial, result.Certainty);
    }

    [Fact]
    public void The_indeterminate_side_names_the_side_that_actually_made_it_unknown()
    {
        var result = Ashtakoota.Assess(Side(3, 7),
            Ashtakoota.Side.Unavailable(Ashtakoota.Reasons.MoonPositionUnavailable));

        Assert.All(result.Categories, category => Assert.Equal("B", category.Side));
        Assert.All(result.Categories, category => Assert.Equal(Ashtakoota.Reasons.MoonPositionUnavailable,
            category.Reason));
    }

    [Fact]
    public void A_fully_known_pair_is_complete_and_its_categories_are_all_scored()
    {
        var result = Ashtakoota.Assess(Side(0, 0), Side(4, 10));

        Assert.Equal(Ashtakoota.CertaintyValues.Complete, result.Certainty);
        Assert.All(result.Categories, category => Assert.True(category.IsScored));
    }

    [Fact]
    public void Most_categories_are_symmetric_and_varna_is_the_only_deliberately_directional_one()
    {
        // Varna is directional BY DESIGN: the higher class must not be below the partner's, so A-to-B and B-to-A can
        // differ by exactly that one point. This was measured, not assumed - the first version of this test asserted a
        // symmetric total and failed at 23 vs 22, and the diff showed the whole gap was Varna.
        //
        // The consequence for the API: pairKey is order-independent, so the cached comparison must be computed from
        // the CANONICAL order, not from request order. Otherwise A-to-B and B-to-A would return different totals from
        // one cache entry. That is enforced in the facade, and this test records WHY it must be.
        for (var a = 0; a < 27; a += 4)
        {
            for (var b = 0; b < 27; b += 4)
            {
                var forward = Ashtakoota.Assess(Side(a % 12, a), Side(b % 12, b));
                var reversed = Ashtakoota.Assess(Side(b % 12, b), Side(a % 12, a));

                Assert.Equal(forward.Certainty, reversed.Certainty);

                // Every category except Varna must agree under reversal.
                var nonVarnaForward = forward.Categories.Where(c => c.Code != "Varna").Sum(c => c.Score ?? 0);
                var nonVarnaReversed = reversed.Categories.Where(c => c.Code != "Varna").Sum(c => c.Score ?? 0);
                Assert.Equal(nonVarnaForward, nonVarnaReversed);

                // And the only possible difference is Varna's single point.
                Assert.InRange(Math.Abs(forward.ScoredTotal - reversed.ScoredTotal), 0, 1);
            }
        }
    }

    [Fact]
    public void The_result_type_cannot_express_a_pass_or_fail_verdict()
    {
        // The owner has left the minimum acceptable-match threshold OPEN. This asserts the absence rather than trusting
        // that nobody adds one quietly: a property named verdict, threshold, acceptable or match on the result, or on
        // any category, is a design change that must be deliberate and approved.
        var resultProperties = typeof(Ashtakoota.Result).GetProperties().Select(property => property.Name).ToArray();
        var categoryProperties = typeof(Ashtakoota.Category).GetProperties().Select(property => property.Name).ToArray();

        string[] forbidden = ["Verdict", "Threshold", "IsAcceptable", "IsMatch", "Passed", "Recommendation"];

        Assert.DoesNotContain(resultProperties, name => forbidden.Contains(name, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(categoryProperties, name => forbidden.Contains(name, StringComparer.OrdinalIgnoreCase));

        // And it does still report what it IS for: a score, a maximum per category, and a certainty.
        Assert.Contains("ScoredTotal", resultProperties);
        Assert.Contains("Certainty", resultProperties);
    }

    [Fact]
    public void Bhakoot_is_zero_exactly_on_the_three_adverse_sign_pairs()
    {
        // A named rule, asserted directly: 2/12, 5/9 and 6/8 are adverse; everything else scores the full 7. The
        // distances are computed from actual sign indices so the test and the rule agree about which is which.
        for (var distance = 1; distance <= 12; distance++)
        {
            var result = Ashtakoota.Assess(Side(0, 0), Side(distance - 1, 0));
            var bhakoot = result.Categories.Single(category => category.Code == "Bhakoot");
            var adverse = distance is 2 or 5 or 6 or 8 or 9 or 12;

            Assert.Equal(adverse ? 0 : 7, bhakoot.Score);
        }
    }

    [Fact]
    public void Nadi_scores_eight_for_different_constitutions_and_zero_for_the_same()
    {
        // The nadi cycle is Adi, Madhya, Antya, Antya, Madhya, Adi - period 6, so within one cycle only nakshatra 0 and
        // nakshatra 5 share a constitution. (An earlier version of this test assumed a period of 3 and expected 0 vs 3
        // to be equal; it is not, and the assertion was wrong rather than the table.)
        var different = Ashtakoota.Assess(Side(0, 0), Side(0, 1)).Categories.Single(c => c.Code == "Nadi");
        var same = Ashtakoota.Assess(Side(0, 0), Side(0, 5)).Categories.Single(c => c.Code == "Nadi");

        Assert.Equal(8, different.Score);
        Assert.Equal(0, same.Score);
    }
}
