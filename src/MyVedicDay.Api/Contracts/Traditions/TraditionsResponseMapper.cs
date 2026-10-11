using MyVedicDay.Traditions;

namespace MyVedicDay.Api.Contracts.Traditions;

/// <summary>
/// Maps Traditions module results to API response contracts.
/// </summary>
internal static class TraditionsResponseMapper
{
    public static TraditionsResponse ToResponse(this TraditionsResult traditions) =>
        new(
            traditions.Language,
            traditions.Traditions.Select(tradition => new TraditionSummaryResponse(tradition.Key, tradition.Name, tradition.Description)).ToArray());
}
