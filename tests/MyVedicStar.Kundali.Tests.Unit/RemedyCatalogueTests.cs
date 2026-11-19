using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// api#128. The catalogue's provenance rule, and that <c>unclassified</c> is a real member rather than a sentinel.
/// </summary>
public sealed class RemedyCatalogueTests
{
    private static KundaliRemedy Entry(string code, string? reference = "A published source.") =>
        new(code, "evaluated", "general", "A suggestion.", "A tradition", reference!);

    [Fact]
    public void Every_catalogue_entry_carries_a_reference()
    {
        Assert.All(RemedyCatalogue.All, entry =>
            Assert.False(string.IsNullOrWhiteSpace(entry.Reference),
                $"Catalogue entry '{entry.FindingCode}' has no reference."));
    }

    [Fact]
    public void Unclassified_is_a_real_entry_with_its_own_reference_not_a_sentinel()
    {
        var unclassified = RemedyCatalogue.For(RemedyCatalogue.UnclassifiedCode);

        Assert.NotNull(unclassified);
        // Not merely present: it must be attributable like any other member. A special-cased sentinel is a null
        // wearing a name, and exclusion would be indistinguishable from "no remedy exists".
        Assert.False(string.IsNullOrWhiteSpace(unclassified.Reference));
        Assert.False(string.IsNullOrWhiteSpace(unclassified.Tradition));
    }

    [Fact]
    public void Deleting_the_unclassified_reference_breaks_validation()
    {
        // This is what makes the reference load-bearing rather than decorative: an entry that can lose its provenance
        // without a failure is documentation, not a guarantee.
        var withoutReference = new[] { Entry(RemedyCatalogue.UnclassifiedCode, reference: "") };

        var failure = Assert.Throws<InvalidOperationException>(() => RemedyCatalogue.Validate(withoutReference));
        Assert.Contains("no reference", failure.Message);
    }

    [Fact]
    public void Deleting_the_unclassified_entry_entirely_breaks_validation()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            RemedyCatalogue.Validate([Entry("yoga:gaja-kesari")]));

        Assert.Contains(RemedyCatalogue.UnclassifiedCode, failure.Message);
    }

    [Fact]
    public void An_entry_with_no_tradition_is_refused()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            RemedyCatalogue.Validate([Entry(RemedyCatalogue.UnclassifiedCode, "ref"),
                new KundaliRemedy("yoga:gaja-kesari", "evaluated", "general", "s", "", "ref")]));

        Assert.Contains("tradition", failure.Message);
    }

    [Fact]
    public void An_unknown_code_has_no_entry()
    {
        Assert.Null(RemedyCatalogue.For("yoga:not-in-the-catalogue"));
    }

    [Fact]
    public void The_catalogue_declares_a_version()
    {
        // catalogVersion identifies the asset that produced a section, so a cached response can be attributed to the
        // catalogue it was rendered against.
        Assert.False(string.IsNullOrWhiteSpace(RemedyCatalogue.Version));
    }
}

