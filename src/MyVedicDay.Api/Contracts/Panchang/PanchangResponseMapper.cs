using MyVedicDay.Api.Contracts.Festivals;
using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Contracts.Muhurta;
using MyVedicDay.Festivals.Business;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Localization;
using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Api.Contracts.Panchang;

/// <summary>
/// Maps Business Facade results to API response contracts.
/// </summary>
internal static class PanchangResponseMapper
{
    /// <summary>
    /// Maps a Panchang result, adding each day's festivals and Good Time summary (same dates and location).
    /// </summary>
    public static PanchangResponse ToResponse(this PanchangResult result, FestivalsResult festivals, DaySummariesResult goodTimes)
    {
        ArgumentNullException.ThrowIfNull(festivals);
        ArgumentNullException.ThrowIfNull(goodTimes);

        var festivalsByDate = festivals.Festivals.ToLookup(festival => festival.Date);
        var goodTimesByDate = goodTimes.Days.ToDictionary(day => day.Date);
        return new(
            result.Location.ToResponse(),
            result.Tradition.ToResponse(),
            result.CalculationMethod,
            result.CalculationVersion,
            result.LocalToday,
            result.LocalTodayDisplay,
            new DisplayFormatResponse(
                result.DisplayFormat.DatePattern,
                result.DisplayFormat.TimePattern,
                result.DisplayFormat.CountryCode,
                result.Days.Count > 0 ? result.Days[0].SunDisplay.Sunrise : result.LocalTodayDisplay),
            result.GeneratedAt,
            result.Glossary.Language,
            ToResponse(result.Glossary.Sections),
            result.Days.Select(day => ToResponse(day, festivalsByDate[day.Date], goodTimesByDate[day.Date])).ToArray());
    }

    public static TraditionResponse ToResponse(this PanchangTradition tradition) =>
        new(tradition.Key, tradition.Name, tradition.Description, tradition.Display);

    public static PanchangTraditionsResponse ToResponse(this PanchangTraditionsResult traditions) =>
        new(traditions.Language, traditions.Traditions.Select(ToResponse).ToArray());

    public static GlossaryResponse ToResponse(this GlossaryResult glossary) =>
        new(glossary.Language, ToResponse(glossary.Sections));

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, GlossaryEntryResponse>> ToResponse(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, LocalizedEntry>> sections) =>
        sections.ToDictionary(
            section => section.Key,
            section => (IReadOnlyDictionary<string, GlossaryEntryResponse>)section.Value.ToDictionary(
                entry => entry.Key,
                entry => new GlossaryEntryResponse(
                    entry.Value.Name, entry.Value.Meaning, entry.Value.Description, entry.Value.Message, entry.Value.Icon)));

    private static DailyPanchangResponse ToResponse(DailyPanchang day, IEnumerable<Festival> festivals, MuhurtaDaySummary goodTime) =>
        new(
            day.Date,
            day.DateDisplay,
            day.Weekday.ToString(),
            day.WeekdayName,
            day.Sun.Sunrise,
            day.Sun.Sunset,
            day.Sun.NextSunrise,
            day.SunDisplay.Sunrise,
            day.SunDisplay.Sunset,
            day.SunDisplay.NextSunrise,
            day.Tithis.Select(ToResponse).ToArray(),
            day.Nakshatras.Select(ToResponse).ToArray(),
            day.Yogas.Select(ToResponse).ToArray(),
            day.Karanas.Select(ToResponse).ToArray(),
            day.Periods.Where(period => !IsChoghadiya(period) && period.IsAuspicious).Select(ToResponse).ToArray(),
            day.Periods.Where(period => !IsChoghadiya(period) && !period.IsAuspicious).Select(ToResponse).ToArray(),
            ToResponse(day.Guidance ?? throw new InvalidOperationException("Daily guidance was not evaluated.")),
            festivals.Select(FestivalsResponseMapper.ToResponse).ToArray(),
            goodTime.ToResponse(),
            day.Periods.Any(IsChoghadiya) ? day.Periods.Where(IsChoghadiya).Select(ToResponse).ToArray() : null);

    private static DailyGuidanceResponse ToResponse(DailyGuidance guidance) =>
        new(
            guidance.Rating.ToString(),
            guidance.RatingName,
            guidance.Icon,
            guidance.Headline,
            guidance.Summary,
            guidance.Factors.Select(factor => new GuidanceFactorResponse(
                factor.Limb, factor.LimbName, factor.Key, factor.Name, factor.Rating.ToString(), factor.RatingName, factor.Reason)).ToArray(),
            guidance.BestWindows.Select(window => new GuidanceWindowResponse(window.Start, window.End, window.StartDisplay, window.EndDisplay)).ToArray(),
            guidance.FavorableWindows.Select(ToResponse).ToArray(),
            guidance.SpiritualWindows.Select(ToResponse).ToArray(),
            guidance.CautionWindows.Select(ToResponse).ToArray(),
            guidance.AvoidWindows.Select(ToResponse).ToArray(),
            guidance.MixedWindows.Select(ToResponse).ToArray(),
            guidance.Methodology,
            guidance.RulesVersion);

    private static MixedWindowResponse ToResponse(GuidanceMixedWindow window) =>
        new(
            window.Start,
            window.End,
            window.StartDisplay,
            window.EndDisplay,
            window.Rating.ToString(),
            window.RatingName,
            window.Icon,
            window.FavorablePeriods.Select(ToResponse).ToArray(),
            window.UnfavorablePeriods.Select(ToResponse).ToArray());

    private static bool IsChoghadiya(PanchangTimePeriod period) =>
        period.Group.Equals("Choghadiya", StringComparison.Ordinal);

    private static PanchangElementResponse ToResponse(PanchangElementPeriod period) =>
        new(period.Key, period.Number, period.Name, period.Start, period.End, period.StartDisplay, period.EndDisplay);

    private static TimePeriodResponse ToResponse(PanchangTimePeriod period) =>
        new(
            period.Kind,
            period.Name,
            period.Classification.ToString(),
            period.Start,
            period.End,
            period.StartDisplay,
            period.EndDisplay,
            period.Overlaps?.Select(overlap => new PeriodOverlapResponse(
                overlap.Kind, overlap.Name, overlap.Classification.ToString(), overlap.Start, overlap.End, overlap.StartDisplay, overlap.EndDisplay)).ToArray(),
            period.OverlapMessage);
}
