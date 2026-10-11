using MyVedicDay.Location;

namespace MyVedicDay.Panchang.Business.Daily;

/// <summary>
/// Everything that identifies one Panchang calculation apart from the date. Regional traditions that share a
/// calculation method share calculations and cache entries.
/// </summary>
internal sealed record PanchangRequestContext(ResolvedLocation Location, string CalculationMethod);
