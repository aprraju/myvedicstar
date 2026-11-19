using MyVedicStar.Kundali.Shared;
using Xunit;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// api#132. The catalogue was an open vocabulary carried as a bare <c>string?</c>: an unnameable code travelled from a
/// database row to an API response without anything checking it, and rendered as "unknown" - a specific failure
/// collapsed into a generic one with no signal that information was lost.
/// </summary>
public sealed class KundaliFailureReasonTests
{
    /// <summary>
    /// Deliberately a code that must NEVER be added to the catalogue. If someone "fixes" a failing test by adding it,
    /// the test stops testing the class of defect and only covers one instance - which the issue forbids.
    /// </summary>
    private const string CodeAbsentFromCatalogue = "a_code_that_is_not_in_the_catalogue";

    [Fact]
    public void An_unrecognised_code_is_distinguishable_from_every_recognised_one()
    {
        Assert.True(KundaliFailureReason.TryParse(CodeAbsentFromCatalogue, out var unrecognised));

        Assert.False(unrecognised.IsRecognised);
        Assert.Equal(CodeAbsentFromCatalogue, unrecognised.Code);

        // The defect was indistinguishability: a caller could not tell a named failure from an unnamed one. Asserting
        // inequality against EVERY recognised reason is what makes that impossible, not just against one.
        foreach (var recognised in KundaliFailureReason.Recognised)
        {
            Assert.NotEqual(recognised, unrecognised);
            Assert.NotEqual(recognised.Code, unrecognised.Code);
        }
    }

    [Fact]
    public void An_unrecognised_code_keeps_its_original_text()
    {
        // Carrying only "recognised: false" would tell an operator that something was unrecognised without telling them
        // what - the same collapse in a nicer costume.
        KundaliFailureReason.TryParse(CodeAbsentFromCatalogue, out var reason);

        Assert.Equal(CodeAbsentFromCatalogue, reason.Code);
        Assert.Equal(CodeAbsentFromCatalogue, reason.ToString());
    }

    [Fact]
    public void A_null_code_is_absence_of_a_reason_not_an_unrecognised_one()
    {
        // NULL means "no reason recorded" and is already constrained in the database. Folding it into "unrecognised"
        // would repeat the original defect: two different facts rendered as one value.
        Assert.False(KundaliFailureReason.TryParse(null, out var reason));
        Assert.Equal(default, reason);
    }

    [Fact]
    public void Every_recognised_reason_parses_back_to_itself()
    {
        // Round-trip: the published code must classify to the same value, so what a caller reads can be re-parsed.
        foreach (var recognised in KundaliFailureReason.Recognised)
        {
            Assert.True(KundaliFailureReason.TryParse(recognised.Code, out var parsed));
            Assert.True(parsed.IsRecognised);
            Assert.Equal(recognised, parsed);
        }
    }

    [Fact]
    public void The_recognised_list_and_the_catalogue_agree()
    {
        // A constant added to KundaliFailureReasons without a matching value here would become silently unrecognised at
        // runtime - the defect re-entering through the fix. This keeps the two in step by failing rather than by
        // remembering.
        Assert.Equal(KundaliFailureReason.CatalogueCodes.Count, KundaliFailureReason.Recognised.Count);
        Assert.Equal(
            KundaliFailureReason.CatalogueCodes.OrderBy(code => code, StringComparer.Ordinal),
            KundaliFailureReason.Recognised.Select(reason => reason.Code).OrderBy(code => code, StringComparer.Ordinal));
    }

    [Fact]
    public void The_catalogue_no_longer_offers_unknown_as_a_value()
    {
        // "unknown" was a second representation of absence that rendered as a value. It must not come back, and it must
        // not become a recognised reason either.
        Assert.DoesNotContain("unknown", KundaliFailureReason.CatalogueCodes);
        Assert.DoesNotContain(KundaliFailureReason.Recognised, reason => reason.Code == "unknown");

        // And a literal "unknown" arriving from storage is now just another unrecognised code, not a special case.
        Assert.True(KundaliFailureReason.TryParse("unknown", out var parsed));
        Assert.False(parsed.IsRecognised);
    }
}
