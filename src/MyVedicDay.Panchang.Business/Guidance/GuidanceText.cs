using MyVedicDay.Localization;
using MyVedicDay.Panchang.Business.Localization;
using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Panchang.Business.Guidance;

/// <summary>
/// Writes the localized headline and summary from content templates (<c>guidanceText.*</c>), so wording and
/// translations are data, not code.
/// </summary>
internal static class GuidanceText
{
    public static string Template(LocalizedContent content, string id) =>
        content.GetEntry(ContentKeys.GuidanceText, id).Message ?? string.Empty;

    public static string Headline(GuidanceRating rating, LocalizedContent content)
    {
        var entry = content.GetEntry(ContentKeys.GuidanceRatings, rating.ToString());
        return $"{entry.Icon} {entry.Message ?? entry.Name}".Trim();
    }

    public static string Summary(
        DailyPanchang day, GuidanceRating rating, IReadOnlyList<GuidanceFactor> factors, GuidanceWindows windows,
        LocalizedContent content, LocationDateTimeFormat format)
    {
        var sentences = new List<string>
        {
            content.GetEntry(ContentKeys.GuidanceRatings, rating.ToString()).Description ?? string.Empty,
        };

        AddFactors(sentences, factors.Where(factor => factor.Rating is GuidanceRating.HighlyFavorable or GuidanceRating.Favorable), "supportive", content);
        AddFactors(sentences, factors.Where(factor => factor.Rating is GuidanceRating.Caution or GuidanceRating.Avoid), "caution", content);

        var nextDay = Template(content, "nextDay");
        AddWindows(sentences, "bestTime", windows.Best.Select(window => format.FormatTimeRange(window.Start, window.End, day.Date, nextDay)), content);
        AddWindows(sentences, "favorable", windows.Favorable.Select(period => Describe(period, day, format, nextDay)), content);
        AddWindows(sentences, "mixed", windows.Mixed.Select(window => Describe(window, day, content, format, nextDay)), content);
        AddWindows(sentences, "avoid", windows.Avoid.Select(period => Describe(period, day, format, nextDay)), content);
        AddWindows(sentences, "caution", windows.Caution.Select(period => Describe(period, day, format, nextDay)), content);
        AddWindows(sentences, "spiritual", windows.Spiritual.Select(period => Describe(period, day, format, nextDay)), content);

        sentences.Add(content.GetEntry(ContentKeys.GuidanceText, "check").Description ?? Template(content, "check"));

        return string.Join(' ', sentences.Where(sentence => !string.IsNullOrWhiteSpace(sentence)));
    }

    private static void AddFactors(List<string> sentences, IEnumerable<GuidanceFactor> factors, string template, LocalizedContent content)
    {
        var items = factors.Select(factor => factor.Limb == "vara" ? factor.Name : $"{factor.LimbName} {factor.Name}").ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var text = Template(content, items.Length == 1 ? $"{template}One" : $"{template}Many");
        sentences.Add(text.Replace("{items}", JoinNatural(items, content), StringComparison.Ordinal));
    }

    private static void AddWindows(List<string> sentences, string template, IEnumerable<string> windows, LocalizedContent content)
    {
        var items = windows.ToArray();
        if (items.Length > 0)
        {
            sentences.Add(Template(content, template).Replace("{windows}", string.Join(Template(content, "separator"), items), StringComparison.Ordinal));
        }
    }

    private static string Describe(PanchangTimePeriod period, DailyPanchang day, LocationDateTimeFormat format, string nextDay) =>
        $"{period.Name} {format.FormatTimeRange(period.Start, period.End, day.Date, nextDay)}";

    private static string Describe(
        GuidanceMixedWindow window, DailyPanchang day, LocalizedContent content, LocationDateTimeFormat format, string nextDay) =>
        Template(content, "mixedItem")
            .Replace("{favorable}", JoinNatural(window.FavorablePeriods.Select(period => period.Name).ToArray(), content), StringComparison.Ordinal)
            .Replace("{unfavorable}", JoinNatural(window.UnfavorablePeriods.Select(period => period.Name).ToArray(), content), StringComparison.Ordinal)
            .Replace("{range}", format.FormatTimeRange(window.Start, window.End, day.Date, nextDay), StringComparison.Ordinal);

    private static string JoinNatural(IReadOnlyList<string> items, LocalizedContent content) =>
        items.Count == 1
            ? items[0]
            : string.Join(Template(content, "separator"), items.Take(items.Count - 1)) + Template(content, "and") + items[^1];
}
