using System.ComponentModel.DataAnnotations;

namespace MyVedicDay.Location;

/// <summary>
/// Location settings. Bound from the <c>Location</c> configuration section and validated at startup.
/// </summary>
public sealed class LocationOptions
{
    public const string SectionName = "Location";

    /// <summary>
    /// Decimal places kept from request coordinates. 2 places ≈ 1.1 km, which moves sunrise by at most a few seconds
    /// while keeping the number of distinct cache entries bounded and limiting stored precision.
    /// </summary>
    [Range(1, 4)]
    public int CoordinateDecimalPlaces { get; set; } = 2;

    /// <summary>Coordinates within this distance of a city are labelled with that city's name.</summary>
    [Range(1, 500)]
    public double NearestCityMaxDistanceKm { get; set; } = 50;

    /// <summary>Coordinates within this distance of a city give that city's country as the place-search hint.</summary>
    [Range(1, 2000)]
    public double CountryHintMaxDistanceKm { get; set; } = 300;
}
