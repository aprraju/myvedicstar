using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using Xunit;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// api#57. The short-report renderer's provable half: the section set it selects, and the withheld-section statement it
/// builds - asserted at the byte seam this class owns, not on a PDF nobody can render yet.
/// </summary>
/// <remarks>
/// <para>Every assertion here is about the <see cref="KundaliShortReportPlan"/> handed to the seam. That is deliberate:
/// the byte-level properties cannot be proven while no producer exists, and a test that asserted on a stubbed PDF would
/// make a gate look green that had never run.</para>
/// </remarks>
public sealed class KundaliShortReportRendererTests
{
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Profile = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static KundaliShortReportPlan Plan(params string[] sections) =>
        KundaliShortReportPlan.For(new KundaliReportRequest(KundaliReportVariants.Short, sections),
            Account, Profile, sourceRevision: 41, Now);

    [Fact]
    public void The_short_set_is_the_contracted_three_sections_and_nothing_else()
    {
        var plan = Plan(KundaliSections.Overview, KundaliSections.Planets, KundaliSections.Dashas);

        Assert.Equal(KundaliReportSections.Short, plan.Sections);
        Assert.Equal([KundaliSections.Overview, KundaliSections.Planets, KundaliSections.Dashas], plan.Sections);
    }

    [Fact]
    public void A_section_the_short_variant_does_not_render_is_never_in_the_rendered_set()
    {
        // Even when the caller asks for it. The variant is a selection rule, not a label on the request.
        var plan = Plan(KundaliSections.Overview, KundaliSections.Charts, KundaliSections.Bala);

        Assert.Equal([KundaliSections.Overview], plan.Sections);
        Assert.DoesNotContain(KundaliSections.Charts, plan.Sections);
        Assert.DoesNotContain(KundaliSections.Bala, plan.Sections);
    }

    [Fact]
    public void Every_withheld_section_is_stated_with_a_reason_rather_than_omitted()
    {
        var plan = Plan(KundaliSections.Overview, KundaliSections.Planets, KundaliSections.Dashas);

        // The six the short variant does not render, all named.
        var shortSet = KundaliReportSections.Short;
        var expectedWithheld = KundaliSections.All.Where(s => !shortSet.Contains(s, StringComparer.Ordinal));

        Assert.Equal(expectedWithheld, plan.WithheldSections.Select(w => w.Path));

        Assert.All(plan.WithheldSections, withheld =>
        {
            Assert.False(string.IsNullOrWhiteSpace(withheld.Reason),
                $"Withheld section '{withheld.Path}' states no reason, so a reader cannot tell it was withheld rather than lost.");
        });
    }

    [Fact]
    public void A_withheld_section_is_never_also_reported_as_rendered()
    {
        // The two lists must partition the catalogue: a section in both would make the statement self-contradictory.
        var plan = Plan(KundaliSections.Overview);

        Assert.Empty(plan.Sections.Intersect(plan.WithheldSections.Select(w => w.Path), StringComparer.Ordinal));
        // Partition is exhaustive only relative to what this plan speaks about: rendered + withheld = the whole
        // catalogue minus nothing, since withheld is built from every section the SHORT variant does not render.
        Assert.Equal(KundaliSections.All.Count - KundaliReportSections.Short.Count,
            plan.WithheldSections.Count);
        Assert.True(plan.Sections.Count + plan.WithheldSections.Count <= KundaliSections.All.Count);
    }

    [Fact]
    public void Rendered_sections_follow_contract_order_not_request_order()
    {
        var forward = Plan(KundaliSections.Overview, KundaliSections.Planets, KundaliSections.Dashas);
        var reversed = Plan(KundaliSections.Dashas, KundaliSections.Planets, KundaliSections.Overview);

        Assert.Equal(forward.Sections, reversed.Sections);
    }

    [Fact]
    public async Task With_no_byte_producer_the_renderer_reports_unavailable_and_produces_no_document()
    {
        var renderer = new KundaliShortReportRenderer(new NoBytes(), TimeProvider.System);

        Assert.False(renderer.IsAvailable);

        var result = await renderer.RenderAsync(Account, Profile,
            new KundaliReportRequest(KundaliReportVariants.Short, KundaliReportSections.Short), 41, default);

        Assert.False(result.Succeeded);
        Assert.Null(result.DocumentReference);
        Assert.Equal(KundaliShortReportRenderer.RendererUnavailable, result.FailureCode);
    }

    [Fact]
    public async Task The_byte_seam_refuses_by_name_rather_than_returning_a_placeholder()
    {
        // The seam's default is a refusal. A placeholder document here would make "a report was produced" pass while
        // nothing had been rendered - the exact failure the port was left unimplemented to avoid.
        await Assert.ThrowsAsync<KundaliReportRenderingUnavailableException>(() =>
            new NoBytes().ProduceAsync(Plan(KundaliSections.Overview), default));
    }

    [Fact]
    public async Task With_a_producer_the_plan_reaches_it_whole_and_the_reference_is_returned()
    {
        // Proves the seam is wired, not that bytes are meaningful: the producer is a test double that records the plan.
        var producer = new RecordingBytes();
        var renderer = new KundaliShortReportRenderer(producer, TimeProvider.System);

        var result = await renderer.RenderAsync(Account, Profile,
            new KundaliReportRequest(KundaliReportVariants.Short, [KundaliSections.Overview, KundaliSections.Charts]),
            41, default);

        Assert.True(result.Succeeded);
        Assert.Equal("reference-from-producer", result.DocumentReference);
        Assert.NotNull(producer.Seen);

        Assert.Equal([KundaliSections.Overview], producer.Seen!.Sections);
        Assert.Contains(producer.Seen.WithheldSections, w => w.Path == KundaliSections.Charts);
        Assert.Equal(41, producer.Seen.SourceRevision);
        Assert.Equal(Account, producer.Seen.AccountId);
        Assert.Equal(Profile, producer.Seen.ProfileId);
    }

    private sealed class NoBytes : KundaliReportPdfBytes;

    private sealed class RecordingBytes : KundaliReportPdfBytes
    {
        public override bool CanProduceBytes => true;
        public KundaliShortReportPlan? Seen { get; private set; }

        public override Task<string> ProduceAsync(KundaliShortReportPlan plan, CancellationToken token)
        {
            Seen = plan;
            return Task.FromResult("reference-from-producer");
        }
    }
}
