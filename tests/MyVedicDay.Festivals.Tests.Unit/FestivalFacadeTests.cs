using MyVedicDay.Festivals.Business;
using MyVedicDay.Location;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Festivals.Tests.Unit;

/// <summary>
/// The festival facade: upcoming lists from the location's local today, tradition-specific festivals and regional
/// names, superseded observances, and per-day caching.
/// </summary>
public sealed class FestivalFacadeTests
{
    private static readonly LocationQuery Hyderabad = new(17.385, 78.4867);

    // 2026-09-14 08:30 IST: Ganesh Chaturthi (Vinayaka Chavithi) in Hyderabad.
    private static readonly Instant GaneshChaturthiMorning = Instant.FromUtc(2026, 9, 14, 3, 0);

    [Fact]
    public async Task Upcoming_StartsWithLocalToday_AndCoversTheRequestedDays()
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);

        var result = await host.Facade.GetUpcomingAsync(new FestivalQuery(Hyderabad), 30, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 14), result.LocalToday);
        Assert.Equal(new DateOnly(2026, 9, 14), result.From);
        Assert.Equal(new DateOnly(2026, 10, 13), result.To);
        Assert.NotEmpty(result.Festivals);
        Assert.All(result.Festivals, festival =>
        {
            Assert.InRange(festival.Date, result.From, result.To);
            Assert.Equal(festival.Date.DayNumber - result.LocalToday.DayNumber, festival.DaysFromToday);
            Assert.False(string.IsNullOrWhiteSpace(festival.Name));
            Assert.False(string.IsNullOrWhiteSpace(festival.DateDisplay));
        });
        Assert.Equal(result.Festivals.OrderBy(festival => festival.Date).Select(festival => festival.Date), result.Festivals.Select(festival => festival.Date));
    }

    [Fact]
    public async Task FestiveToday_IsListedWithDaysFromTodayZero_AndItsObservanceWindow()
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);

        var result = await host.Facade.GetUpcomingAsync(new FestivalQuery(Hyderabad, "telugu"), 1, CancellationToken.None);

        var festival = Assert.Single(result.Festivals);
        Assert.Equal("GaneshChaturthi", festival.Key);
        Assert.Equal("Vinayaka Chavithi", festival.Name);
        Assert.Equal(0, festival.DaysFromToday);
        Assert.Equal("ShuklaChaturthi", festival.Timing.BasisKey);
        Assert.Equal("Madhyahna", festival.Timing.ObservanceTime);
        Assert.NotNull(festival.Timing.ObservanceStart);
        Assert.EndsWith(" IST", festival.Timing.ObservanceStartDisplay, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("telugu", "Vinayaka Chavithi")]
    [InlineData("tamil", "Vinayagar Chathurthi")]
    [InlineData("north-indian", "Ganesh Chaturthi")]
    [InlineData("all", "Ganesh Chaturthi")]
    public async Task Tradition_ChoosesTheRegionalName_NotTheDate(string tradition, string expectedName)
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);

        var result = await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad, tradition), new DateOnly(2026, 9, 14), 1, CancellationToken.None);

        var festival = Assert.Single(result.Festivals, festival => festival.Key == "GaneshChaturthi");
        Assert.Equal(expectedName, festival.Name);
        Assert.Equal(new DateOnly(2026, 9, 14), festival.Date);
        Assert.Equal(tradition, result.Tradition.Key);
    }

    [Fact]
    public async Task Tradition_ChoosesWhichFestivalsAreListed()
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);
        var start = new DateOnly(2026, 10, 1);

        var northIndian = await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad, "north-indian"), start, 60, CancellationToken.None);
        var telugu = await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad, "telugu"), start, 60, CancellationToken.None);
        var all = await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad, "all"), start, 60, CancellationToken.None);

        Assert.Contains(northIndian.Festivals, festival => festival.Key == "KarwaChauth");
        Assert.DoesNotContain(telugu.Festivals, festival => festival.Key == "KarwaChauth");
        Assert.Contains(telugu.Festivals, festival => festival.Key == "NagulaChavithi");
        Assert.DoesNotContain(northIndian.Festivals, festival => festival.Key == "NagulaChavithi");

        // Festivals every tradition observes fall on the same date for all of them.
        var diwali = Assert.Single(northIndian.Festivals, festival => festival.Key == "Diwali").Date;
        Assert.Equal(diwali, Assert.Single(telugu.Festivals, festival => festival.Key == "Diwali").Date);
        Assert.Superset(
            northIndian.Festivals.Select(festival => festival.Key).ToHashSet(),
            all.Festivals.Select(festival => festival.Key).ToHashSet());
    }

    [Fact]
    public async Task NamedFestival_HidesTheGenericObservanceOnTheSameDate()
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);
        var start = new DateOnly(2026, 12, 10);

        var telugu = await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad, "telugu"), start, 45, CancellationToken.None);
        var northIndian = await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad, "north-indian"), start, 45, CancellationToken.None);

        var vaikuntha = Assert.Single(telugu.Festivals, festival => festival.Key == "VaikunthaEkadashi");
        Assert.Equal("Mukkoti Ekadashi", vaikuntha.Name);
        Assert.DoesNotContain(telugu.Festivals, festival => festival.Date == vaikuntha.Date && festival.Key == "Ekadashi");

        // North India does not observe Vaikuntha Ekadashi, so the same day is still an Ekadashi (or Gita Jayanti).
        Assert.Contains(northIndian.Festivals, festival => festival.Date == vaikuntha.Date && festival.Key is "Ekadashi" or "GitaJayanti");
    }

    [Fact]
    public async Task Observances_AreCategorized_AndListedAfterFestivals()
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);

        var result = await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad), new DateOnly(2026, 1, 1), 366, CancellationToken.None);

        Assert.Contains(result.Festivals, festival => festival.Key == "Ekadashi" && festival.Category == "observance" && festival.CategoryName == "Observance");
        Assert.Contains(result.Festivals, festival => festival.Key == "MakarSankranti" && festival.Category == "festival");
        Assert.InRange(result.Festivals.Count(festival => festival.Key == "Purnima"), 6, 13);
        foreach (var day in result.Festivals.GroupBy(festival => festival.Date))
        {
            var categories = day.Select(festival => festival.Category).ToArray();
            Assert.Equal(categories.OrderBy(category => category == "observance" ? 1 : 0), categories);
        }
    }

    [Fact]
    public async Task Days_AreCachedIndividually_AndOnlyMissingDaysAreCalculated()
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);

        await host.Facade.GetUpcomingAsync(new FestivalQuery(Hyderabad, "telugu"), 30, CancellationToken.None);
        await host.Facade.GetUpcomingAsync(new FestivalQuery(Hyderabad, "tamil"), 30, CancellationToken.None);
        Assert.Equal(1, host.Calculator.Calls); // cached days are tradition-neutral

        // Today's entry expires at the location's next local midnight (00:00 IST), not at UTC or server midnight.
        var today = Assert.Single(host.Cache.Expirations, entry => entry.Key.Contains(":2026-09-14:", StringComparison.Ordinal));
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 18, 30, 0, TimeSpan.Zero), today.Value);
        Assert.StartsWith("festivals:17.39:78.49:Asia/Kolkata:2026-09-14:", today.Key, StringComparison.Ordinal);

        await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad), new DateOnly(2026, 9, 20), 10, CancellationToken.None);
        Assert.Equal(1, host.Calculator.Calls); // fully inside the cached range

        await host.Facade.GetRangeAsync(new FestivalQuery(Hyderabad), new DateOnly(2026, 10, 10), 10, CancellationToken.None);
        Assert.Equal(2, host.Calculator.Calls); // partly new
    }

    [Fact]
    public async Task CachedDays_RoundTripThroughTheCacheSerializer()
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);
        await host.Facade.GetUpcomingAsync(new FestivalQuery(Hyderabad), 1, CancellationToken.None);
        var key = Assert.Single(host.Cache.Expirations.Keys);
        var cached = await host.Cache.GetAsync<Business.Daily.FestivalDayData>(key, CancellationToken.None);

        // Same serializer settings as the distributed cache: a shape it cannot read would turn every hit into a miss.
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var json = System.Text.Json.JsonSerializer.Serialize(cached, options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<Business.Daily.FestivalDayData>(json, options);

        Assert.NotNull(restored);
        Assert.Equal("GaneshChaturthi", Assert.Single(restored.Festivals).Key);
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(restored, options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task UpcomingDays_OutOfRange_IsAValidationError(int days)
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);

        await Assert.ThrowsAsync<ValidationException>(() => host.Facade.GetUpcomingAsync(new FestivalQuery(Hyderabad), days, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownTradition_IsAValidationError()
    {
        using var host = new FestivalTestHost(GaneshChaturthiMorning);

        await Assert.ThrowsAsync<ValidationException>(() =>
            host.Facade.GetUpcomingAsync(new FestivalQuery(Hyderabad, "klingon"), 30, CancellationToken.None));
    }
}
