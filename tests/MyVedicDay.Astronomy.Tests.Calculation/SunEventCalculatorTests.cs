using NodaTime;
using NodaTime.Text;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// Compares sunrise/sunset against published civil almanac times (upper limb, standard refraction), rounded to the minute.
/// </summary>
public sealed class SunEventCalculatorTests
{
    private static readonly Duration Tolerance = Duration.FromMinutes(2);
    private readonly SunEventCalculator _calculator = new(new MeeusEphemeris());

    [Theory]
    [InlineData("London", 51.5074, -0.1278, "Europe/London", "2024-06-21", "04:43", "21:21")]
    [InlineData("New York", 40.7128, -74.0060, "America/New_York", "2024-06-20", "05:25", "20:31")]
    [InlineData("Sydney", -33.8688, 151.2093, "Australia/Sydney", "2024-06-21", "07:00", "16:54")]
    [InlineData("Austin", 30.2672, -97.7431, "America/Chicago", "2024-12-21", "07:24", "17:34")]
    public void SunriseAndSunset_MatchPublishedTimes(
        string city, double latitude, double longitude, string zoneId, string date, string sunrise, string sunset)
    {
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var localDate = LocalDatePattern.Iso.Parse(date).Value;
        var location = new GeoCoordinate(latitude, longitude);

        var actualSunrise = _calculator.CalculateSunrise(localDate, location, zone, SunriseDefinition.UpperLimbWithRefraction);
        var actualSunset = _calculator.CalculateSunset(localDate, location, zone, SunriseDefinition.UpperLimbWithRefraction);

        AssertClose(city, ToInstant(localDate, sunrise, zone), actualSunrise);
        AssertClose(city, ToInstant(localDate, sunset, zone), actualSunset);
    }

    [Fact]
    public void Sunrise_FallsOnRequestedLocalDate_WhenZoneIsFarFromSolarTime()
    {
        // Kiritimati (UTC+14) at longitude −157°: local solar noon is about 10.5 hours "earlier" than civil noon.
        var zone = DateTimeZoneProviders.Tzdb["Pacific/Kiritimati"];
        var date = new LocalDate(2026, 3, 1);

        var sunrise = _calculator.CalculateSunrise(date, new GeoCoordinate(1.87, -157.4), zone, SunriseDefinition.UpperLimbWithRefraction);

        Assert.Equal(date, sunrise.InZone(zone).Date);
    }

    [Fact]
    public void Sunrise_DuringPolarDay_Throws()
    {
        var zone = DateTimeZoneProviders.Tzdb["Europe/Oslo"];

        Assert.Throws<SunEventNotFoundException>(() => _calculator.CalculateSunrise(
            new LocalDate(2026, 6, 21), new GeoCoordinate(69.65, 18.96), zone, SunriseDefinition.UpperLimbWithRefraction));
    }

    private static Instant ToInstant(LocalDate date, string time, DateTimeZone zone) =>
        date.At(LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(time).Value).InZoneStrictly(zone).ToInstant();

    private static void AssertClose(string city, Instant expected, Instant actual)
    {
        var difference = actual - expected;
        Assert.True(
            Duration.Negate(Tolerance) <= difference && difference <= Tolerance,
            $"{city}: expected {expected} but was {actual} (difference {difference.TotalSeconds:0} s).");
    }
}
