using System.Globalization;
using Microsoft.Extensions.Options;
using MyVedicDay.Location;
using MyVedicDay.Panchang.Business.Daily;
using MyVedicDay.Panchang.Calculation;
using NodaTime;
using NodaTime.Text;

namespace MyVedicDay.Panchang.Business.Caching;

/// <summary>
/// Builds daily cache keys:
/// <c>panchang:{latitude}:{longitude}:{timeZone}:{localDate}:{calculationMethod}:{calculationVersion}:d{dataSchemaVersion}</c>.
/// </summary>
/// <remarks>
/// The key is based on the location's local calendar date, never the server or UTC date, so when the local day
/// changes a new key is used automatically and yesterday's entry can never be served as today's.
/// The key has no language: cached entries are language-neutral and localized per request.
/// </remarks>
internal sealed class PanchangCacheKeyFactory(IOptions<LocationOptions> options)
{
    /// <summary>Version of the cached <c>DailyPanchangData</c> shape. Bump when that record changes.</summary>
    public const string DataSchemaVersion = "3";

    // Built once at construction time; the format string does not change during the lifetime of the process.
    private readonly string _coordinateFormat =
        "F" + options.Value.CoordinateDecimalPlaces.ToString(CultureInfo.InvariantCulture);

    public string Create(PanchangRequestContext context, LocalDate date)
    {
        ArgumentNullException.ThrowIfNull(context);

        var location = context.Location;

        return string.Join(
            ':',
            "panchang",
            location.Latitude.ToString(_coordinateFormat, CultureInfo.InvariantCulture),
            location.Longitude.ToString(_coordinateFormat, CultureInfo.InvariantCulture),
            location.TimeZone.Id,
            LocalDatePattern.Iso.Format(date),
            context.CalculationMethod,
            PanchangCalculationVersion.Current,
            "d" + DataSchemaVersion);
    }
}
