using MyVedicDay.Localization;
using MyVedicDay.Panchang.Business.Localization;
using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Panchang.Business.Guidance;

/// <summary>
/// Produces simple consumer daily guidance from a localized, tradition-filtered day. Activity-specific questions
/// ("is this a good day for a marriage?") are a Muhurta calculation and belong to a separate Muhurta evaluator.
/// </summary>
internal interface IDailyGuidanceEvaluator
{
    Task<DailyGuidance> EvaluateAsync(
        DailyPanchang day, string tradition, LocalizedContent content, LocationDateTimeFormat format, CancellationToken cancellationToken);
}

/// <summary>
/// Two independent evaluations: the day rating from the five Panchanga limbs, and the time windows from the tradition's
/// periods. Windows never change the day rating, and overlapping favorable and unfavorable periods are reported as mixed
/// windows rather than cancelling each other.
/// </summary>
internal sealed class DailyGuidanceEvaluator(GuidanceRulesProvider rulesProvider) : IDailyGuidanceEvaluator
{
    public async Task<DailyGuidance> EvaluateAsync(
        DailyPanchang day, string tradition, LocalizedContent content, LocationDateTimeFormat format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(format);

        var rules = await rulesProvider.GetRulesAsync(tradition, cancellationToken);
        var (rating, factors) = GuidanceFactorScorer.Score(day, rules, content);
        var windows = GuidanceWindowAnalyzer.Analyze(day, rules, content, format);
        var ratingEntry = content.GetEntry(ContentKeys.GuidanceRatings, rating.ToString());

        return new DailyGuidance(
            rating,
            ratingEntry.Name,
            ratingEntry.Icon ?? string.Empty,
            GuidanceText.Headline(rating, content),
            GuidanceText.Summary(day, rating, factors, windows, content, format),
            factors,
            windows.Best,
            windows.Favorable,
            windows.Spiritual,
            windows.Caution,
            windows.Avoid,
            windows.Mixed,
            content.GetEntry(ContentKeys.GuidanceText, "methodology").Description ?? string.Empty,
            rules.Version);
    }
}
