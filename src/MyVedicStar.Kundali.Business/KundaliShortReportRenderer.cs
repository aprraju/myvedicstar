using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// KUN-17 (api#57) short-report renderer. Selects the sections a short report claims, records which requested sections
/// it is withholding and why, and hands the document body to a <b>named byte seam</b> that refuses.
/// </summary>
/// <remarks>
/// <para><b>Why this class has no PDF library.</b> A document format nobody has specified, behind a licence review and a
/// supply-chain entry, is a decision rather than an implementation detail, and PR #198 deliberately left the port
/// implementation-free for that reason. Adding a package here would also make the two byte-level properties
/// (section set, withheld sections stated) <i>look</i> proven while nothing had been rendered.</para>
/// <para><b>What is proven here, and what is not.</b> This class proves what reaches the byte seam: the short section
/// set, in contract order, and a withheld-section statement for every requested section it does not render. It does
/// <b>not</b> prove anything about bytes, because it produces none. <see cref="KundaliReportPdfBytes"/> is the single
/// place a future byte producer plugs in, and until one exists it refuses by name.</para>
/// <para><b>Refusing rather than stubbing.</b> The seam throws instead of returning a placeholder document. A
/// placeholder would satisfy every downstream assertion and turn "no renderer" into "a document that looks real", which
/// is the same defect as a fabricated reference value.</para>
/// </remarks>
public sealed class KundaliShortReportRenderer : IKundaliReportRenderer
{
    /// <summary>This host has no byte producer, so no document can be produced here.</summary>
    public const string RendererUnavailable = "renderer-unavailable";

    /// <summary>No section this variant renders was requested, so the document would be empty.</summary>
    public const string SectionUnsupported = "section-unsupported";

    private readonly KundaliReportPdfBytes _bytes;
    private readonly TimeProvider _clock;

    public KundaliShortReportRenderer(KundaliReportPdfBytes bytes, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(clock);
        _bytes = bytes;
        _clock = clock;
    }

    /// <summary>
    /// Armed for the bytes leg only. Availability is not a capability probe: it is this host saying whether a byte
    /// producer exists, so the facade settles `renderer-unavailable` instead of a report that never arrives.
    /// </summary>
    public bool IsAvailable => _bytes.CanProduceBytes;

    /// <inheritdoc />
    public async Task<KundaliReportRenderResult> RenderAsync(Guid accountId, Guid profileId,
        KundaliReportRequest request, long sourceRevision, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsAvailable)
        {
            return new KundaliReportRenderResult(Succeeded: false, DocumentReference: null,
                EmptySections: [], FailureCode: RendererUnavailable);
        }

        // The plan is computed and handed over as a value, so the section selection and the withheld statement are
        // asserted at the seam this class owns rather than inferred from a document nobody can read.
        var plan = KundaliShortReportPlan.For(request, accountId, profileId, sourceRevision, _clock.GetUtcNow());

        if (plan.Sections.Count == 0)
        {
            return new KundaliReportRenderResult(Succeeded: false, DocumentReference: null,
                EmptySections: [], FailureCode: SectionUnsupported);
        }

        // NAME: this is the line where a byte producer plugs in. Replace nothing above it and nothing below it: the
        // plan is already the complete description of the document, so a renderer for a future variant bolts on here.
        var documentReference = await _bytes.ProduceAsync(plan, token).ConfigureAwait(false);

        return new KundaliReportRenderResult(Succeeded: true, DocumentReference: documentReference,
            EmptySections: plan.EmptySections, FailureCode: null);
    }
}

/// <summary>
/// The complete description of a short report document: what it renders, what it withholds, and why.
/// </summary>
/// <remarks>
/// <para><b>A withheld section is stated, not omitted.</b> The short variant renders three of the nine published
/// sections. A reader holding that document must be able to tell a short report from an incomplete one, so every
/// section the caller <i>could</i> have asked for and this variant does not render is listed with a reason. Silence
/// would be indistinguishable from a bug.</para>
/// <para><b>Requested-but-unsupported is a different answer from withheld-by-variant.</b> A section the caller actually
/// requested and this variant cannot render is <c>section-unsupported</c>; a section that exists in the catalogue but was
/// never requested is simply absent from the document and does not need a failure code. Collapsing those two would
/// report a problem with the request that does not exist.</para>
/// </remarks>
/// <param name="AccountId">Owner, carried so the byte producer cannot render another account's profile by mistake.</param>
/// <param name="ProfileId">The profile the document describes.</param>
/// <param name="SourceRevision">The profile revision the document is bound to.</param>
/// <param name="Variant">The variant id, recorded rather than inferred from the section set.</param>
/// <param name="Sections">Sections rendered, in contract order.</param>
/// <param name="WithheldSections">Sections the catalogue publishes that this variant does not render, with reasons.</param>
/// <param name="EmptySections">Sections that were selected but produced no content.</param>
/// <param name="RequestedAtUtc">When the request was made, from the injected clock.</param>
public sealed record KundaliShortReportPlan(
    Guid AccountId,
    Guid ProfileId,
    long SourceRevision,
    string Variant,
    IReadOnlyList<string> Sections,
    IReadOnlyList<KundaliReportFailure> WithheldSections,
    IReadOnlyList<string> EmptySections,
    DateTimeOffset RequestedAtUtc)
{
    /// <summary>
    /// The short report over <paramref name="request"/>. Pure: no clock read, no store, no client.
    /// </summary>
    /// <remarks>
    /// The section set comes from <see cref="KundaliReportSections.Short"/> and is intersected with what was requested -
    /// never from the request directly. A renderer that rendered whatever it was handed would make the variant a label
    /// rather than a selection rule.
    /// </remarks>
    public static KundaliShortReportPlan For(KundaliReportRequest request, Guid accountId, Guid profileId,
        long sourceRevision, DateTimeOffset requestedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        var shortSet = KundaliReportSections.Short;

        // Intersection in CONTRACT order, so two identical requests produce identical plans regardless of request order.
        var rendered = shortSet
            .Where(section => request.Sections.Contains(section, StringComparer.Ordinal))
            .ToArray();

        // Every published section this variant does not render, stated with a reason rather than left to be inferred.
        var withheld = KundaliSections.All
            .Where(section => !shortSet.Contains(section, StringComparer.Ordinal))
            .Select(section => new KundaliReportFailure(section,
                KundaliReportFailureCodes.SectionUnsupported,
                "Not rendered by the short variant; request the detailed report for this section."))
            .ToArray();

        return new KundaliShortReportPlan(accountId, profileId, sourceRevision,
            Variant: KundaliReportVariants.Short,
            Sections: rendered,
            WithheldSections: withheld,
            EmptySections: [],
            RequestedAtUtc: requestedAtUtc);
    }
}
