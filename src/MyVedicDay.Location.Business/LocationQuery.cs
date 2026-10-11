namespace MyVedicDay.Location;

/// <summary>
/// A location as supplied by a client. Supply one of:
/// <list type="bullet">
/// <item><description><see cref="Place"/> as "City, State, Country";</description></item>
/// <item><description><see cref="Place"/> as "City, State" plus the browser's coordinates, which supply the country;</description></item>
/// <item><description>only the browser's <see cref="Latitude"/>/<see cref="Longitude"/>.</description></item>
/// </list>
/// When a place is given it determines the coordinates; coordinates then only help disambiguate it.
/// </summary>
/// <param name="Latitude">Decimal degrees, −90 to 90.</param>
/// <param name="Longitude">Decimal degrees, −180 to 180.</param>
/// <param name="Place">"City", "City, State" or "City, State, Country".</param>
/// <param name="TimeZoneId">
/// Optional IANA time zone supplied by the client. It is validated but not trusted: the zone derived from the
/// coordinates takes precedence, and the client zone is used only when no zone can be derived.
/// </param>
public sealed record LocationQuery(double? Latitude = null, double? Longitude = null, string? Place = null, string? TimeZoneId = null);
