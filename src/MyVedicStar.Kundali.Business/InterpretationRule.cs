using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>One interpretation as the model produced it, before any guardrail has been applied.</summary>
/// <param name="Text">The prose.</param>
/// <param name="FactorIds">The factor ids the prose claims to rest on.</param>
public sealed record InterpretationDraft(string Text, IReadOnlyList<string> FactorIds);

/// <summary>
/// The production-time rule for interpretations (KUN-14, api#54): a published interpretation may only refer to factors
/// that exist in the input it was given, and it must not make a claim the product refuses to make.
/// </summary>
/// <remarks>
/// <para><b>Why this is a rule in Business and not prompt text.</b> An instruction to the model is a request; this is the
/// check that runs after the request regardless of what came back. A model that cites an invented factor, or writes a
/// diagnosis, is not hypothetical: it is the expected failure mode, and the product's answer has to be deterministic
/// rather than hopeful.</para>
///
/// <para><b>Unknown references are stripped and reported.</b> A citation that does not exist in the input cannot support
/// a sentence, so the interpretation is withheld with <c>no-attributable-factors</c> when nothing remains, and when part
/// of it survives the surviving part is published with the unknown ids listed in
/// <see cref="InterpretationVerdict.UnknownFactorReferences"/>. Either way the run leaves evidence.</para>
///
/// <para><b>Never a whole-section error.</b> A rejected domain reports <c>withheld</c> with its own reason while every
/// other domain stays available, matching the rule that an AI failure is reported per section.</para>
/// </remarks>
public static class InterpretationRule
{
    /// <summary>
    /// Phrases that turn an interpretation into a medical claim or a promise of outcome. Matched case-insensitively as
    /// substrings, deliberately blunt: a false positive withholds one domain and says why, while a false negative
    /// publishes a diagnosis.
    /// </summary>
    private static readonly string[] RefusedClaims =
    [
        "diagnos", "cure", "treat", "prescri", "medication", "guaranteed", "guarantee",
        "will definitely", "certain to", "always will", "incurable",
    ];

    /// <summary>
    /// Applies the rule to a draft for one domain.
    /// </summary>
    /// <param name="domain">The domain being produced; echoed onto the verdict so a caller cannot mis-file it.</param>
    /// <param name="draft">The model output.</param>
    /// <param name="knownFactors">Every factor the engine supplied for this revision, by id. The draft may cite only
    /// these.</param>
    /// <returns>
    /// A verdict carrying either a publishable entry or the reason it was withheld. Never null and never an exception:
    /// a rule that throws on hostile model output turns a bad paragraph into a failed request.
    /// </returns>
    public static InterpretationVerdict Apply(
        string domain,
        InterpretationDraft draft,
        IReadOnlyDictionary<string, KundaliInterpretationFactor> knownFactors)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(knownFactors);
        RequireKnownDomain(domain);

        var text = draft.Text?.Trim() ?? string.Empty;
        if (ContainsRefusedClaim(text))
            return InterpretationVerdict.Withheld(domain, KundaliInterpretationReasons.ContentRejected, []);

        var known = new List<KundaliInterpretationFactor>();
        var unknown = new List<string>();
        foreach (var id in draft.FactorIds.Distinct(StringComparer.Ordinal))
        {
            if (knownFactors.TryGetValue(id, out var factor))
                known.Add(factor);
            else
                unknown.Add(id);
        }

        if (text.Length == 0 || known.Count == 0)
            return InterpretationVerdict.Withheld(domain, KundaliInterpretationReasons.NoAttributableFactors,
                unknown.ToArray());

        return new InterpretationVerdict(
            domain,
            KundaliInterpretationStatuses.Ready,
            text,
            known,
            Reason: null,
            unknown.ToArray());
    }

    /// <summary>
    /// A domain that is deliberately withheld for a product reason while the chart itself was produced - a capability
    /// gap or a destination the deployment does not publish.
    /// </summary>
    public static InterpretationVerdict Withheld(string domain, string reason)
    {
        RequireKnownDomain(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return InterpretationVerdict.Withheld(domain, reason, []);
    }

    /// <summary>
    /// A domain nothing was assessed for. Named separately from <see cref="Withheld"/> so a caller cannot express
    /// "assessed but unpublished" and "never looked at" with the same call: that distinction is the contract.
    /// </summary>
    public static InterpretationVerdict NotEvaluated(string domain, string reason)
    {
        RequireKnownDomain(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new InterpretationVerdict(domain, KundaliInterpretationStatuses.NotEvaluated, null, [], reason, []);
    }

    private static void RequireKnownDomain(string domain)
    {
        if (!KundaliInterpretationDomains.IsKnown(domain))
            throw new ArgumentOutOfRangeException(nameof(domain), domain,
                "Unknown interpretation domain. The closed set is in KundaliInterpretationDomains.");
    }

    private static bool ContainsRefusedClaim(string text) =>
        RefusedClaims.Any(candidate => text.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The outcome of <see cref="InterpretationRule"/> for one domain.</summary>
/// <param name="Domain">The domain.</param>
/// <param name="Status">One of <see cref="KundaliInterpretationStatuses"/>.</param>
/// <param name="Text">The publishable prose, or null when the domain is not ready.</param>
/// <param name="Factors">The factors the prose is attributed to.</param>
/// <param name="Reason">Why the domain is not ready; null when ready.</param>
/// <param name="UnknownFactorReferences">Factor ids the draft cited that do not exist in the input.</param>
public sealed record InterpretationVerdict(
    string Domain,
    string Status,
    string? Text,
    IReadOnlyList<KundaliInterpretationFactor> Factors,
    string? Reason,
    IReadOnlyList<string> UnknownFactorReferences)
{
    /// <summary>A withheld domain, with the reason and any unknown references that caused it.</summary>
    public static InterpretationVerdict Withheld(string domain, string reason,
        IReadOnlyList<string> unknownFactorReferences) =>
        new(domain, KundaliInterpretationStatuses.Withheld, null, [], reason, unknownFactorReferences);

    /// <summary>Projects the verdict onto the wire contract.</summary>
    public KundaliInterpretationEntry ToEntry() =>
        new(Domain, Status, Text, Factors, Reason, UnknownFactorReferences);
}
