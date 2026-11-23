using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// Builds the KUN-14 interpretation payload (api#54) so that every domain in the closed set is ENUMERATED exactly once,
/// with its own status and its own reason, and the three states a domain can be in stay distinguishable as data.
/// </summary>
/// <remarks>
/// <para><b>Why this exists as a separate, endpoint-free type.</b> The payload shape is the expensive thing to get wrong:
/// an endpoint is cheap to add once the shape is right, and expensive to change once a client binds to it. This type is
/// therefore testable on its own with no controller, no database and no model.</para>
///
/// <para><b>Absent is not a state.</b> The builder iterates <see cref="KundaliInterpretationDomains.All"/>, never the keys
/// it was handed. A domain whose verdict is missing, or whose narration was skipped, is emitted as
/// <c>not-evaluated</c> with the reason that was supplied - it is never dropped from the list. An omitted domain would be
/// indistinguishable from a domain with nothing to say, which is the collapse this issue exists to prevent.</para>
///
/// <para><b>Assessed-and-failed vs never-assessed.</b> A domain where narration ran and the guardrail or the provider
/// refused is <c>withheld</c> with its own reason (<c>content-rejected</c>, <c>generation-failed</c>, or a capability
/// gap). A domain that was never attempted is <c>not-evaluated</c> with its reason (<c>ai-disabled</c>,
/// <c>input-missing</c>). The two carry different statuses, so a client can tell them apart without reading prose, and a
/// test asserts the statuses differ rather than that a reason string is merely present.</para>
/// </remarks>
public static class InterpretationSection
{
    /// <summary>The label every interpretation payload carries, so the text is never presented as fact without its
    /// framing.</summary>
    public const string TraditionalLabel = "traditional interpretation";

    /// <summary>
    /// Composes the section from one verdict per domain. Missing domains become <c>not-evaluated</c> entries, never
    /// omissions.
    /// </summary>
    /// <param name="sourceRevision">The profile revision the facts came from, so a stored section is attributable.</param>
    /// <param name="locale">The locale the prose is written in.</param>
    /// <param name="promptVersion">The prompt version that produced the text; part of provenance.</param>
    /// <param name="modelProvenance">Provider/model that produced the text, or a value naming why no model ran.</param>
    /// <param name="verdicts">The verdicts produced this run, by domain.</param>
    /// <param name="missingDomainReasons">The reason to record per domain that has no verdict. A map, not one string:
    /// "why is this domain missing" is answerable per domain (one may be disabled, another may lack inputs), and a single
    /// value would have to be the lowest common denominator and would misreport every case it does not fit. A domain
    /// absent from this map is recorded as <c>input-missing</c>, which is the honest default: no verdict was produced
    /// because our inputs were insufficient, unless the caller says otherwise.</param>
    /// <param name="unsupported">Section-level notes, for example a section-wide capability gap. Per-domain reasons remain
    /// authoritative; this list is additive and never a substitute.</param>
    public static KundaliInterpretationsSection Compose(
        long sourceRevision,
        string locale,
        string promptVersion,
        string modelProvenance,
        IReadOnlyDictionary<string, InterpretationVerdict> verdicts,
        IReadOnlyDictionary<string, string>? missingDomainReasons = null,
        IReadOnlyList<KundaliUnsupportedField>? unsupported = null)
    {
        ArgumentNullException.ThrowIfNull(verdicts);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelProvenance);

        var entries = new List<KundaliInterpretationEntry>(KundaliInterpretationDomains.All.Count);
        foreach (var domain in KundaliInterpretationDomains.All)
        {
            if (verdicts.TryGetValue(domain, out var verdict))
            {
                // A verdict filed under the wrong domain would attribute one domain's text to another; refuse rather
                // than quietly republishing it under the key it arrived with.
                if (!string.Equals(verdict.Domain, domain, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"A verdict for '{verdict.Domain}' was filed under '{domain}'. Refusing to publish an " +
                        "interpretation attributed to a domain it was not produced for.");

                entries.Add(verdict.ToEntry());
                continue;
            }

            var reason = missingDomainReasons is not null
                && missingDomainReasons.TryGetValue(domain, out var supplied)
                && !string.IsNullOrWhiteSpace(supplied)
                    ? supplied
                    : KundaliInterpretationReasons.InputMissing;

            entries.Add(new KundaliInterpretationEntry(
                domain,
                KundaliInterpretationStatuses.NotEvaluated,
                Text: null,
                Factors: [],
                Reason: reason,
                UnknownFactorReferences: []));
        }

        return new KundaliInterpretationsSection(
            sourceRevision,
            promptVersion,
            modelProvenance,
            locale,
            TraditionalLabel,
            entries,
            unsupported ?? []);
    }

    /// <summary>
    /// The varshaphal part of the same section surface. It has exactly one shape and always says not-available: the
    /// engine has no annual chart, so there is nothing to interpret and nothing to invent.
    /// </summary>
    public static KundaliVarshaphalSection Varshaphal() => KundaliVarshaphalSection.NotAvailable;
}
