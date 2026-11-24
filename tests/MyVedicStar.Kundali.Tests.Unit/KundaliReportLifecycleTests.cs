using System.Security.Claims;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-17 / api#57: the four contract gaps as enforced behaviour, not declarations.
/// </summary>
/// <remarks>
/// The issue closed on a contract shipped ahead of the surface, with 409/410 declared and unreachable. These tests hold
/// the opposite property: every value the surface can produce is a member of a closed set, and every status this route
/// can answer is produced by a path that exists here.
/// </remarks>
public sealed class KundaliReportRequestValidatorTests
{
    [Fact]
    public void A_valid_request_is_normalised_to_contract_order_and_de_duplicated()
    {
        // Order must not depend on what the caller happened to send: two identical requests would otherwise produce
        // documents that compare unequal.
        var request = new KundaliReportRequest(KundaliReportVariants.Detailed,
            [KundaliSections.Dashas, KundaliSections.Overview, KundaliSections.Dashas]);

        var validated = KundaliReportRequestValidator.Validate(request);

        Assert.Equal([KundaliSections.Overview, KundaliSections.Dashas], validated.Sections);
    }

    [Fact]
    public void An_unknown_variant_is_refused()
    {
        var request = new KundaliReportRequest("extended", [KundaliSections.Overview]);
        Assert.Throws<ValidationException>(() => KundaliReportRequestValidator.Validate(request));
    }

    [Fact]
    public void An_empty_selection_is_a_caller_error_and_never_means_everything()
    {
        var request = new KundaliReportRequest(KundaliReportVariants.Short, []);
        Assert.Throws<ValidationException>(() => KundaliReportRequestValidator.Validate(request));
    }

    [Fact]
    public void A_section_this_contract_does_not_define_is_refused_as_unknown()
    {
        var request = new KundaliReportRequest(KundaliReportVariants.Short, [KundaliSections.Overview, "revenge"]);
        var error = Assert.Throws<ValidationException>(() => KundaliReportRequestValidator.Validate(request));

        Assert.Contains("not defined", error.PublicMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_defined_section_this_variant_does_not_render_is_refused_as_unsupported_not_unknown()
    {
        // The distinction is the point: telling a caller with a valid id "unknown" would send them to fix a typo that
        // does not exist, and silently dropping it would produce a document missing a section they asked for.
        var request = new KundaliReportRequest(KundaliReportVariants.Short, [KundaliSections.Charts]);
        Assert.True(KundaliReportSections.IsDefined(KundaliSections.Charts));
        Assert.False(KundaliReportSections.IsSupported(KundaliReportVariants.Short, KundaliSections.Charts));

        var error = Assert.Throws<ValidationException>(() => KundaliReportRequestValidator.Validate(request));
        Assert.Contains("does not render", error.PublicMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_supported_section_is_also_a_defined_section()
    {
        // A catalogue that supports an id the contract does not define would let the server accept what a client cannot
        // name. One direction only: defined-but-unsupported is legitimate.
        foreach (var variant in KundaliReportVariants.All)
            foreach (var section in KundaliReportSections.For(variant))
                Assert.True(KundaliReportSections.IsDefined(section),
                    $"{variant} advertises '{section}', which KundaliSections does not define.");
    }

    [Fact]
    public void The_short_variant_renders_fewer_sections_than_the_detailed_one()
    {
        // Pins the difference the two variants exist to express, so "short" cannot drift into "detailed with a
        // different name".
        Assert.True(KundaliReportSections.Short.Count < KundaliReportSections.Detailed.Count);
        Assert.All(KundaliReportSections.Short,
            section => Assert.Contains(section, KundaliReportSections.Detailed));
    }
}
