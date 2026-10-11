using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Tests.Unit.TestDoubles;
using NodaTime;

namespace MyVedicDay.Panchang.Tests.Unit.Localization;

/// <summary>
/// Display strings follow the location's date convention, 24-hour time and the DST-correct zone abbreviation.
/// </summary>
public sealed partial class DateTimeDisplayTests
{
    private static readonly PanchangQuery Austin = new(30.2672, -97.7431);
    private static readonly PanchangQuery Hyderabad = new(17.385, 78.4867);

    [Fact]
    public async Task UnitedStates_UsesMonthDayYear_AndDaylightAbbreviationInSeptember()
    {
        var result = await GetAsync(Austin, new DateOnly(2026, 9, 28));
        var day = result.Days[0];

        Assert.Equal("MM/dd/yyyy", result.DisplayFormat.DatePattern);
        Assert.Equal("US", result.DisplayFormat.CountryCode);
        Assert.Equal("09/28/2026", day.DateDisplay);
        Assert.Matches(UsDisplay(), day.SunDisplay.Sunrise);
        Assert.EndsWith(" CDT", day.SunDisplay.Sunrise, StringComparison.Ordinal);
        Assert.Equal(Expected(day.Sun.Sunrise, "MM/dd/yyyy", "CDT"), day.SunDisplay.Sunrise);
    }

    [Fact]
    public async Task UnitedStates_UsesStandardAbbreviationInWinter()
    {
        var day = (await GetAsync(Austin, new DateOnly(2026, 1, 15))).Days[0];

        Assert.EndsWith(" CST", day.SunDisplay.Sunrise, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbbreviationChangesOnTheDaylightSavingDate()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 3, 1, 12, 0));

        var result = await host.Facade.GetDateRangeAsync(Austin, new DateOnly(2026, 3, 7), 2, CancellationToken.None);

        Assert.EndsWith(" CST", result.Days[0].SunDisplay.Sunrise, StringComparison.Ordinal);
        Assert.EndsWith(" CDT", result.Days[1].SunDisplay.Sunrise, StringComparison.Ordinal);
    }

    [Fact]
    public async Task India_UsesDayMonthYear_AndIst()
    {
        var result = await GetAsync(Hyderabad, new DateOnly(2026, 9, 28));
        var day = result.Days[0];

        Assert.Equal("dd/MM/yyyy", result.DisplayFormat.DatePattern);
        Assert.Equal("28/09/2026", day.DateDisplay);
        Assert.Equal("28/09/2026", result.LocalTodayDisplay);
        Assert.Equal(Expected(day.Sun.Sunrise, "dd/MM/yyyy", "IST"), day.SunDisplay.Sunrise);
    }

    [Fact]
    public async Task EveryPeriodAndElement_HasDisplayStringsMatchingItsTimes()
    {
        var day = (await GetAsync(Hyderabad, new DateOnly(2026, 9, 28))).Days[0];

        foreach (var tithi in day.Tithis)
        {
            Assert.Equal(Expected(tithi.Start, "dd/MM/yyyy", "IST"), tithi.StartDisplay);
            Assert.Equal(Expected(tithi.End, "dd/MM/yyyy", "IST"), tithi.EndDisplay);
        }

        foreach (var period in day.Periods)
        {
            Assert.Equal(Expected(period.Start, "dd/MM/yyyy", "IST"), period.StartDisplay);
            Assert.Equal(Expected(period.End, "dd/MM/yyyy", "IST"), period.EndDisplay);
        }
    }

    [Fact]
    public async Task OtherCountries_FollowTheirConvention()
    {
        var berlin = await GetAsync(new PanchangQuery(52.52, 13.405), new DateOnly(2026, 9, 28));
        var tokyo = await GetAsync(new PanchangQuery(35.6762, 139.6503), new DateOnly(2026, 9, 28));

        Assert.Equal("28.09.2026", berlin.Days[0].DateDisplay);
        Assert.EndsWith(" CEST", berlin.Days[0].SunDisplay.Sunrise, StringComparison.Ordinal);
        Assert.Equal("2026/09/28", tokyo.Days[0].DateDisplay);
        Assert.EndsWith(" JST", tokyo.Days[0].SunDisplay.Sunrise, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZonesWithoutLetterAbbreviation_ShowUtcOffset()
    {
        var dubai = await GetAsync(new PanchangQuery(25.2048, 55.2708), new DateOnly(2026, 9, 28));

        Assert.EndsWith(" UTC+04:00", dubai.Days[0].SunDisplay.Sunrise, StringComparison.Ordinal);

        // Arabic culture patterns contain invisible right-to-left marks; they must not leak into the display.
        Assert.Equal("28/09/2026", dubai.Days[0].DateDisplay);
    }

    [Fact]
    public async Task CountryPatterns_AreConfigurable()
    {
        using var host = new BusinessTestHost(
            Instant.FromUtc(2026, 9, 28, 6, 0),
            settings: new Dictionary<string, string?> { ["DateTimeDisplay:CountryDatePatterns:IN"] = "dd-MM-yyyy" });

        var result = await host.Facade.GetDateAsync(Hyderabad, new DateOnly(2026, 9, 28), CancellationToken.None);

        Assert.Equal("28-09-2026", result.Days[0].DateDisplay);
    }

    [Fact]
    public async Task WithoutANamedPlace_TheCountryComesFromTheTimeZone()
    {
        // Gibson Desert, Western Australia: no city within 50 km, so there is no place to take the country from.
        var result = await GetAsync(new PanchangQuery(-23.5, 125.0), new DateOnly(2026, 9, 28));

        Assert.Null(result.Location.Place);
        Assert.Equal("AU", result.DisplayFormat.CountryCode);
        Assert.Equal("28/09/2026", result.Days[0].DateDisplay);
    }

    [Theory]
    [InlineData("DateTimeDisplay:DefaultDatePattern", "yyyy-MM-dd 'unterminated")]
    [InlineData("DateTimeDisplay:ShortTimePattern", "HH:mm 'unterminated")]
    [InlineData("DateTimeDisplay:CountryDatePatterns:USA", "MM/dd/yyyy")]
    [InlineData("DateTimeDisplay:CountryDatePatterns:G1", "dd/MM/yyyy")]
    [InlineData("DateTimeDisplay:CountryDatePatterns:FR", "dd/MM/yyyy 'unterminated")]
    public void InvalidSettings_FailValidation(string key, string value)
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0), settings: new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => host.GetRequiredService<IOptions<DateTimeDisplayOptions>>().Value);
    }

    [Fact]
    public void InvalidPattern_FailsValidation()
    {
        using var host = new BusinessTestHost(
            Instant.FromUtc(2026, 9, 28, 6, 0),
            settings: new Dictionary<string, string?> { ["DateTimeDisplay:TimePattern"] = "HH:mm:ss 'unterminated" });

        Assert.Throws<OptionsValidationException>(() => host.GetRequiredService<IOptions<DateTimeDisplayOptions>>().Value);
    }

    private static async Task<PanchangResult> GetAsync(PanchangQuery query, DateOnly date)
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));
        return await host.Facade.GetDateAsync(query, date, CancellationToken.None);
    }

    private static string Expected(DateTimeOffset value, string datePattern, string abbreviation) =>
        $"{value.ToString(datePattern, CultureInfo.InvariantCulture)} " +
        $"{value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {abbreviation}";

    [GeneratedRegex(@"^\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2} C[DS]T$")]
    private static partial Regex UsDisplay();
}
