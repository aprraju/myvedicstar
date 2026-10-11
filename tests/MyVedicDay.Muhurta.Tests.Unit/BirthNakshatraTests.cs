using System.Text.Json;
using MyVedicDay.Location;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Muhurta.Tests.Unit;

public sealed class BirthNakshatraTests
{
    private static readonly MuhurtaQuery Query = new(new LocationQuery(17.385, 78.4867));
    private static readonly Instant Morning = Instant.FromUtc(2026, 10, 1, 3, 0);

    [Fact]
    public void CatalogAndAliases_NormalizeWithoutAcceptingNumbersOrAbhijit()
    {
        foreach (var star in Enum.GetValues<Nakshatra>()) Assert.Equal(star, BirthNakshatra.Parse(star.ToString().ToUpperInvariant()));
        Assert.Equal(Nakshatra.PurvaPhalguni, BirthNakshatra.Parse(" purva-phalguni "));
        Assert.Equal(Nakshatra.Mrigashira, BirthNakshatra.Parse("Mrigashirsha"));
        foreach (var invalid in new[] { "Abhijit", "1", "28", "invalid" }) Assert.Throws<ValidationException>(() => BirthNakshatra.Parse(invalid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyInput_KeepsBusinessResultsAndStandardCacheKey(string? birth)
    {
        using var host = new MuhurtaTestHost(Morning);
        var request = new GoodTimesRequest(new DateOnly(2026, 10, 1), "business");
        var standard = await host.Facade.GetGoodTimesAsync(Query, request, CancellationToken.None);
        var optional = await host.Facade.GetGoodTimesAsync(Query with { Nakshatra = birth }, request, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(standard), JsonSerializer.Serialize(optional));
        Assert.Equal(1, host.Calculator.Calls);
        Assert.DoesNotContain(host.Cache.Expirations.Keys, key => key.Contains("birth-nakshatra", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PersonalizedCache_IsSeparatedAndAliasesShareCanonicalEntry()
    {
        using var host = new MuhurtaTestHost(Morning);
        var request = new GoodTimesRequest(new DateOnly(2026, 10, 1), "business");
        await host.Facade.GetGoodTimesAsync(Query, request, CancellationToken.None);
        var first = await host.Facade.GetGoodTimesAsync(Query with { Nakshatra = "Rohini" }, request, CancellationToken.None);
        var alias = await host.Facade.GetGoodTimesAsync(Query with { Nakshatra = "ROHINI" }, request, CancellationToken.None);
        var second = await host.Facade.GetGoodTimesAsync(Query with { Nakshatra = "Bharani" }, request, CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(alias));
        Assert.NotEqual(JsonSerializer.Serialize(first.Segments), JsonSerializer.Serialize(second.Segments));
        Assert.Equal(3, host.Calculator.Calls);
        Assert.Equal(3, host.Cache.Expirations.Count);
        Assert.Single(host.Cache.Expirations.Values.Distinct());
        Assert.Contains(host.Cache.Expirations.Keys, key => key.EndsWith(":birth-nakshatra:Rohini:tara-v1", StringComparison.Ordinal));
        Assert.All(first.Segments.Where(segment => segment.IsGoodTime), segment => Assert.Empty(segment.Conflicts));
    }

    [Theory]
    [InlineData(17.385, 78.4867, "Asia/Kolkata", "2026-10-01")]
    [InlineData(30.2672, -97.7431, "America/Chicago", "2026-09-30")]
    public async Task PersonalRangesAndBestTimes_AgreeWithDailyEvaluationAcrossTimeZones(
        double latitude, double longitude, string zone, string expectedToday)
    {
        using var host = new MuhurtaTestHost(Morning);
        var query = new MuhurtaQuery(new LocationQuery(latitude, longitude), Nakshatra: "Rohini");
        var start = DateOnly.Parse(expectedToday, System.Globalization.CultureInfo.InvariantCulture);
        var summaries = await host.Facade.GetDaySummariesAsync(query, start, 5, "business", null, CancellationToken.None);
        Assert.Equal(start, summaries.LocalToday);
        Assert.Equal(zone, summaries.Location.TimeZoneId);
        foreach (var summary in summaries.Days)
        {
            var day = await host.Facade.GetGoodTimesAsync(query, new GoodTimesRequest(summary.Date, "business"), CancellationToken.None);
            Assert.Equal(day.DayStatus, summary.Status);
            Assert.Equal(day.GoodTimes.Count, summary.GoodTimeCount);
            Assert.Equal(day.NoGoodTimeReason, summary.NoGoodTimeReason);
        }
        var best = await host.Facade.GetBestGoodTimesAsync(query, new BestGoodTimesRequest("business", Days: 5), CancellationToken.None);
        Assert.Equal(start, best.SearchedFrom);
        foreach (var option in best.Options)
        {
            var day = await host.Facade.GetGoodTimesAsync(query, new GoodTimesRequest(option.Date, "business"), CancellationToken.None);
            var daily = day.Segments.Single(segment => segment.Id == option.Segment.Id);
            Assert.Equal(JsonSerializer.Serialize(daily), JsonSerializer.Serialize(option.Segment));
            Assert.True(daily.IsGoodTime);
            Assert.Empty(daily.Conflicts);
        }
    }
}
