using System.Globalization;
using Microsoft.Extensions.Options;
using MyVedicDay.Festivals.Calculation;
using MyVedicDay.Location;
using MyVedicDay.Panchang.Calculation;
using NodaTime;
using NodaTime.Text;

namespace MyVedicDay.Festivals.Business.Daily;

/// <summary>
/// Builds daily cache keys:
/// <c>festivals:{latitude}:{longitude}:{timeZone}:{localDate}:{rulesVersion}:{festivalVersion}:{panchangVersion}:d{dataSchemaVersion}</c>.
/// </summary>
/// <remarks>
/// Keys have no tradition or language: entries hold every festival and are filtered and localized per request, so all
/// traditions share one calculation per location and date.
/// </remarks>
internal sealed class FestivalCacheKeyFactory(IOptions<LocationOptions> options)
{
    /// <summary>Version of the cached <see cref="FestivalDayData"/> shape. Bump when that record changes.</summary>
    public const string DataSchemaVersion = "1";

    public string Create(ResolvedLocation location, string rulesVersion, LocalDate date)
    {
        ArgumentNullException.ThrowIfNull(location);

        var coordinateFormat = "F" + options.Value.CoordinateDecimalPlaces.ToString(CultureInfo.InvariantCulture);
        return string.Join(
            ':',
            "festivals",
            location.Latitude.ToString(coordinateFormat, CultureInfo.InvariantCulture),
            location.Longitude.ToString(coordinateFormat, CultureInfo.InvariantCulture),
            location.TimeZone.Id,
            LocalDatePattern.Iso.Format(date),
            rulesVersion,
            FestivalCalculationVersion.Current,
            PanchangCalculationVersion.Current,
            DateTimeZoneProviders.Tzdb.VersionId,
            "d" + DataSchemaVersion);
    }
}
