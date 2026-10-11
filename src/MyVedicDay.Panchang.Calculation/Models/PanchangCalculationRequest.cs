using MyVedicDay.Astronomy;
using NodaTime;

namespace MyVedicDay.Panchang.Calculation.Models;

/// <summary>
/// Input for calculating the Panchang of one local calendar date at one location.
/// </summary>
/// <param name="Date">Local calendar date at the location.</param>
/// <param name="Location">Observer coordinates. Sunrise and all sunrise-based periods depend on these.</param>
/// <param name="TimeZone">IANA time zone of the location, used to determine which sunrise belongs to <paramref name="Date"/>.</param>
/// <param name="Tradition">Panchang tradition key, see <see cref="PanchangTraditions"/>.</param>
public sealed record PanchangCalculationRequest(LocalDate Date, GeoCoordinate Location, DateTimeZone TimeZone, string Tradition);
