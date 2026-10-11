using MyVedicDay.Location;

namespace MyVedicDay.Panchang.Business.Models;

/// <summary>
/// Where and how to calculate the Panchang.
/// </summary>
/// <param name="Location">The location.</param>
/// <param name="Tradition">Optional Panchang tradition key; the configured default is used when omitted.</param>
/// <param name="PreferredLanguages">
/// Language tags in order of preference (e.g. from <c>?language=</c> then <c>Accept-Language</c>). The first supported
/// language wins; the configured default language is used otherwise.
/// </param>
public sealed record PanchangQuery(LocationQuery Location, string? Tradition = null, IReadOnlyList<string>? PreferredLanguages = null)
{
    public PanchangQuery(double latitude, double longitude, string? tradition = null)
        : this(new LocationQuery(latitude, longitude), tradition)
    {
    }
}
