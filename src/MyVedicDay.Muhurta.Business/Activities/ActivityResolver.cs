using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MyVedicDay.Muhurta.Business.Rules;
using MyVedicDay.Muhurta.Data;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Muhurta.Business.Activities;

/// <summary>An activity "Other" text may be mapped to.</summary>
public sealed record ActivityCandidate(string Key, IReadOnlyList<string> Keywords);

/// <summary>
/// Maps a free-text description ("opening a bakery") to one of the known activities. Implementations never invent an
/// activity: they return one of the candidate keys, or null when unsure.
/// </summary>
public interface IActivityInterpreter
{
    Task<string?> InterpretAsync(string text, IReadOnlyList<ActivityCandidate> candidates, CancellationToken cancellationToken);
}

/// <summary>Deterministic interpreter: the first activity (in display order) one of whose keywords occurs in the text.</summary>
internal sealed partial class KeywordActivityInterpreter : IActivityInterpreter
{
    public Task<string?> InterpretAsync(string text, IReadOnlyList<ActivityCandidate> candidates, CancellationToken cancellationToken)
    {
        var normalized = $" {Separators().Replace(text.ToLower(CultureInfo.InvariantCulture), " ").Trim()} ";
        var match = candidates.FirstOrDefault(candidate =>
            candidate.Keywords.Any(keyword => normalized.Contains($" {keyword.ToLower(CultureInfo.InvariantCulture)} ", StringComparison.Ordinal)));
        return Task.FromResult(match?.Key);
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Separators();
}

/// <summary>The activity to evaluate, and what was asked for.</summary>
internal sealed record ResolvedActivity(MuhurtaActivityDefinition Definition, string Requested, string? RequestedText);

/// <summary>
/// Validates the requested activity and maps "Other" text to a known activity: first the configured interpreters in
/// order (e.g. AI, then keywords), else the default activity.
/// </summary>
internal sealed partial class ActivityResolver(
    MuhurtaRulesProvider rules, IEnumerable<IActivityInterpreter> interpreters, IOptions<MuhurtaOptions> options)
{
    public async Task<ResolvedActivity> ResolveAsync(string? requested, string? otherText, CancellationToken cancellationToken)
    {
        var document = await rules.GetDocumentAsync(cancellationToken);
        var key = string.IsNullOrWhiteSpace(requested) ? options.Value.DefaultActivity : requested.Trim();
        var definition = MuhurtaRulesProvider.FindActivity(document, key)
            ?? throw new ValidationException(
                "activity", $"Unsupported activity. Supported activities: {string.Join(", ", document.Activities.Select(activity => activity.Key))}.");

        if (!definition.RequiresInput)
        {
            return new ResolvedActivity(definition, definition.Key, null);
        }

        var text = ValidateText(otherText);
        var candidates = document.Activities
            .Where(activity => !activity.RequiresInput && activity.Rules is not null)
            .Select(activity => new ActivityCandidate(activity.Key, activity.Keywords ?? []))
            .ToArray();

        foreach (var interpreter in interpreters)
        {
            var mapped = await interpreter.InterpretAsync(text, candidates, cancellationToken);
            if (mapped is not null && candidates.Any(candidate => candidate.Key == mapped))
            {
                return new ResolvedActivity(MuhurtaRulesProvider.FindActivity(document, mapped)!, definition.Key, text);
            }
        }

        var fallback = MuhurtaRulesProvider.FindActivity(document, options.Value.DefaultActivity)
            ?? throw new CalculationException($"The default activity '{options.Value.DefaultActivity}' is not defined.");
        return new ResolvedActivity(fallback, definition.Key, text);
    }

    private string ValidateText(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ValidationException("otherText", "Describe the activity when choosing Other.");
        }

        if (trimmed.Length > options.Value.OtherTextMaxLength || !AllowedText().IsMatch(trimmed))
        {
            throw new ValidationException(
                "otherText", $"Use up to {options.Value.OtherTextMaxLength} letters, digits, spaces and simple punctuation.");
        }

        return trimmed;
    }

    [GeneratedRegex(@"^[\p{L}\p{M}\p{N} .,'&/()\-]+$")]
    private static partial Regex AllowedText();
}
