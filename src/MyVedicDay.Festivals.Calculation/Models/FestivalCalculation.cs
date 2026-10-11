using MyVedicDay.Astronomy;
using NodaTime;

namespace MyVedicDay.Festivals.Calculation.Models;

/// <summary>Input for finding festivals between two local dates at one location.</summary>
/// <param name="From">First local date (inclusive).</param>
/// <param name="To">Last local date (inclusive).</param>
/// <param name="Location">Observer coordinates: sunrise, sunset and therefore the festival day depend on them.</param>
/// <param name="TimeZone">IANA time zone of the location.</param>
/// <param name="Festivals">Festivals to evaluate.</param>
public sealed record FestivalCalculationRequest(
    LocalDate From, LocalDate To, GeoCoordinate Location, DateTimeZone TimeZone, IReadOnlyList<FestivalDefinition> Festivals);

/// <summary>A festival falling on a local date.</summary>
/// <param name="Key">Festival key.</param>
/// <param name="Date">Local date of observance.</param>
/// <param name="Basis">What determined the date.</param>
/// <param name="BasisKey">The Tithi, Nakshatra or sign, e.g. <c>ShuklaChaturthi</c>, <c>Shravana</c>, <c>Makara</c>.</param>
/// <param name="BasisStart">When the Tithi or Nakshatra begins, or the Sankranti instant.</param>
/// <param name="BasisEnd">When the Tithi or Nakshatra ends, or the Sankranti instant.</param>
/// <param name="Time">Observance time of the rule.</param>
/// <param name="WindowStart">Start of the observance window on the anchor day; null for offset days.</param>
/// <param name="WindowEnd">End of the observance window on the anchor day; null for offset days.</param>
/// <param name="OffsetDays">Days from the anchor day (0 for most festivals).</param>
public sealed record FestivalOccurrence(
    string Key,
    LocalDate Date,
    FestivalBasis Basis,
    string BasisKey,
    Instant BasisStart,
    Instant BasisEnd,
    ObservanceTime Time,
    Instant? WindowStart,
    Instant? WindowEnd,
    int OffsetDays);
