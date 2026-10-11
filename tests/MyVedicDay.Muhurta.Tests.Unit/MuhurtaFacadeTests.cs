using MyVedicDay.Location;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Muhurta.Tests.Unit;

/// <summary>
/// Find a Good Time through the facade: activity-specific results, the "never promote" guarantee across activities and
/// traditions, Other text mapping, now/next, summaries and caching.
/// </summary>
public sealed class MuhurtaFacadeTests
{
    private static readonly MuhurtaQuery Hyderabad = new(new LocationQuery(17.385, 78.4867));

    // 2026-10-01 08:30 IST (Thursday).
    private static readonly Instant Morning = Instant.FromUtc(2026, 10, 1, 3, 0);

    private static readonly string[] GoodStatuses = ["HighlyFavorable", "Favorable"];

    [Fact]
    public async Task Activities_AreListedWithNamesIcons_AndGeneralIsTheDefault()
    {
        using var host = new MuhurtaTestHost(Morning);

        var result = await host.Facade.GetActivitiesAsync(null, null, CancellationToken.None);

        Assert.Equal("general", result.DefaultActivity);
        Assert.Equal(11, result.Activities.Count);
        Assert.Equal("Puja / Spiritual", result.Activities.Single(activity => activity.Key == "puja").Name);
        Assert.True(result.Activities.Single(activity => activity.Key == "other").RequiresInput);
        Assert.All(result.Activities, activity => Assert.False(string.IsNullOrWhiteSpace(activity.Icon)));
        Assert.Equal(Enumerable.Range(1, 11), result.Activities.Select(activity => activity.Order));
    }

    [Fact]
    public async Task GoodTimes_AreNeverMixedCautionAvoidOrConflicting_ForAnyActivityOrTradition()
    {
        using var host = new MuhurtaTestHost(Morning);
        var activities = (await host.Facade.GetActivitiesAsync(null, null, CancellationToken.None)).Activities.Where(activity => !activity.RequiresInput);
        var anyGoodTime = false;

        foreach (var tradition in new[] { "all", "telugu", "tamil", "gujarati", "north-indian" })
        {
            foreach (var activity in activities)
            {
                for (var day = 0; day < 7; day++)
                {
                    var result = await host.Facade.GetGoodTimesAsync(
                        Hyderabad with { Tradition = tradition }, new GoodTimesRequest(new DateOnly(2026, 10, 1).AddDays(day), activity.Key), CancellationToken.None);

                    var goodTimes = result.Segments.Where(segment => segment.IsGoodTime).ToArray();
                    Assert.Equal(goodTimes.Select(segment => segment.Id).Order(), result.GoodTimes.Order());
                    Assert.All(goodTimes, segment =>
                    {
                        Assert.Contains(segment.Status.Status, GoodStatuses);
                        Assert.Empty(segment.Conflicts);
                    });
                    Assert.Equal(goodTimes.Length == 0, result.NoGoodTimeMessage is not null);
                    if (activity.Completeness == "partial")
                    {
                        Assert.DoesNotContain(result.Segments, segment => segment.Status.Status == "HighlyFavorable");
                    }

                    anyGoodTime |= goodTimes.Length > 0;
                }
            }
        }

        Assert.True(anyGoodTime);
    }

    [Fact]
    public async Task SameDay_DiffersByActivity()
    {
        using var host = new MuhurtaTestHost(Morning);

        var statuses = new Dictionary<string, string>();
        foreach (var activity in new[] { "puja", "business", "wedding", "travel" })
        {
            var result = await host.Facade.GetGoodTimesAsync(Hyderabad, new GoodTimesRequest(new DateOnly(2026, 10, 1), activity), CancellationToken.None);
            statuses[activity] = string.Join(',', result.Segments.Select(segment => $"{segment.Start:HHmm}{segment.Status.Status}"));
            Assert.Equal(activity, result.Activity.Key);
        }

        Assert.True(statuses.Values.Distinct().Count() > 1, "Activities produce different evaluations.");
    }

