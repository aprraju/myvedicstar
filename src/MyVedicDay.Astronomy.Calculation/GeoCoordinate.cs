namespace MyVedicDay.Astronomy;

/// <summary>Geographic coordinate in decimal degrees (east and north positive).</summary>
public readonly record struct GeoCoordinate(double Latitude, double Longitude);
