using MyVedicDay.Muhurta.Business.Daily;
using NodaTime;

namespace MyVedicDay.Muhurta.Tests.Unit;

/// <summary>Choosing the next best Good Times: only Good Times, one per day, Highly Favorable first, then soonest.</summary>
public sealed class BestGoodTimeSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly LocalDate Today = new(2026, 10, 1);

    [Fact]
    public void Ranks_HighlyFavorableBeforeFavorable_ThenSoonest_OnePerDay()
    {
        var selector = new BestGoodTimeSelector(3, Now);
        selector.Consider(Today, Day(Today, ("Favorable", true, 11), ("Favorable", true, 13)));
        selector.Consider(Today.PlusDays(1), Day(Today.PlusDays(1), ("Favorable", true, 9)));
        selector.Consider(Today.PlusDays(2), Day(Today.PlusDays(2), ("HighlyFavorable", true, 9)));
        selector.Consider(Today.PlusDays(3), Day(Today.PlusDays(3), ("Favorable", true, 9)));

        Assert.Equal([Today.PlusDays(2), Today, Today.PlusDays(1)], selector.Results.Select(choice => choice.Date));
        Assert.Equal(0, selector.Results[1].SegmentIndex); // the day's best-ranked Good Time
    }

    [Fact]
    public void SkipsGoodTimesThatHaveEnded_AndNeverPromotesOtherSegments()
    {
        var selector = new BestGoodTimeSelector(3, Now);
        // Today: the best Good Time ended at 09:00; the next one still counts. Tomorrow: Mixed only, never chosen.
        selector.Consider(Today, Day(Today, ("HighlyFavorable", true, 8), ("Favorable", true, 12)));
        selector.Consider(Today.PlusDays(1), Day(Today.PlusDays(1), ("Mixed", false, 9)));

        var choice = Assert.Single(selector.Results);
        Assert.Equal(1, choice.SegmentIndex);
        Assert.False(selector.IsComplete);
    }

    [Fact]
    public void IsComplete_OnceEnoughHighlyFavorableDaysAreFound()
    {
        var selector = new BestGoodTimeSelector(2, Now);
        selector.Consider(Today, Day(Today, ("HighlyFavorable", true, 11)));
        Assert.False(selector.IsComplete);
        selector.Consider(Today.PlusDays(1), Day(Today.PlusDays(1), ("Favorable", true, 9)));
        Assert.False(selector.IsComplete);
        selector.Consider(Today.PlusDays(2), Day(Today.PlusDays(2), ("HighlyFavorable", true, 9)));
        Assert.True(selector.IsComplete);
    }

    [Fact]
    public void NoGoodTimes_GiveNoResults()
    {
        var selector = new BestGoodTimeSelector(3, Now);
        selector.Consider(Today, Day(Today, ("Caution", false, 11), ("Avoid", false, 13)));

        Assert.Empty(selector.Results);
    }

    /// <summary>A day whose Good Times (the segments flagged good) are ranked in the order given; each lasts one hour.</summary>
    private static MuhurtaDayData Day(LocalDate date, params (string Status, bool IsGoodTime, int StartHour)[] segments)
    {
        var data = segments
            .Select(segment =>
            {
                var start = new DateTimeOffset(date.Year, date.Month, date.Day, segment.StartHour, 0, 0, TimeSpan.Zero);
                return new MuhurtaSegmentData(start, start.AddHours(1), segment.Status, 0, segment.IsGoodTime, [], []);
            })
            .ToArray();
        var goodTimes = data.Select((segment, index) => (segment, index)).Where(item => item.segment.IsGoodTime).Select(item => item.index).ToArray();
        return new MuhurtaDayData(date.ToDateOnly(), "Favorable", data, goodTimes, goodTimes.Length == 0 ? "NO_QUALIFYING_PERIOD" : null);
    }
}
