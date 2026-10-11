using MyVedicDay.Localization;
using MyVedicDay.Panchang.Business.Localization;
using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Panchang.Business.Guidance;

/// <summary>
/// Rates each Panchanga limb and combines the ratings into the day rating using the rules' weights. Each limb is rated by
/// the value in force for most of the daytime, or at sunrise, per the rules' <c>valueSelection</c>. The numeric score is
/// internal: it only selects the rating label.
/// </summary>
internal static class GuidanceFactorScorer
{
    public static (GuidanceRating Rating, IReadOnlyList<GuidanceFactor> Factors) Score(
        DailyPanchang day, ResolvedGuidanceRules rules, LocalizedContent content)
    {
        var factors = new List<GuidanceFactor>(GuidanceRulesProvider.Limbs.Length);
        double weightedScore = 0;
        double totalWeight = 0;

        foreach (var limb in GuidanceRulesProvider.Limbs)
        {
            var (key, name) = SelectValue(day, limb, rules.UsePredominantDaytimeValue);
            var value = rules.Values[limb].GetValueOrDefault(key) ?? new ValueRating(GuidanceRating.Mixed, Group: null);

            var weight = rules.Weights[limb];
            if (weight > 0)
            {
                weightedScore += weight * rules.Scores.GetValueOrDefault(value.Rating);
                totalWeight += weight;
            }

            factors.Add(CreateFactor(limb, key, name, value, content));
        }

        var score = totalWeight > 0 ? weightedScore / totalWeight : 0;
        var rating = rules.Thresholds.FirstOrDefault(threshold => score >= threshold.MinScore).Rating;
        return (rating == default ? GuidanceRating.Avoid : rating, factors);
    }

    private static (string Key, string Name) SelectValue(DailyPanchang day, string limb, bool predominantDaytime)
    {
        var periods = limb switch
        {
            "tithi" => day.Tithis,
            "nakshatra" => day.Nakshatras,
            "yoga" => day.Yogas,
            "karana" => day.Karanas,
            _ => null,
        };

        if (periods is null)
        {
            return (day.Weekday.ToString(), day.WeekdayName);
        }

        var selected = predominantDaytime
            ? periods.MaxBy(period => DaytimeOverlap(period, day.Sun))!
            : periods[0];
        return (selected.Key, selected.Name);
    }

    private static TimeSpan DaytimeOverlap(PanchangElementPeriod period, SunTimes sun)
    {
        var start = period.Start > sun.Sunrise ? period.Start : sun.Sunrise;
        var end = period.End < sun.Sunset ? period.End : sun.Sunset;
        return end > start ? end - start : TimeSpan.Zero;
    }

    private static GuidanceFactor CreateFactor(string limb, string key, string name, ValueRating value, LocalizedContent content)
    {
        var limbName = LimbName(limb, content);
        var ratingName = content.GetName(ContentKeys.GuidanceRatings, value.Rating.ToString());
        var explanation = value.Group is null
            ? string.Empty
            : content.GetEntry(ContentKeys.GuidanceGroups, $"{limb}.{value.Group}").Description ?? string.Empty;

        var reason = GuidanceText.Template(content, "reason")
            .Replace("{limb}", limbName, StringComparison.Ordinal)
            .Replace("{value}", name, StringComparison.Ordinal)
            .Replace("{rating}", ratingName, StringComparison.Ordinal)
            .Replace("{explanation}", explanation, StringComparison.Ordinal)
            .Trim();

        return new GuidanceFactor(limb, limbName, key, name, value.Rating, ratingName, reason);
    }

    public static string LimbName(string limb, LocalizedContent content) =>
        limb == "vara" ? content.GetName(ContentKeys.Terms, "vara") : content.GetName(ContentKeys.Elements, limb);
}
