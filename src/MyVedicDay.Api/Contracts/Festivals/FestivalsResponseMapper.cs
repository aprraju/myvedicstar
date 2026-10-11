using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Contracts.Panchang;
using MyVedicDay.Festivals.Business;

namespace MyVedicDay.Api.Contracts.Festivals;

/// <summary>
/// Maps festival facade results to API response contracts.
/// </summary>
internal static class FestivalsResponseMapper
{
    public static FestivalsResponse ToResponse(this FestivalsResult result) =>
        new(
            result.Location.ToResponse(),
            new FestivalTraditionResponse(result.Tradition.Key, result.Tradition.Name),
            result.LocalToday,
            result.LocalTodayDisplay,
            result.From,
            result.To,
            new DisplayFormatResponse(
                result.DisplayFormat.DatePattern,
                result.DisplayFormat.TimePattern,
                result.DisplayFormat.CountryCode,
                result.Festivals.Count > 0 ? result.Festivals[0].Timing.StartDisplay : result.LocalTodayDisplay),
            result.GeneratedAt,
            result.Language,
            result.RulesVersion,
            result.CalculationVersion,
            result.Festivals.Select(ToResponse).ToArray());

    public static FestivalResponse ToResponse(this Festival festival) =>
        new(
            festival.Key,
            festival.Name,
            festival.Description,
            festival.Category,
            festival.CategoryName,
            festival.Icon,
            festival.Date,
            festival.DateDisplay,
            festival.DaysFromToday,
            new FestivalTimingResponse(
                festival.Timing.Basis,
                festival.Timing.BasisKey,
                festival.Timing.Start,
                festival.Timing.End,
                festival.Timing.StartDisplay,
                festival.Timing.EndDisplay,
                festival.Timing.ObservanceTime,
                festival.Timing.ObservanceTimeName,
                festival.Timing.ObservanceStart,
                festival.Timing.ObservanceEnd,
                festival.Timing.ObservanceStartDisplay,
                festival.Timing.ObservanceEndDisplay));
}
