namespace MyVedicStar.Kundali.Business;

/// <summary>
/// The single seam where a short-report document is turned into bytes (api#57).
/// </summary>
/// <remarks>
/// <para><b>Named, singular, and refusing.</b> There is exactly one place a PDF library is ever used, so the licence
/// review, the supply-chain entry and the format specification happen once and in one diff. Until that happens this
/// class throws with the reason, and its <see cref="CanProduceBytes"/> is false so
/// <see cref="KundaliShortReportRenderer.IsAvailable"/> reports the host honestly.</para>
/// <para><b>Why it throws rather than returning a placeholder.</b> The same reason the Horizons fetch port refuses
/// rather than returning zeros: a document that exists but contains nothing real is worse than no document, because
/// every downstream assertion about "a report was produced" starts passing while nothing has been rendered. The failure
/// has to stay visible until a real producer exists.</para>
/// <para><b>What a future implementation owes this contract, stated here so it cannot be discovered later:</b></para>
/// <list type="number">
/// <item>Render exactly <see cref="KundaliShortReportPlan.Sections"/>, in that order, and no other section's content.</item>
/// <item>Print every entry in <see cref="KundaliShortReportPlan.WithheldSections"/> as a visible statement in the
/// document. A withheld section must be readable as withheld, not absent.</item>
/// <item>Return a reference this host can later resolve; do not store the document here (retention is out of scope for
/// api#57 and lives with the lifecycle owner).</item>
/// <item>Add its package with the licence named in the PR body that introduces it - not in a later cleanup.</item>
/// </list>
/// </remarks>
public abstract class KundaliReportPdfBytes
{
    /// <summary>
    /// True when a real byte producer is configured. False makes the renderer report `renderer-unavailable`, which is a
    /// fact the caller can act on rather than a document that never arrives.
    /// </summary>
    public virtual bool CanProduceBytes => false;

    /// <summary>
    /// Produces the document and returns its reference.
    /// </summary>
    /// <exception cref="KundaliReportRenderingUnavailableException">No byte producer is configured. The default
    /// implementation throws this; a real producer overrides both this and <see cref="CanProduceBytes"/>.</exception>
    public virtual Task<string> ProduceAsync(KundaliShortReportPlan plan, CancellationToken token) =>
        throw new KundaliReportRenderingUnavailableException(
            "No Kundali report byte producer is configured. api#57 deliberately ships no PDF library: the format is "
            + "unspecified and the licence review is not done. Implementing this seam is the whole of the remaining work, "
            + "and the plan handed to it is the complete description of the document to produce.");
}

/// <summary>
/// Raised when the byte seam is reached with no producer behind it. Named so a test asserts the refusal specifically
/// rather than any exception, which a NullReference would otherwise satisfy.
/// </summary>
public sealed class KundaliReportRenderingUnavailableException(string detail) : Exception(detail);
