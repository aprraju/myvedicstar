using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyVedicDay.Api.Contracts.Festivals;
using MyVedicDay.Api.Contracts.Panchang;
using MyVedicDay.Api.Tests.Integration.Infrastructure;

namespace MyVedicDay.Api.Tests.Integration.Api;

/// <summary>
/// Festivals through the real HTTP pipeline: the upcoming list, and festivals inside every Panchang day (the host
/// composes the Panchang and Festivals facades).
/// </summary>
public sealed class FestivalsApiTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    private const string Hyderabad = "latitude=17.385&longitude=78.4867";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Upcoming_DefaultsToThirtyDaysFromLocalToday()
    {
        var response = await GetAsync<FestivalsResponse>($"/api/v1/festivals/upcoming?{Hyderabad}&tradition=telugu");

        Assert.Equal("telugu", response.Tradition.Key);
        Assert.Equal("Telugu", response.Tradition.Name);
        Assert.Equal(response.LocalToday, response.From);
        Assert.Equal(response.LocalToday.AddDays(29), response.To);
        Assert.False(string.IsNullOrWhiteSpace(response.RulesVersion));
        Assert.NotEmpty(response.Festivals);
        Assert.All(response.Festivals, festival =>
        {
            Assert.InRange(festival.Date, response.From, response.To);
            Assert.Equal(festival.Date.DayNumber - response.LocalToday.DayNumber, festival.DaysFromToday);
            Assert.Contains(festival.Category, new[] { "festival", "observance" });
            Assert.False(string.IsNullOrWhiteSpace(festival.Timing.BasisKey));
        });
    }

    [Fact]
    public async Task Upcoming_HonorsTheRequestedDays()
    {
        var response = await GetAsync<FestivalsResponse>($"/api/v1/festivals/upcoming?{Hyderabad}&days=60");

        Assert.Equal(response.LocalToday.AddDays(59), response.To);
    }

    [Theory]
    [InlineData("days=0")]
    [InlineData("days=91")]
    [InlineData("tradition=klingon")]
    public async Task Upcoming_RejectsInvalidParameters(string parameter)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/v1/festivals/upcoming?{Hyderabad}&{parameter}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PanchangMonth_ListsEachDaysFestivals_WithTheTraditionsNames()
    {
        var response = await GetAsync<PanchangResponse>($"/api/v1/panchang/month/2026/9?{Hyderabad}&tradition=telugu");

        Assert.All(response.Days, day => Assert.NotNull(day.Festivals));
        var ganeshChaturthi = Assert.Single(response.Days, day => day.Date == new DateOnly(2026, 9, 14));
        var festival = Assert.Single(ganeshChaturthi.Festivals, festival => festival.Key == "GaneshChaturthi");
        Assert.Equal("Vinayaka Chavithi", festival.Name);
        Assert.All(response.Days.SelectMany(day => day.Festivals.Select(festival => (day.Date, festival.Date))), pair => Assert.Equal(pair.Item1, pair.Item2));
    }

    [Fact]
    public async Task PanchangTodayAndTenDays_IncludeFestivals_MatchingTheUpcomingList()
    {
        var today = await GetAsync<PanchangResponse>($"/api/v1/panchang/today?{Hyderabad}&tradition=tamil");
        var tenDays = await GetAsync<PanchangResponse>($"/api/v1/panchang/ten-days?{Hyderabad}&tradition=tamil");
        var upcoming = await GetAsync<FestivalsResponse>($"/api/v1/festivals/upcoming?{Hyderabad}&tradition=tamil&days=10");

        Assert.Equal(
            upcoming.Festivals.Where(festival => festival.DaysFromToday == 0).Select(festival => festival.Key),
            Assert.Single(today.Days).Festivals.Select(festival => festival.Key));
        Assert.Equal(
            upcoming.Festivals.Select(festival => (festival.Date, festival.Key)),
            tenDays.Days.SelectMany(day => day.Festivals.Select(festival => (festival.Date, festival.Key))));
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }
}
