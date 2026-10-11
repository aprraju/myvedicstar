namespace MyVedicDay.Festivals.Business.Daily;

/// <summary>
/// The cached, language- and tradition-neutral festivals of one local date at one location. Times carry the location's
/// UTC offset at that instant.
/// </summary>
internal sealed record FestivalDayData(DateOnly Date, IReadOnlyList<FestivalOccurrenceData> Festivals);

/// <summary>One festival occurrence, as calculated.</summary>
internal sealed record FestivalOccurrenceData(
    string Key,
    string Basis,
    string BasisKey,
    DateTimeOffset BasisStart,
    DateTimeOffset BasisEnd,
    string Time,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd);
