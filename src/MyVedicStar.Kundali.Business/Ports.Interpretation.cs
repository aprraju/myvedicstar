using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Business;

/// <summary>
/// KUN-14 narration: turn one domain's structured factors into prose, or say why it could not (api#54).
/// </summary>
/// <remarks>
/// <para><b>A port, not a dependency on the AI assembly.</b> Business stays free of the provider; the integration
/// project supplies an implementation over the existing <c>IStructuredTextGenerator</c>. That also makes the facade
/// testable without a model, which is the only way the withheld and not-evaluated paths can be asserted at all.</para>
///
/// <para><b>It may not throw for a provider problem.</b> A disabled provider, a timeout or a refused draft is a
/// <see cref="KundaliInterpretationStatuses"/> in the returned value, because the calculation sections of the same
/// response must stay available.</para>
/// </remarks>
public interface IKundaliInterpretationNarrator
{
    /// <summary>True when narration is possible in this deployment. False makes every domain
    /// <c>not-evaluated</c> with <c>ai-disabled</c>.</summary>
    bool IsEnabled { get; }

    /// <summary>Provider/model identity for provenance, or a value naming why no model ran.</summary>
    string Fingerprint { get; }

    /// <summary>The prompt version this build sends.</summary>
    string PromptVersion { get; }

    /// <summary>Narrates one domain from the factors the engine published, applying the attribution guardrail.</summary>
    Task<InterpretationVerdict> NarrateAsync(string domain, string locale, long sourceRevision,
        IReadOnlyList<KundaliInterpretationFactor> factors, CancellationToken token);
}
