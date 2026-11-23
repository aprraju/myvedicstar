using Microsoft.Extensions.Logging;
using MyVedicDay.Ai;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Integration;

/// <summary>Facts for one domain, plus every factor the domain is allowed to cite.</summary>
/// <param name="SourceRevision">The profile revision these facts came from, echoed so a stored interpretation is
/// attributable.</param>
/// <param name="Locale">The locale the prose is written in.</param>
/// <param name="Domain">One of <see cref="KundaliInterpretationDomains"/>.</param>
/// <param name="Factors">The factors, assembled by <see cref="InterpretationFactors"/> from the engine result.</param>
public sealed record InterpretationRequest(
    long SourceRevision,
    string Locale,
    string Domain,
    IReadOnlyList<KundaliInterpretationFactor> Factors);

/// <summary>What the adapter produced for one domain.</summary>
/// <param name="Verdict">The verdict after the guardrail, or null when narration was not attempted.</param>
/// <param name="SkippedReason">Set when narration was not attempted at all (provider disabled, no factors). Null when
/// <paramref name="Verdict"/> already carries the reason.</param>
public sealed record InterpretationOutcome(InterpretationVerdict? Verdict, string? SkippedReason);

/// <summary>
/// KUN-14 interpretation narration over the existing AI integration (api#54). It asks the model for prose and factor
/// references, then hands the answer to <see cref="InterpretationRule"/>: it never publishes model output directly, and
/// it never invents a factor.
/// </summary>
/// <remarks>
/// <para><b>Failure is a state, not an exception.</b> A provider timeout, a disabled provider, an open circuit or a
/// guardrail refusal all return an <see cref="InterpretationOutcome"/>. No path here throws for an AI problem, because
/// the calculation sections of the same response must stay available: a Kundali that fails entirely because a language
/// model was slow is the defect the parent refinement forbids.</para>
///
/// <para><b>No factor is generated here.</b> The factors come from the caller, which reads them from the engine result.
/// If they are empty the adapter reports <c>not-evaluated</c> with <c>input-missing</c> rather than asking the model to
/// describe a chart it was not given.</para>
/// </remarks>
public sealed class KundaliInterpretationAdapter : IKundaliInterpretationNarrator
{
    /// <summary>Prompt version this build sends. Stamped onto the section so stored text is attributable.</summary>
    public const string PromptVersion = "kundali-interpretation-2026-11-a";

    /// <summary>The domain-level instruction sent with every request.</summary>
    internal const string Instructions =
        "Write a short traditional-astrology interpretation for the requested life domain, using ONLY the structured "
        + "factors supplied. Do not name any planet, house or sign that is not in the factors. Do not give medical "
        + "diagnoses, treatments, prescriptions or guarantees of outcomes. Use encouraging, non-directive language. "
        + "Return the prose and the list of factor ids it relies on.";

    private readonly IStructuredTextGenerator _ai;
    private readonly ILogger<KundaliInterpretationAdapter>? _logger;

