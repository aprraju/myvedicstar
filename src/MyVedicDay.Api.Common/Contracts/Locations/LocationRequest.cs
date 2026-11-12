using System.ComponentModel.DataAnnotations;
using MyVedicDay.Location;

namespace MyVedicDay.Api.Contracts.Locations;

/// <summary>
/// Location query parameters. Supply one of:
/// <c>place=City, State, Country</c>;
/// <c>place=City, State</c> with the browser's <c>latitude</c>/<c>longitude</c> (used to infer the country);
/// or only the browser's <c>latitude</c>/<c>longitude</c>.
/// </summary>
public class LocationRequest : IValidatableObject
{
    /// <summary>Latitude in decimal degrees (north positive), e.g. from the browser Geolocation API.</summary>
    [Range(-90d, 90d)]
    public double? Latitude { get; init; }

    /// <summary>Longitude in decimal degrees (east positive), e.g. from the browser Geolocation API.</summary>
    [Range(-180d, 180d)]
    public double? Longitude { get; init; }

    /// <summary>"City, State" or "City, State, Country", e.g. <c>Austin, Texas, USA</c> or <c>Hyderabad, Telangana</c>.</summary>
    [PlaceName]
    [StringLength(PlaceNameAttribute.MaxLength)]
    public string? Place { get; init; }

    /// <summary>
    /// Optional IANA time zone, e.g. <c>Asia/Kolkata</c>. The zone derived from the location takes precedence.
    /// </summary>
    [RegularExpression("^[A-Za-z0-9_+\\-/]{1,64}$")]
    public string? TimeZone { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitude.HasValue != Longitude.HasValue)
        {
            yield return new ValidationResult(
                "Latitude and longitude must be supplied together.", [nameof(Latitude), nameof(Longitude)]);
        }
        else if (!Latitude.HasValue && string.IsNullOrWhiteSpace(Place))
        {
            yield return new ValidationResult(
                "Provide latitude and longitude, or a place as \"City, State\" or \"City, State, Country\".",
                [nameof(Place)]);
        }
    }

    internal LocationQuery ToLocationQuery() => new(Latitude, Longitude, Place, TimeZone);
}
