using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyVedicDay.Api.Contracts.Muhurta;
using MyVedicDay.Api.Contracts.Panchang;
using MyVedicDay.Api.Tests.Integration.Infrastructure;

namespace MyVedicDay.Api.Tests.Integration.Api;

/// <summary>
/// Find a Good Time through the real HTTP pipeline, and the consistency the requirements demand: Panchang days,
/// day summaries and good-times report the same status.
/// </summary>
public sealed class MuhurtaApiTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    private const string Hyderabad = "latitude=17.385&longitude=78.4867";
    private const string Austin = "latitude=30.2672&longitude=-97.7431";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("en", "en")]
    [InlineData("te", "en")]
    public async Task Nakshatras_ReturnAllCanonicalChoicesWithLocalizedNames(string language, string expectedLanguage)
    {
        var response = await GetAsync<MuhurtaNakshatrasResponse>($"/api/v1/muhurta/nakshatras?language={language}&tradition=telugu");
        // The current content catalog is English; unsupported languages use the shared fallback.
        Assert.Equal(expectedLanguage, response.Language);
        Assert.Equal(27, response.Nakshatras.Count);
        Assert.Equal(Enumerable.Range(1, 27), response.Nakshatras.Select(star => star.Order));
        Assert.Equal("Ashwini", response.Nakshatras[0].Key);
        Assert.Equal("Revati", response.Nakshatras[^1].Key);
        Assert.All(response.Nakshatras, star => Assert.False(string.IsNullOrWhiteSpace(star.Name)));
        Assert.DoesNotContain(response.Nakshatras, star => star.Key == "Abhijit");
        Assert.Equal(27, response.Nakshatras.Select(star => star.Key).Distinct().Count());
    }

    [Theory]
    [InlineData("good-times")]
    [InlineData("good-times/explanation")]
    [InlineData("day-summaries")]
    [InlineData("good-times/best")]
    public async Task InvalidBirthNakshatra_ReturnsValidationProblem(string endpoint)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri($"/api/v1/muhurta/{endpoint}?{Hyderabad}&start=2026-10-01&nakshatra=Abhijit", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("nakshatra", out _));
    }

    [Fact]
    public async Task BirthNakshatra_IsBoundAndExplainedSeparatelyFromActiveStar()
    {
        var response = await GetAsync<GoodTimesResponse>($"/api/v1/muhurta/good-times?{Hyderabad}&date=2026-10-01&nakshatra=rohini");
        Assert.All(response.Segments, segment => Assert.Contains(segment.Reasons, reason =>
            reason.Subject is not null && reason.Subject.StartsWith("birth=Rohini;active=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Activities_ListTheCardsInOrder()
    {
        var response = await GetAsync<MuhurtaActivitiesResponse>("/api/v1/muhurta/activities");

        Assert.Equal("general", response.DefaultActivity);
        Assert.Equal(11, response.Activities.Count);
        Assert.Equal("business", response.Activities[0].Key);
        Assert.True(response.Activities.Single(activity => activity.Key == "other").RequiresInput);
    }

    [Fact]
    public async Task GoodTimes_ResolveConflicts_AndOnlyQualifyingSegmentsAreGoodTimes()
    {
        var response = await GetAsync<GoodTimesResponse>($"/api/v1/muhurta/good-times?{Hyderabad}&date=2026-10-01&activity=business&tradition=telugu");

        Assert.Equal("business", response.Activity.Key);
        Assert.Equal("telugu", response.Tradition.Key);
        Assert.NotEmpty(response.Segments);
        Assert.All(response.Segments.Where(segment => segment.IsGoodTime), segment =>
        {
            Assert.Contains(segment.Status.Status, new[] { "HighlyFavorable", "Favorable" });
            Assert.Empty(segment.Conflicts);
            Assert.Contains(segment.Id, response.GoodTimes);
        });
        Assert.All(response.Segments.Where(segment => segment.Conflicts.Count > 0), segment => Assert.False(segment.IsGoodTime));
        Assert.Contains(response.Segments.SelectMany(segment => segment.Conflicts), conflict => conflict is { Code: "RAHU_KALAM", Label: "Overlaps Rahu Kalam" });
        Assert.Equal(response.GoodTimes.Count == 0, response.NoGoodTimeReason is not null);
        Assert.Contains("consult a qualified astrologer", response.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherText_IsMappedAndReported()
    {
        var response = await GetAsync<GoodTimesResponse>($"/api/v1/muhurta/good-times?{Austin}&activity=other&otherText={Uri.EscapeDataString("Opening a bakery shop")}");

        Assert.Equal(("business", "other", "Opening a bakery shop"), (response.Activity.Key, response.Activity.Requested, response.Activity.RequestedText));
        Assert.NotEmpty(response.Notices);
        Assert.Equal(response.LocalToday, response.Date);
    }

    [Theory]
    [InlineData("activity=skydiving")]
    [InlineData("activity=other")]
    [InlineData("activity=other&otherText=%3Cscript%3E")]
    [InlineData("date=2101-01-01")]
    public async Task GoodTimes_RejectInvalidParameters(string parameters)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/v1/muhurta/good-times?{Hyderabad}&{parameters}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CalendarHomeAndGoodTimes_ReportTheSameStatus()
    {
        var month = await GetAsync<PanchangResponse>($"/api/v1/panchang/month/2026/10?{Hyderabad}&tradition=tamil");
        var summaries = await GetAsync<DaySummariesResponse>($"/api/v1/muhurta/day-summaries?{Hyderabad}&tradition=tamil&start=2026-10-01&days=31");

        Assert.Equal(31, summaries.Days.Count);
        Assert.Equal(
            summaries.Days.Select(day => (day.Date, day.Status.Status, day.GoodTimeCount, day.BestGoodTime?.Start)),
            month.Days.Select(day => (day.GoodTime.Date, day.GoodTime.Status.Status, day.GoodTime.GoodTimeCount, day.GoodTime.BestGoodTime?.Start)));

        var fifth = await GetAsync<GoodTimesResponse>($"/api/v1/muhurta/good-times?{Hyderabad}&tradition=tamil&date=2026-10-05");
        Assert.Equal(fifth.DayStatus.Status, summaries.Days[4].Status.Status);
        Assert.Equal(fifth.GoodTimes.Count, summaries.Days[4].GoodTimeCount);
    }

    [Fact]
    public async Task PanchangToday_CarriesTheGoodTimeSummary()
    {
        var today = await GetAsync<PanchangResponse>($"/api/v1/panchang/today?{Austin}");

        var day = Assert.Single(today.Days);
        Assert.Equal(day.Date, day.GoodTime.Date);
        Assert.Contains(day.GoodTime.Status.Status, new[] { "HighlyFavorable", "Favorable", "Mixed", "Caution", "Avoid" });
        Assert.Equal(day.GoodTime.GoodTimeCount > 0, day.GoodTime.BestGoodTime is not null);
    }

    [Fact]
    public async Task Explanation_WithoutAi_IsTheDeterministicTemplate()
    {
        var response = await GetAsync<GoodTimeExplanationResponse>($"/api/v1/muhurta/good-times/explanation?{Hyderabad}&date=2026-10-01&activity=business");

        Assert.Equal("template", response.Source);
        Assert.Null(response.Provider);
        Assert.False(string.IsNullOrWhiteSpace(response.Summary));
        Assert.Equal("business", response.Activity.Key);
        Assert.Contains("consult a qualified astrologer", response.Disclaimer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BestGoodTimes_AreGoodTimesThatMatchTheirDay()
    {
        var best = await GetAsync<BestGoodTimesResponse>($"/api/v1/muhurta/good-times/best?{Hyderabad}&activity=business");

        Assert.Equal(best.LocalToday, best.SearchedFrom);
        Assert.Equal(best.LocalToday.AddDays(29), best.SearchedThrough);
        Assert.InRange(best.Options.Count, 0, 3);
        Assert.Equal(best.Options.Count == 0, best.NoGoodTimeMessage is not null);
        Assert.Equal(best.Options.Count, best.Options.Select(option => option.Date).Distinct().Count());
        foreach (var option in best.Options)
        {
            Assert.True(option.Segment.IsGoodTime);
            var day = await GetAsync<GoodTimesResponse>($"/api/v1/muhurta/good-times?{Hyderabad}&activity=business&date={option.Date:yyyy-MM-dd}");
            Assert.Contains(option.Segment.Id, day.GoodTimes);
            Assert.Equal(day.Segments.Single(segment => segment.Id == option.Segment.Id).Start, option.Segment.Start);
        }
    }

    [Fact]
    public async Task DaySummaries_ForOther_UseTheDescription()
    {
        var month = await GetAsync<DaySummariesResponse>(
            $"/api/v1/muhurta/day-summaries?{Hyderabad}&start=2026-10-01&days=31&activity=other&otherText={Uri.EscapeDataString("Opening a bakery shop")}");

        Assert.Equal(("business", "other"), (month.Activity.Key, month.Activity.Requested));
    }

    [Theory]
    [InlineData("count=0")]
    [InlineData("count=11")]
    [InlineData("days=91")]
    [InlineData("activity=other")]
    public async Task BestGoodTimes_RejectInvalidParameters(string parameters)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/v1/muhurta/good-times/best?{Hyderabad}&{parameters}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }
}
