using MyVedicDay.Location;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Tests.Unit.TestDoubles;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Panchang.Tests.Unit;

public sealed class PanchangFacadeTests
{
    private static readonly PanchangQuery Hyderabad = new(17.385, 78.4867);
    private static readonly PanchangQuery Austin = new(30.2672, -97.7431);

    [Fact]
    public async Task Today_IsTheLocationsLocalDate_NotTheServersOrUtcDate()
    {
        // 20:00 UTC on 28 Sep is already 29 Sep in Hyderabad but still 28 Sep in Austin.
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 20, 0));

        var hyderabad = await host.Facade.GetTodayAsync(Hyderabad, CancellationToken.None);
        var austin = await host.Facade.GetTodayAsync(Austin, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 29), hyderabad.LocalToday);
        Assert.Equal(new DateOnly(2026, 9, 29), Assert.Single(hyderabad.Days).Date);
        Assert.Equal("Asia/Kolkata", hyderabad.Location.TimeZoneId);

        Assert.Equal(new DateOnly(2026, 9, 28), austin.LocalToday);
        Assert.Equal(new DateOnly(2026, 9, 28), Assert.Single(austin.Days).Date);
        Assert.Equal("America/Chicago", austin.Location.TimeZoneId);
    }

    [Fact]
    public async Task Today_AfterLocalMidnight_ReturnsTheNewDay()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 18, 0)); // 23:30 IST

        var before = await host.Facade.GetTodayAsync(Hyderabad, CancellationToken.None);
        host.Clock.AdvanceMinutes(60); // 00:30 IST next day
        var after = await host.Facade.GetTodayAsync(Hyderabad, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 28), before.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 29), after.Days[0].Date);
    }

    [Fact]
    public async Task RepeatedRequest_IsServedFromCache()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        await host.Facade.GetTenDaysAsync(Hyderabad, CancellationToken.None);
        await host.Facade.GetTenDaysAsync(Hyderabad, CancellationToken.None);

        Assert.Equal(10, host.Calculator.Calls);
    }

    [Fact]
    public async Task TodaysCacheEntry_ExpiresAtTheLocationsNextLocalMidnight()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        await host.Facade.GetTodayAsync(Hyderabad, CancellationToken.None);
        await host.Facade.GetTodayAsync(Austin, CancellationToken.None);

        Assert.Contains(new DateTimeOffset(2026, 9, 28, 18, 30, 0, TimeSpan.Zero), host.Cache.Expirations.Values); // 00:00 IST
        Assert.Contains(new DateTimeOffset(2026, 9, 29, 5, 0, 0, TimeSpan.Zero), host.Cache.Expirations.Values); // 00:00 CDT
    }

    [Fact]
    public async Task OverlappingRange_CalculatesOnlyMissingDays()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        await host.Facade.GetTodayAsync(Hyderabad, CancellationToken.None);
        await host.Facade.GetTomorrowAsync(Hyderabad, CancellationToken.None);
        var tenDays = await host.Facade.GetTenDaysAsync(Hyderabad, CancellationToken.None);

        Assert.Equal(10, host.Calculator.Calls);
        Assert.Equal(10, tenDays.Days.Count);
    }

    [Fact]
    public async Task NearbyCoordinates_ShareCachedEntries()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        await host.Facade.GetTodayAsync(new PanchangQuery(17.3861, 78.4867), CancellationToken.None);
        await host.Facade.GetTodayAsync(new PanchangQuery(17.3899, 78.4912), CancellationToken.None);

        Assert.Equal(1, host.Calculator.Calls);
    }

    [Fact]
    public async Task PlaceQuery_SharesCacheWithCoordinatesInTheSameCell()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));
        var austin = await host.GetRequiredService<ILocationFacade>().ResolveAsync(new LocationQuery(Place: "Austin, Texas"), CancellationToken.None);

        await host.Facade.GetTodayAsync(new PanchangQuery(new LocationQuery(Place: "Austin, Texas, USA")), CancellationToken.None);
        await host.Facade.GetTodayAsync(new PanchangQuery(austin.Latitude, austin.Longitude), CancellationToken.None);

        Assert.Equal(1, host.Calculator.Calls);
    }

    [Fact]
    public async Task DifferentLocation_IsCalculatedSeparately()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        await host.Facade.GetDateAsync(Hyderabad, new DateOnly(2026, 10, 1), CancellationToken.None);
        await host.Facade.GetDateAsync(Austin, new DateOnly(2026, 10, 1), CancellationToken.None);

        Assert.Equal(2, host.Calculator.Calls);
    }

    [Theory]
    [InlineData(2024, 2, 29)]
    [InlineData(2026, 2, 28)]
    [InlineData(2026, 12, 31)]
    public async Task Month_ReturnsEveryDayInOrder(int year, int month, int expectedDays)
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        var result = await host.Facade.GetMonthAsync(Hyderabad, year, month, CancellationToken.None);

        Assert.Equal(expectedDays, result.Days.Count);
        Assert.Equal(new DateOnly(year, month, 1), result.Days[0].Date);
        Assert.Equal(new DateOnly(year, month, expectedDays), result.Days[^1].Date);
    }

    [Fact]
    public async Task Year_ReturnsEveryDay()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        var result = await host.Facade.GetYearAsync(Hyderabad, 2024, CancellationToken.None);

        Assert.Equal(366, result.Days.Count);
    }

    [Fact]
    public async Task DaylightSavingTransition_ReportsTheOffsetInForceAtEachTime()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 3, 1, 12, 0));

        var result = await host.Facade.GetDateRangeAsync(Austin, new DateOnly(2026, 3, 7), 2, CancellationToken.None);

        Assert.Equal(TimeSpan.FromHours(-6), result.Days[0].Sun.Sunrise.Offset); // CST
        Assert.Equal(TimeSpan.FromHours(-5), result.Days[1].Sun.Sunrise.Offset); // CDT
    }

    [Fact]
    public async Task PanchangDay_EndsAtNextSunrise_OnTheFollowingDate()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        var day = (await host.Facade.GetTodayAsync(Hyderabad, CancellationToken.None)).Days[0];

        Assert.Equal(day.Date.AddDays(1), DateOnly.FromDateTime(day.Sun.NextSunrise.DateTime));
        Assert.True(day.Tithis[^1].End >= day.Sun.NextSunrise);
        Assert.True(day.Tithis[0].Start <= day.Sun.Sunrise);
    }

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(1899, 12)]
    [InlineData(2101, 1)]
    public async Task Month_OutOfRange_ThrowsValidationException(int year, int month)
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        await Assert.ThrowsAsync<ValidationException>(() => host.Facade.GetMonthAsync(Hyderabad, year, month, CancellationToken.None));
    }

    [Fact]
    public async Task Range_TooLong_ThrowsValidationException()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        await Assert.ThrowsAsync<ValidationException>(
            () => host.Facade.GetDateRangeAsync(Hyderabad, new DateOnly(2026, 1, 1), 367, CancellationToken.None));
    }

    [Fact]
    public async Task UnsupportedTradition_ThrowsValidationException()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 9, 28, 6, 0));

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => host.Facade.GetTodayAsync(Hyderabad with { Tradition = "unknown" }, CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey("tradition"));
    }

    [Fact]
    public async Task PolarDay_ThrowsBusinessRuleException()
    {
        using var host = new BusinessTestHost(Instant.FromUtc(2026, 6, 21, 12, 0));

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => host.Facade.GetTodayAsync(new PanchangQuery(69.65, 18.96), CancellationToken.None));
    }
}