    [Fact]
    public async Task Segments_AreExplainedWithLocalizedLabels_AndConflictsCarryTimes()
    {
        using var host = new MuhurtaTestHost(Morning);

        var result = await host.Facade.GetGoodTimesAsync(Hyderabad with { Tradition = "telugu" }, new GoodTimesRequest(new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.Equal("general", result.Activity.Key);
        Assert.All(result.Segments, segment =>
        {
            Assert.EndsWith("for General", segment.Headline, StringComparison.Ordinal);
            Assert.All(segment.Reasons, reason => Assert.False(string.IsNullOrWhiteSpace(reason.Label)));
            Assert.All(segment.Conflicts, conflict =>
            {
                Assert.StartsWith("Overlaps", conflict.Label, StringComparison.Ordinal);
                Assert.True(conflict.OverlapStart >= segment.Start && conflict.OverlapEnd <= segment.End);
            });
        });
        Assert.Contains(result.Segments, segment => segment.Conflicts.Any(conflict => conflict.Code == "RAHU_KALAM"));
        Assert.Contains(result.Segments.Where(segment => segment.IsGoodTime).SelectMany(segment => segment.Reasons), reason => reason.Code == "NO_RAHU_KALAM_CONFLICT");
        Assert.All(result.Segments.Zip(result.Segments.Skip(1)), pair => Assert.Equal(pair.First.End, pair.Second.Start));
        Assert.Contains("consult a qualified astrologer", result.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Today_ReportsTheSegmentInProgressAndTheNextGoodTime()
    {
        using var host = new MuhurtaTestHost(Morning);

        var result = await host.Facade.GetGoodTimesAsync(Hyderabad, new GoodTimesRequest(), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 1), result.Date);
        var now = Morning.ToDateTimeOffset();
        var current = Assert.Single(result.Segments, segment => segment.Id == result.NowSegmentId);
        Assert.True(current.Start <= now && now < current.End);
        if (result.NextGoodTimeId is { } next)
        {
            var segment = Assert.Single(result.Segments, candidate => candidate.Id == next);
            Assert.True(segment.IsGoodTime && segment.Start > now);
        }

        var tomorrow = await host.Facade.GetGoodTimesAsync(Hyderabad, new GoodTimesRequest(new DateOnly(2026, 10, 2)), CancellationToken.None);
        Assert.Null(tomorrow.NowSegmentId);
        Assert.Null(tomorrow.NextGoodTimeId);
    }

    [Theory]
    [InlineData("Opening my new bakery shop", "business")]
    [InlineData("road trip to Tirupati", "travel")]
    [InlineData("buying a scooter", "vehicle")]
    [InlineData("Gruhapravesam", "housewarming")]
    [InlineData("painting the fence", "general")]
    public async Task OtherText_IsMappedToAKnownActivity_WithANotice(string text, string expected)
    {
        using var host = new MuhurtaTestHost(Morning);

        var result = await host.Facade.GetGoodTimesAsync(Hyderabad, new GoodTimesRequest(Activity: "other", OtherText: text), CancellationToken.None);

        Assert.Equal(expected, result.Activity.Key);
        Assert.Equal("other", result.Activity.Requested);
        Assert.Equal(text, result.Activity.RequestedText);
        Assert.Contains(result.Notices, notice => notice.StartsWith("Evaluated as", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("other", null)]
    [InlineData("other", "<script>alert(1)</script>")]
    [InlineData("other", "an extremely long description that keeps going on and on far beyond what any reasonable activity name needs")]
    [InlineData("skydiving", null)]
    public async Task InvalidActivityOrText_IsAValidationError(string activity, string? text)
    {
        using var host = new MuhurtaTestHost(Morning);

        await Assert.ThrowsAsync<ValidationException>(() =>
            host.Facade.GetGoodTimesAsync(Hyderabad, new GoodTimesRequest(Activity: activity, OtherText: text), CancellationToken.None));
    }

    [Fact]
    public async Task PartialActivities_CarryANotice()
    {
        using var host = new MuhurtaTestHost(Morning);

        var result = await host.Facade.GetGoodTimesAsync(Hyderabad, new GoodTimesRequest(Activity: "wedding"), CancellationToken.None);

        Assert.Equal("partial", result.Activity.Completeness);
        Assert.Contains(result.Notices, notice => notice.Contains("Lagna", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DaySummaries_MatchTheDayEvaluations()
    {
        using var host = new MuhurtaTestHost(Morning);

        var summaries = await host.Facade.GetDaySummariesAsync(Hyderabad, new DateOnly(2026, 10, 1), 10, null, null, CancellationToken.None);

        Assert.Equal(10, summaries.Days.Count);
        foreach (var summary in summaries.Days)
        {
            var day = await host.Facade.GetGoodTimesAsync(Hyderabad, new GoodTimesRequest(summary.Date), CancellationToken.None);
            Assert.Equal(day.DayStatus.Status, summary.Status.Status);
            Assert.Equal(day.GoodTimes.Count, summary.GoodTimeCount);
            Assert.Equal(day.GoodTimes.Count > 0 ? day.Segments.Single(segment => segment.Id == day.GoodTimes[0]).Start : null, summary.BestGoodTime?.Start);
            Assert.True(summary.BestGoodTime is null || summary.Status.Status is "HighlyFavorable" or "Favorable");
        }
    }

    [Fact]
    public async Task Days_AreCachedPerActivityAndTradition_ExpiringAtLocalMidnight()
    {
        using var host = new MuhurtaTestHost(Morning);
        var request = new GoodTimesRequest(new DateOnly(2026, 10, 1), "business");

        await host.Facade.GetGoodTimesAsync(Hyderabad, request, CancellationToken.None);
        await host.Facade.GetGoodTimesAsync(Hyderabad, request, CancellationToken.None);
        Assert.Equal(1, host.Calculator.Calls);

        await host.Facade.GetGoodTimesAsync(Hyderabad with { Tradition = "gujarati" }, request, CancellationToken.None);
        await host.Facade.GetGoodTimesAsync(Hyderabad, request with { Activity = "travel" }, CancellationToken.None);
        Assert.Equal(3, host.Calculator.Calls);

        var expiry = Assert.Single(host.Cache.Expirations, entry => entry.Key.Contains(":business:all:", StringComparison.Ordinal)).Value;
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 18, 30, 0, TimeSpan.Zero), expiry); // 00:00 IST
    }

    [Theory]
    [InlineData(0)]
    [InlineData(367)]
    public async Task SummaryDays_OutOfRange_IsAValidationError(int days)
    {
        using var host = new MuhurtaTestHost(Morning);

        await Assert.ThrowsAsync<ValidationException>(() =>
            host.Facade.GetDaySummariesAsync(Hyderabad, new DateOnly(2026, 10, 1), days, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task BestGoodTimes_AreTheDaysBestRemainingGoodTimes_HighlyFavorableFirstThenSoonest()
    {
        using var host = new MuhurtaTestHost(Morning);

        var best = await host.Facade.GetBestGoodTimesAsync(Hyderabad, new BestGoodTimesRequest("business"), CancellationToken.None);

        // Expected from the day evaluations: each day's first Good Time (best first) that has not ended.
        var now = Morning.ToDateTimeOffset();
        var candidates = new List<(DateOnly Date, GoodTimeSegment Segment)>();
        for (var date = best.SearchedFrom; date <= best.SearchedThrough; date = date.AddDays(1))
        {
            var day = await host.Facade.GetGoodTimesAsync(Hyderabad, new GoodTimesRequest(date, "business"), CancellationToken.None);
            var first = day.GoodTimes.Select(id => day.Segments.Single(segment => segment.Id == id)).FirstOrDefault(segment => segment.End > now);
            if (first is not null)
            {
                candidates.Add((date, first));
            }
        }

        var expected = candidates
            .OrderBy(candidate => candidate.Segment.Status.Status == "HighlyFavorable" ? 0 : 1)
            .ThenBy(candidate => candidate.Date)
            .Take(3)
            .ToArray();

        Assert.Equal(new DateOnly(2026, 10, 1), best.SearchedFrom);
        Assert.Equal(new DateOnly(2026, 10, 30), best.SearchedThrough);
        Assert.Equal(3, best.Options.Count);
        Assert.Equal([1, 2, 3], best.Options.Select(option => option.Rank));
        Assert.Equal(expected.Select(candidate => candidate.Date), best.Options.Select(option => option.Date));
        Assert.Equal(expected.Select(candidate => (candidate.Segment.Id, candidate.Segment.Start)), best.Options.Select(option => (option.Segment.Id, option.Segment.Start)));
        Assert.All(best.Options, option =>
        {
            Assert.True(option.Segment.IsGoodTime);
            Assert.Contains(option.Segment.Status.Status, GoodStatuses);
            Assert.Empty(option.Segment.Conflicts);
        });
        Assert.Null(best.NoGoodTimeMessage);
        Assert.False(string.IsNullOrWhiteSpace(best.Disclaimer));
    }

    [Fact]
    public async Task OtherText_IsUsedByBestGoodTimesAndDaySummaries()
    {
        using var host = new MuhurtaTestHost(Morning);

        var best = await host.Facade.GetBestGoodTimesAsync(Hyderabad, new BestGoodTimesRequest("other", "Opening a bakery shop"), CancellationToken.None);
        var month = await host.Facade.GetDaySummariesAsync(Hyderabad, new DateOnly(2026, 10, 1), 31, "other", "Opening a bakery shop", CancellationToken.None);

        Assert.Equal("business", best.Activity.Key);
        Assert.Equal("other", best.Activity.Requested);
        Assert.Contains("Evaluated as Business.", best.Notices);
        Assert.Equal("business", month.Activity.Key);
        Assert.Equal(31, month.Days.Count);
        await Assert.ThrowsAsync<ValidationException>(() =>
            host.Facade.GetDaySummariesAsync(Hyderabad, new DateOnly(2026, 10, 1), 31, "other", null, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(11, null)]
    [InlineData(null, 0)]
    [InlineData(null, 91)]
    public async Task BestGoodTimes_CountOrDaysOutOfRange_IsAValidationError(int? count, int? days)
    {
        using var host = new MuhurtaTestHost(Morning);

        await Assert.ThrowsAsync<ValidationException>(() =>
            host.Facade.GetBestGoodTimesAsync(Hyderabad, new BestGoodTimesRequest(Count: count, Days: days), CancellationToken.None));
    }
}
