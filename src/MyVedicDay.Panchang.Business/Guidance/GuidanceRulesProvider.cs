using System.Collections.Concurrent;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Data.Guidance;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Panchang.Business.Guidance;

/// <summary>A value's rating and the traditional group explaining it.</summary>
internal sealed record ValueRating(GuidanceRating Rating, string? Group);

/// <summary>Guidance rules for one tradition: the default rules with the tradition's overrides applied.</summary>
internal sealed record ResolvedGuidanceRules(
    string Version,
    bool UsePredominantDaytimeValue,
    IReadOnlyDictionary<GuidanceRating, int> Scores,
    IReadOnlyList<(GuidanceRating Rating, int MinScore)> Thresholds,
    int MinimumBestWindowMinutes,
    int MaximumBestWindows,
    IReadOnlySet<string> SpiritualPeriods,
    IReadOnlyDictionary<string, double> Weights,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, ValueRating>> Values);

/// <summary>
/// Resolves the guidance rules for a tradition. Overrides replace individual weights and value ratings; everything else
/// comes from the default rules. Resolved rules are cached per tradition (the rules document is immutable).
/// </summary>
internal sealed class GuidanceRulesProvider(IGuidanceRulesRepository repository)
{
    public static readonly string[] Limbs = ["tithi", "vara", "nakshatra", "yoga", "karana"];

    private readonly ConcurrentDictionary<string, ResolvedGuidanceRules> _resolved = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ResolvedGuidanceRules> GetRulesAsync(string tradition, CancellationToken cancellationToken)
    {
        if (_resolved.TryGetValue(tradition, out var cached))
        {
            return cached;
        }

        var document = await repository.GetRulesAsync(cancellationToken);
        var overrides = document.Traditions?.GetValueOrDefault(tradition);
        return _resolved.GetOrAdd(tradition, Resolve(document, overrides));
    }

    private static ResolvedGuidanceRules Resolve(GuidanceRulesDocument document, GuidanceRuleSet? overrides)
    {
        var weights = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, IReadOnlyDictionary<string, ValueRating>>(StringComparer.OrdinalIgnoreCase);

        foreach (var limb in Limbs)
        {
            weights[limb] = overrides?.Weights?.GetValueOrDefault(limb) ?? document.Default.Weights?.GetValueOrDefault(limb) ?? 0d;

            var merged = new Dictionary<string, ValueRating>(StringComparer.Ordinal);
            foreach (var (key, rule) in Select(document.Default, limb) ?? new Dictionary<string, GuidanceValueRule>())
            {
                merged[key] = ToValueRating(rule);
            }

            foreach (var (key, rule) in Select(overrides, limb) ?? new Dictionary<string, GuidanceValueRule>())
            {
                merged[key] = ToValueRating(rule);
            }

            values[limb] = merged;
        }

        return new ResolvedGuidanceRules(
            document.Version,
            ParseValueSelection(document.ValueSelection),
            document.RatingScores.ToDictionary(pair => ParseRating(pair.Key), pair => pair.Value),
            document.Thresholds
                .Select(threshold => (ParseRating(threshold.Rating), threshold.MinScore))
                .OrderByDescending(threshold => threshold.MinScore)
                .ToArray(),
            document.BestWindows.MinimumMinutes,
            document.BestWindows.MaximumCount,
            document.SpiritualPeriods.ToHashSet(StringComparer.Ordinal),
            weights,
            values);
    }

    private static IReadOnlyDictionary<string, GuidanceValueRule>? Select(GuidanceRuleSet? rules, string limb) => limb switch
    {
        "tithi" => rules?.Tithi,
        "vara" => rules?.Vara,
        "nakshatra" => rules?.Nakshatra,
        "yoga" => rules?.Yoga,
        "karana" => rules?.Karana,
        _ => null,
    };

    private static bool ParseValueSelection(string? selection) => selection switch
    {
        null or "PredominantDaytime" => true,
        "Sunrise" => false,
        _ => throw new CalculationException($"The daily-guidance rules contain an unknown valueSelection '{selection}'."),
    };

    private static ValueRating ToValueRating(GuidanceValueRule rule) => new(ParseRating(rule.Rating), rule.Group);

    private static GuidanceRating ParseRating(string rating) =>
        Enum.TryParse<GuidanceRating>(rating, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new CalculationException($"The daily-guidance rules contain an unknown rating '{rating}'.");
}