    public KundaliInterpretationAdapter(IStructuredTextGenerator ai,
        ILogger<KundaliInterpretationAdapter>? logger = null)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _logger = logger;
    }

    /// <summary>True when a provider is configured; false makes every domain <c>not-evaluated</c> with
    /// <c>ai-disabled</c>.</summary>
    public bool IsEnabled => _ai.IsEnabled;

    /// <summary>The provider/model fingerprint, reported as model provenance.</summary>
    public string Fingerprint => _ai.Fingerprint;

    /// <summary>
    /// Port implementation used by the facade (api#54). Thin: it builds the request from the factors the facade already
    /// selected, and returns the guardrailed verdict. Kept separate from <see cref="InterpretAsync"/> so the port can be
    /// faked in business tests without the AI assembly.
    /// </summary>
    public Task<InterpretationVerdict> NarrateAsync(string domain, string locale, long sourceRevision,
        IReadOnlyList<KundaliInterpretationFactor> factors, CancellationToken token) =>
        InterpretAsync(new InterpretationRequest(sourceRevision, locale, domain, factors), token)
            .ContinueWith(task =>
            {
                var outcome = task.GetAwaiter().GetResult();
                return outcome.Verdict
                    ?? InterpretationRule.NotEvaluated(domain,
                        outcome.SkippedReason ?? KundaliInterpretationReasons.InputMissing);
            }, token, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>
    /// Narrates one domain. Returns <c>not-evaluated</c> when the provider is disabled or when there are no factors to
    /// attribute prose to, and <c>withheld</c> when the guardrail refuses or the provider fails.
    /// </summary>
    public async Task<InterpretationOutcome> InterpretAsync(InterpretationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!KundaliInterpretationDomains.IsKnown(request.Domain))
            throw new ArgumentOutOfRangeException(nameof(request), request.Domain,
                "Unknown interpretation domain. The closed set is in KundaliInterpretationDomains.");

        if (request.Factors.Count == 0)
            return new InterpretationOutcome(null, KundaliInterpretationReasons.InputMissing);

        if (!_ai.IsEnabled)
            return new InterpretationOutcome(null, KundaliInterpretationReasons.AiDisabled);

        var known = request.Factors.ToDictionary(factor => factor.Id, factor => factor, StringComparer.Ordinal);

        var input = string.Join('\n', request.Factors.Select(factor => $"{factor.Id}: {factor.Description}"));
        var aiRequest = new AiRequest($"kundali.interpret.{request.Domain}", Instructions,
            $"domain: {request.Domain}\nlocale: {request.Locale}\nfactors:\n{input}", PromptVersion);

        var response = await _ai.GenerateAsync<InterpretationDraft>(aiRequest, cancellationToken);
        if (response.Outcome != AiOutcome.Succeeded || response.Value is null)
        {
            _logger?.LogInformation(
                "Kundali interpretation for {Domain} was not generated: {Outcome}", request.Domain, response.Outcome);
            return new InterpretationOutcome(
                InterpretationRule.Withheld(request.Domain, KundaliInterpretationReasons.GenerationFailed),
                SkippedReason: null);
        }

        var verdict = InterpretationRule.Apply(request.Domain, response.Value, known);
        if (verdict.UnknownFactorReferences.Count > 0)
        {
            _logger?.LogWarning(
                "Kundali interpretation for {Domain} cited {Count} unknown factor(s); its attribution was reduced.",
                request.Domain, verdict.UnknownFactorReferences.Count);
        }

        return new InterpretationOutcome(verdict, SkippedReason: null);
    }

    /// <summary>
    /// Builds the section from one outcome per domain. Every domain in the closed set appears exactly once, whether or
    /// not narration ran, so a domain cannot go missing from the payload.
    /// </summary>
    public static KundaliInterpretationsSection Compose(
        long sourceRevision,
        string locale,
        string modelProvenance,
        IReadOnlyDictionary<string, InterpretationOutcome> byDomain)
    {
        ArgumentNullException.ThrowIfNull(byDomain);

        var entries = new List<KundaliInterpretationEntry>(KundaliInterpretationDomains.All.Count);
        foreach (var domain in KundaliInterpretationDomains.All)
        {
            if (!byDomain.TryGetValue(domain, out var outcome) || outcome.Verdict is null)
            {
                entries.Add(new KundaliInterpretationEntry(
                    domain,
                    KundaliInterpretationStatuses.NotEvaluated,
                    Text: null,
                    Factors: [],
                    Reason: outcome?.SkippedReason ?? KundaliInterpretationReasons.InputMissing,
                    UnknownFactorReferences: []));
                continue;
            }

            entries.Add(outcome.Verdict.ToEntry());
        }

        return new KundaliInterpretationsSection(
            sourceRevision,
            PromptVersion,
            modelProvenance,
            locale,
            "traditional interpretation",
            entries,
            Unsupported: []);
    }
}
