using System.Text.Json.Serialization;

namespace MyVedicStar.Kundali.Shared;

/// <summary>
/// KUN-14 interpretation section (api#54). One entry per domain, each carrying its own completeness, its own reason, and
/// the factors the text is allowed to refer to.
/// </summary>
/// <remarks>
/// <para><b>Why per-entry completeness and not a section footer.</b> A section-level caveat lets a caller render the
/// ready entries and never learn that another domain was never evaluated. The states a domain can be in are therefore
/// repeated on every entry, and the withheld and not-evaluated entries are enumerated one per domain rather than
/// omitted.</para>
///
/// <para><b>The distinction this contract exists to keep.</b> <c>withheld</c> means the engine evaluated the chart but the
/// product does not publish an interpretation of it (a capability gap or a refused claim). <c>not-evaluated</c> means
/// nothing was assessed at all. Collapsing the two into "absent" is the defect: absence cannot tell a reader whether the
/// astrologer looked and declined or never looked.</para>
///
/// <para><b>No varshaphal payload is invented here.</b> The engine has no annual chart, so
/// <see cref="KundaliVarshaphalSection"/> can only ever be the not-available branch. A field for annual-chart facts would
/// have to be filled with something, and a fabricated annual chart in a wire contract is a claim the product cannot
/// stand behind.</para>
/// </remarks>
public sealed record KundaliInterpretationsSection(
    long SourceRevision,
    string PromptVersion,
    string ModelProvenance,
    string Locale,
    string InterpretationLabel,
    IReadOnlyList<KundaliInterpretationEntry> Sections,
    IReadOnlyList<KundaliUnsupportedField> Unsupported);
/// <summary>One domain's interpretation, with the factors it was allowed to draw on.</summary>
/// <param name="Domain">One of <see cref="KundaliInterpretationDomains"/>.</param>
/// <param name="Status">One of <see cref="KundaliInterpretationStatuses"/>.</param>
/// <param name="Text">The interpretation, or null when <paramref name="Status"/> is not <c>ready</c>. Never a
/// placeholder sentence: a domain that is not ready says why in <paramref name="Reason"/> rather than in prose that
/// looks like content.</param>
/// <param name="Factors">Ids naming the structured facts the text refers to. Every id is present in the engine input for
/// this revision; anything the model cited that is not was stripped before this entry was published.</param>
/// <param name="Reason">Why the domain is not ready. Null exactly when <paramref name="Status"/> is
/// <c>ready</c>.</param>
/// <param name="UnknownFactorReferences">Factor ids the model cited that do not exist in the input. Reported, not
/// silently dropped: a run that cites invented facts is a quality signal about the prompt, not a property of the
/// response.</param>
public sealed record KundaliInterpretationEntry(
    string Domain,
    string Status,
    string? Text,
    IReadOnlyList<KundaliInterpretationFactor> Factors,
    string? Reason,
    IReadOnlyList<string> UnknownFactorReferences);

/// <summary>One structured factor the interpretation may refer to, identified by a stable id.</summary>
/// <param name="Id">Stable, machine-readable identifier (for example <c>graha:saturn:sign</c>). Never a display label.</param>
/// <param name="Description">A human-readable rendering of the factor, for clients that show the evidence.</param>
public sealed record KundaliInterpretationFactor(string Id, string Description);

/// <summary>The five interpretation domains KUN-14 publishes. Closed set: an unknown domain is rejected, never mapped
/// to a default.</summary>
public static class KundaliInterpretationDomains
{
    public const string Career = "career";
    public const string Marriage = "marriage";
    public const string Wellbeing = "wellbeing";
    public const string Finance = "finance";
    public const string Education = "education";

    public static readonly IReadOnlyList<string> All = [Career, Marriage, Wellbeing, Finance, Education];

    /// <summary>True when the text names a domain in the closed set.</summary>
    public static bool IsKnown(string? domain) =>
        domain is not null && All.Contains(domain, StringComparer.Ordinal);
}

/// <summary>The states an interpretation domain can be in. Closed set, with stable wire text.</summary>
public static class KundaliInterpretationStatuses
{
    /// <summary>An interpretation was produced for this domain.</summary>
    public const string Ready = "ready";

    /// <summary>The chart was produced, but no interpretation is published for this domain (capability gap, refused
    /// claim, or a failed narration).</summary>
    public const string Withheld = "withheld";

    /// <summary>Nothing was assessed: a required input or the provider was absent.</summary>
    public const string NotEvaluated = "not-evaluated";

    public static readonly IReadOnlyList<string> All = [Ready, Withheld, NotEvaluated];

    public static bool IsKnown(string? status) =>
        status is not null && All.Contains(status, StringComparer.Ordinal);
}

/// <summary>
/// Reasons an interpretation domain is not ready, as stable wire text.
/// </summary>
/// <remarks>
/// <c>withheld</c> and <c>not-evaluated</c> never share a reason string: the first is a statement about text that was
/// produced or attempted, the second about work that never happened. Reusing one text for both would put the
/// distinction back where this contract started.
/// </remarks>
public static class KundaliInterpretationReasons
{
    /// <summary>The AI narration failed for this domain on this run; the chart facts remain available.</summary>
    public const string GenerationFailed = "generation-failed";

    /// <summary>The AI provider is not enabled in this deployment.</summary>
    public const string AiDisabled = "ai-disabled";

    /// <summary>The text failed the content guardrail (diagnosis, treatment, guaranteed outcome).</summary>
    public const string ContentRejected = "content-rejected";

    /// <summary>Every factor the text cited was unknown, or the text was empty, so it could not be attributed to chart
    /// facts.</summary>
    public const string NoAttributableFactors = "no-attributable-factors";

    /// <summary>The profile's birth inputs are insufficient to assess this domain.</summary>
    public const string InputMissing = "input-missing";

    /// <summary>The domain needs a chart frame the engine does not publish yet.</summary>
    public const string CapabilityGap = "capability-gap";

    /// <summary>A domain in the closed set that this request did not ask about. Reported so the enumerable list never
    /// implies an assessment of a domain nobody requested; distinct from every withheld reason, which is a statement
    /// about work that did happen.</summary>
    public const string NotRequested = "not-requested";
}

/// <summary>
/// The varshaphal half of KUN-14, which the engine cannot serve: it has no annual chart.
/// </summary>
/// <remarks>
/// This record carries no annual-chart fields at all. Any field for solar-return facts, annual houses or year lords would
/// have to be populated with something, and the only thing available to populate it with would be invention. The
/// contract therefore has one shape - the not-available branch.
/// </remarks>
/// <param name="Available">Always false. Present so a client reads a capability statement instead of inferring one from
/// an absent object.</param>
/// <param name="Reason">The stable wire reason, from <see cref="KundaliVarshaphalReasons"/>.</param>
public sealed record KundaliVarshaphalSection(bool Available, string Reason)
{
    /// <summary>The only value this deployment can produce.</summary>
    public static KundaliVarshaphalSection NotAvailable { get; } =
        new(false, KundaliVarshaphalReasons.EngineHasNoAnnualChart);
}

/// <summary>Why the annual-chart surface is unavailable. A closed set, so a second cause is added deliberately rather
/// than as a new free-form string.</summary>
public static class KundaliVarshaphalReasons
{
    /// <summary>The calculation engine produces no annual (solar-return) chart, so no annual facts exist to
    /// interpret.</summary>
    public const string EngineHasNoAnnualChart = "engine-has-no-annual-chart";
}
