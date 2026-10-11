using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Text;
using NodaTime.TimeZones;

namespace MyVedicDay.Localization;

/// <summary>
/// Chooses the display format for a location: its country selects the date pattern; the time zone supplies the
/// abbreviation in force at each instant (e.g. CST/CDT, IST).
/// </summary>
public interface IDateTimeDisplayFormatter
{
    /// <param name="timeZone">The location's time zone.</param>
    /// <param name="countryCode">ISO 3166-1 alpha-2 country of the location (e.g. from the matched city); when
    /// <see langword="null"/>, the time zone's country is used.</param>
    LocationDateTimeFormat CreateFormat(DateTimeZone timeZone, string? countryCode);
}

internal sealed class DateTimeDisplayFormatter(IOptions<DateTimeDisplayOptions> options) : IDateTimeDisplayFormatter
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ZoneCountries = new(LoadZoneCountries);
    private readonly ConcurrentDictionary<string, string?> _derivedPatterns = new(StringComparer.OrdinalIgnoreCase);

    public LocationDateTimeFormat CreateFormat(DateTimeZone timeZone, string? countryCode)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var country = countryCode ?? ZoneCountries.Value.GetValueOrDefault(timeZone.Id);
        var datePattern = SelectDatePattern(country);
        var timePattern = options.Value.TimePattern;

        return new LocationDateTimeFormat(
            timeZone,
            new DateTimeDisplayFormat(datePattern, timePattern, country),
            LocalDatePattern.Create(datePattern, CultureInfo.InvariantCulture),
            LocalTimePattern.Create(timePattern, CultureInfo.InvariantCulture),
            LocalTimePattern.Create(options.Value.ShortTimePattern, CultureInfo.InvariantCulture));
    }

    private string SelectDatePattern(string? country)
    {
        if (country is null)
        {
            return options.Value.DefaultDatePattern;
        }

        if (options.Value.CountryDatePatterns.TryGetValue(country, out var configured))
        {
            return configured;
        }

        return options.Value.DeriveFromCountryConventions
            ? _derivedPatterns.GetOrAdd(country, DeriveDatePattern) ?? options.Value.DefaultDatePattern
            : options.Value.DefaultDatePattern;
    }

    /// <summary>
    /// Derives the country's convention from CLDR culture data: the most common normalized short-date pattern among
    /// the country's non-English cultures (e.g. <c>de-DE</c>, not <c>en-DE</c>), falling back to English cultures when
    /// no other language is present. Returns <see langword="null"/> when culture data is unavailable
    /// (e.g. invariant globalization mode).
    /// </summary>
    private static string? DeriveDatePattern(string country)
    {
        var cultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(culture => culture.Name.EndsWith("-" + country, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var local = cultures.Where(culture => culture.TwoLetterISOLanguageName != "en").ToArray();

        return (local.Length > 0 ? local : cultures)
            .Select(culture => Normalize(culture.DateTimeFormat.ShortDatePattern))
            .OfType<string>()
            .GroupBy(pattern => pattern, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    /// <summary>
    /// Keeps the day/month/year order and separator of a short-date pattern and normalizes it to two-digit day and
    /// month and a four-digit year (e.g. <c>d.M.yy</c> → <c>dd.MM.yyyy</c>).
    /// </summary>
    private static string? Normalize(string shortDate)
    {
        var order = new List<char>(3);
        char? separator = null;
        foreach (var character in shortDate)
        {
            var field = char.ToLowerInvariant(character) switch { 'd' => 'd', 'm' => 'M', 'y' => 'y', _ => '\0' };
            if (field != '\0')
            {
                if (!order.Contains(field))
                {
                    order.Add(field);
                }
            }
            else if (separator is null && order.Count > 0 && character is '/' or '.' or '-')
            {
                // Only visible date separators: some cultures (e.g. Arabic) embed invisible directional marks.
                separator = character;
            }
        }

        return order.Count == 3
            ? string.Join(separator ?? '/', order.Select(field => field switch { 'd' => "dd", 'M' => "MM", _ => "yyyy" }))
            : null;
    }

    private static Dictionary<string, string> LoadZoneCountries()
    {
        var source = TzdbDateTimeZoneSource.Default;
        var countries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var location in (IEnumerable<TzdbZoneLocation>?)source.ZoneLocations ?? [])
        {
            countries.TryAdd(location.ZoneId, location.CountryCode);
        }

        return countries;
    }
}

/// <summary>
/// Formats values for one location: <c>{date} {time} {zone abbreviation}</c>, e.g. <c>09/28/2026 09:37:10 CDT</c>.
/// </summary>
public sealed class LocationDateTimeFormat
{
    private static readonly OffsetPattern UtcOffsetPattern = OffsetPattern.CreateWithInvariantCulture("+HH:mm");

    private readonly DateTimeZone zone;
    private readonly LocalDatePattern datePattern;
    private readonly LocalTimePattern timePattern;
    private readonly LocalTimePattern shortTimePattern;

    internal LocationDateTimeFormat(
        DateTimeZone zone,
        DateTimeDisplayFormat format,
        LocalDatePattern datePattern,
        LocalTimePattern timePattern,
        LocalTimePattern shortTimePattern)
    {
        this.shortTimePattern = shortTimePattern;
        this.zone = zone;
        this.datePattern = datePattern;
        this.timePattern = timePattern;
        Format = format;
    }

    public DateTimeDisplayFormat Format { get; }

    public string FormatDate(DateOnly date) => datePattern.Format(LocalDate.FromDateOnly(date));

    /// <summary>
    /// Compact range for sentences, e.g. <c>13:32–14:58 IST</c>. A time whose local date is after
    /// <paramref name="referenceDate"/> is followed by <paramref name="nextDayMarker"/> (e.g. <c>23:10–00:45(+1) IST</c>).
    /// </summary>
    public string FormatTimeRange(DateTimeOffset start, DateTimeOffset end, DateOnly referenceDate, string nextDayMarker)
    {
        var zonedStart = Instant.FromDateTimeOffset(start).InZone(zone);
        var zonedEnd = Instant.FromDateTimeOffset(end).InZone(zone);
        var reference = LocalDate.FromDateOnly(referenceDate);

        string Time(ZonedDateTime value) =>
            shortTimePattern.Format(value.TimeOfDay) + (value.Date > reference ? nextDayMarker : string.Empty);

        return $"{Time(zonedStart)}–{Time(zonedEnd)} {Abbreviation(zonedEnd)}";
    }

    public string FormatDateTime(DateTimeOffset value)
    {
        var zoned = Instant.FromDateTimeOffset(value).InZone(zone);
        return $"{datePattern.Format(zoned.Date)} {timePattern.Format(zoned.TimeOfDay)} {Abbreviation(zoned)}";
    }

    /// <summary>
    /// The tz database abbreviation (CST, CDT, IST, BST, AEST…). Zones without a letter abbreviation (the database
    /// uses numeric ones such as <c>+04</c>) are shown as <c>UTC+04:00</c>.
    /// </summary>
    private static string Abbreviation(ZonedDateTime zoned)
    {
        var name = zoned.GetZoneInterval().Name;
        return string.IsNullOrEmpty(name) || !char.IsLetter(name[0])
            ? "UTC" + UtcOffsetPattern.Format(zoned.Offset)
            : name;
    }
}
