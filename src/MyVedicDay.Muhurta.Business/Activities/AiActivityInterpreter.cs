using System.Text.Json;
using Microsoft.Extensions.Options;
using MyVedicDay.Ai;
using MyVedicDay.Muhurta.Data;

namespace MyVedicDay.Muhurta.Business.Activities;

/// <summary>
/// Maps "Other" text to a known activity with generative AI (ADR-0012). Returns null when AI is disabled, fails, is
/// unsure, or answers with anything but a listed key; the keyword interpreter then decides.
/// </summary>
internal sealed class AiActivityInterpreter(
    IStructuredTextGenerator ai, IMuhurtaPromptRepository prompts, IOptions<MuhurtaOptions> options) : IActivityInterpreter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> InterpretAsync(string text, IReadOnlyList<ActivityCandidate> candidates, CancellationToken cancellationToken)
    {
        if (!ai.IsEnabled)
        {
            return null;
        }

        var prompt = await prompts.GetPromptsAsync(cancellationToken);
        var input = JsonSerializer.Serialize(new { activities = candidates.Select(candidate => candidate.Key), description = text }, SerializerOptions);
        var result = await ai.GenerateAsync<ActivityClassification>(
            new AiRequest("muhurta.classify-activity", prompt.ActivityClassification, input, prompt.Version), cancellationToken);

        return result is { Outcome: AiOutcome.Succeeded, Value: { Activity: { } key, Confidence: var confidence } }
            && confidence >= options.Value.OtherTextMinimumConfidence
            && candidates.Any(candidate => candidate.Key == key)
            ? key
            : null;
    }
}

/// <summary>The AI's answer: one listed activity key (or null) and its confidence.</summary>
internal sealed record ActivityClassification(string? Activity, double Confidence);
