using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyVedicDay.Api.Contracts.Locations;
using MyVedicDay.Api.Contracts.Panchang;
using MyVedicDay.Api.Contracts.Traditions;
using MyVedicDay.Api.Tests.Integration.Infrastructure;

namespace MyVedicDay.Api.Tests.Integration.Api;

/// <summary>
/// Exercises API → Business Facade → Calculation/Cache through the real HTTP pipeline (in-memory distributed cache).
/// </summary>
public sealed class PanchangApiTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    private const string Hyderabad = "latitude=17.385&longitude=78.4867";
    private const string Austin = "latitude=30.2672&longitude=-97.7431";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Today_ReturnsOneDayInTheLocationsTimeZone()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/today?{Hyderabad}");

        Assert.Equal("Asia/Kolkata", response.Location.TimeZone);
        Assert.Equal("all", response.Tradition.Key);
        Assert.Equal("drik", response.CalculationMethod);
        var day = Assert.Single(response.Days);
        Assert.Equal(response.LocalToday, day.Date);
        Assert.Equal(TimeSpan.FromHours(5.5), day.Sunrise.Offset);
        Assert.NotEmpty(day.Tithis);
        Assert.NotEmpty(day.Nakshatras);
        Assert.Contains(day.InauspiciousPeriods, period => period.Kind == "RahuKalam");
        Assert.Contains(day.AuspiciousPeriods, period => period.Kind == "BrahmaMuhurta");
    }

    [Fact]
    public async Task Tomorrow_IsTheDayAfterLocalToday()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/tomorrow?{Austin}");

        Assert.Equal(response.LocalToday.AddDays(1), Assert.Single(response.Days).Date);
    }

    [Fact]
    public async Task TenDays_ReturnsConsecutiveDaysFromLocalToday()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/ten-days?{Hyderabad}");

        Assert.Equal(10, response.Days.Count);
        for (var i = 0; i < response.Days.Count; i++)
        {
            Assert.Equal(response.LocalToday.AddDays(i), response.Days[i].Date);
        }
    }

    [Fact]
    public async Task Month_ReturnsEveryDayOfTheMonth()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/month/2024/2?{Hyderabad}");

        Assert.Equal(29, response.Days.Count);
    }

    [Fact]
    public async Task Year_ReturnsEveryDayOfTheYear()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/year/2025?{Austin}");

        Assert.Equal(365, response.Days.Count);
    }

    [Fact]
    public async Task SpecificDate_ReportsTransitionsWithExactStartAndEnd()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/2024-04-08?{Hyderabad}");

        var day = Assert.Single(response.Days);
        Assert.Contains(day.Tithis, tithi => tithi.Name == "Amavasya");
        Assert.Contains(day.Tithis, tithi => tithi.Name == "Shukla Pratipada");
        for (var i = 1; i < day.Tithis.Count; i++)
        {
            Assert.Equal(day.Tithis[i - 1].End, day.Tithis[i].Start);
        }
    }

    [Fact]
    public async Task Range_AcrossDaylightSavingStart_ReportsEachDaysOffset()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/range?start=2026-03-07&days=2&{Austin}");

        Assert.Equal(TimeSpan.FromHours(-6), response.Days[0].Sunrise.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), response.Days[1].Sunrise.Offset);
    }

    [Fact]
    public async Task ClientTimeZone_IsNotTrustedOverCoordinates()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/today?{Hyderabad}&timezone=America/Chicago");

        Assert.Equal("Asia/Kolkata", response.Location.TimeZone);
    }

    [Fact]
    public async Task Today_ByCityStateCountry_UsesThePlacesTimeZone()
    {
        var response = await GetPanchangAsync("/api/v1/panchang/today?place=Austin,%20Texas,%20USA");

        Assert.Equal("America/Chicago", response.Location.TimeZone);
        Assert.Equal("Place", response.Location.Source);
        Assert.Equal("Austin, Texas, United States", response.Location.Place?.DisplayName);
    }

    [Theory]
    [InlineData("São Paulo, Brazil", "America/Sao_Paulo")]
    [InlineData("St. Louis, Missouri, USA", "America/Chicago")]
    public async Task Today_AcceptsPlaceNamesWithAccentsAndPunctuation(string place, string timeZone)
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/today?place={Uri.EscapeDataString(place)}");

        Assert.Equal(timeZone, response.Location.TimeZone);
    }

    [Fact]
    public async Task Today_ByCityStateWithBrowserLocation_InfersCountryFromBrowser()
    {
        // Browser in Karachi: "Hyderabad, Sindh" rather than the larger Hyderabad in India.
        var response = await GetPanchangAsync("/api/v1/panchang/today?place=Hyderabad&latitude=24.86&longitude=67.01");

        Assert.Equal("Asia/Karachi", response.Location.TimeZone);
        Assert.Equal("PK", response.Location.Place?.CountryCode);
    }

    [Fact]
    public async Task Today_ByBrowserLocationOnly_IsLabelledWithNearestCity()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/today?{Hyderabad}");

        Assert.Equal("Coordinates", response.Location.Source);
        Assert.Equal("Telangana", response.Location.Place?.State);
    }

    [Fact]
    public async Task Today_IncludesGlossaryClassificationsAndLanguageHeaders()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/v1/panchang/today?{Hyderabad}", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<PanchangResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal("en", body.Language);
        Assert.Equal("en", response.Content.Headers.ContentLanguage.Single());
        Assert.Contains("Accept-Language", response.Headers.Vary);

        var rahu = body.Glossary["periods"]["RahuKalam"];
        Assert.Equal("Avoid important new beginnings", rahu.Message);
        Assert.False(string.IsNullOrWhiteSpace(rahu.Meaning));
        Assert.Equal("🟢", body.Glossary["classifications"]["Favorable"].Icon);

        var day = Assert.Single(body.Days);
        Assert.Equal("Avoid", day.InauspiciousPeriods.First(period => period.Kind == "RahuKalam").Classification);
        Assert.All(day.Tithis, tithi => Assert.False(string.IsNullOrWhiteSpace(tithi.Key)));
        Assert.Equal(day.Weekday, day.WeekdayName);
    }

    [Fact]
    public async Task Tradition_Tamil_ShowsOnlyItsTimingsAndOmitsChoghadiya()
    {
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync(new Uri($"/api/v1/panchang/2026-09-29?{Hyderabad}&tradition=tamil", UriKind.Relative));
        var body = JsonSerializer.Deserialize<PanchangResponse>(json, JsonOptions)!;

        Assert.Equal("tamil", body.Tradition.Key);
        Assert.DoesNotContain("\"choghadiya\":", json, StringComparison.Ordinal);
        var day = Assert.Single(body.Days);
        Assert.Equal(["AbhijitMuhurta", "AmritKalam", "BrahmaMuhurta"], day.AuspiciousPeriods.Select(period => period.Kind).Order());
        Assert.Equal("Brahma Muhurtham", day.AuspiciousPeriods.Single(period => period.Kind == "BrahmaMuhurta").Name);
        Assert.NotEmpty(day.Guidance.FavorableWindows);
        Assert.NotEmpty(day.Guidance.SpiritualWindows);
        Assert.Equal(["GulikaKalam", "RahuKalam", "Yamagandam"], day.InauspiciousPeriods.Select(period => period.Kind).Order());
        Assert.Equal("Kuligai", day.InauspiciousPeriods.Single(period => period.Kind == "GulikaKalam").Name);
    }

    [Fact]
    public async Task Tradition_Gujarati_IncludesChoghadiya()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/2026-10-01?{Austin}&tradition=gujarati");

        var day = Assert.Single(response.Days);
        Assert.NotNull(day.Choghadiya);
        Assert.Equal(16, day.Choghadiya.Count);
        Assert.Equal("Choghadiya", response.Tradition.Display[0]);
    }

    [Fact]
    public async Task DisplayStrings_FollowTheLocationsFormat()
    {
        var austin = await GetPanchangAsync($"/api/v1/panchang/2026-09-28?{Austin}");
        var hyderabad = await GetPanchangAsync($"/api/v1/panchang/2026-09-28?{Hyderabad}");

        Assert.Equal("09/28/2026", austin.Days[0].DateDisplay);
        Assert.Matches(@"^09/28/2026 \d{2}:\d{2}:\d{2} CDT$", austin.Days[0].SunriseDisplay);
        Assert.Equal("MM/dd/yyyy", austin.DisplayFormat.DatePattern);

        Assert.Equal("28/09/2026", hyderabad.Days[0].DateDisplay);
        Assert.Matches(@"^28/09/2026 \d{2}:\d{2}:\d{2} IST$", hyderabad.Days[0].SunriseDisplay);
        Assert.All(hyderabad.Days[0].InauspiciousPeriods, period => Assert.EndsWith(" IST", period.StartDisplay, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Traditions_ListsEveryTradition()
    {
        using var client = factory.CreateClient();

        var traditions = await client.GetFromJsonAsync<TraditionsResponse>(new Uri("/api/v1/traditions", UriKind.Relative), JsonOptions);
        var panchangTraditions = await client.GetFromJsonAsync<PanchangTraditionsResponse>(
            new Uri("/api/v1/panchang/traditions", UriKind.Relative), JsonOptions);

        Assert.NotNull(traditions);
        Assert.Equal(8, traditions.Traditions.Count);
        Assert.Contains(traditions.Traditions, tradition => tradition.Key == "telugu" && tradition.Description == "Andhra Pradesh and Telangana.");

        Assert.NotNull(panchangTraditions);
        Assert.Equal(8, panchangTraditions.Traditions.Count);
        Assert.Contains(panchangTraditions.Traditions, tradition => tradition.Key == "telugu" && tradition.Display.Contains("Varjyam"));
    }

    [Fact]
    public async Task EveryDay_IncludesDailyGuidance()
    {
        var response = await GetPanchangAsync($"/api/v1/panchang/2026-09-29?{Hyderabad}&tradition=telugu");

        var guidance = Assert.Single(response.Days).Guidance;
        Assert.Contains(guidance.Rating, new[] { "HighlyFavorable", "Favorable", "Mixed", "Caution", "Avoid" });
        Assert.StartsWith(guidance.Icon, guidance.Headline, StringComparison.Ordinal);
        Assert.Equal(5, guidance.Factors.Count);
        Assert.Contains(guidance.AvoidWindows, window => window.Name == "Durmuhurtham");
        Assert.Contains("Durmuhurtham", guidance.Summary, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(guidance.Methodology));
        Assert.NotEmpty(guidance.BestWindows);
        Assert.NotNull(guidance.MixedWindows);
        Assert.All(
            Assert.Single(response.Days).AuspiciousPeriods.Where(period => period.Overlaps is not null),
            period => Assert.All(period.Overlaps!, overlap => Assert.Contains(overlap.Name, period.OverlapMessage!, StringComparison.Ordinal)));
        Assert.All(guidance.MixedWindows, window =>
        {
            Assert.Equal("Mixed", window.Rating);
            Assert.NotEmpty(window.FavorablePeriods);
            Assert.NotEmpty(window.UnfavorablePeriods);
        });
    }

    [Fact]
    public async Task UnsupportedLanguage_FallsBackToDefault()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/v1/panchang/today?{Hyderabad}", UriKind.Relative));
        request.Headers.AcceptLanguage.ParseAdd("fr-FR,fr;q=0.9");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("en", response.Content.Headers.ContentLanguage.Single());
    }

    [Fact]
    public async Task Glossary_ReturnsEverySection()
    {
        using var client = factory.CreateClient();

        var glossary = await client.GetFromJsonAsync<GlossaryResponse>(new Uri("/api/v1/panchang/glossary?language=en", UriKind.Relative), JsonOptions);

        Assert.NotNull(glossary);
        Assert.Equal(["classifications", "elements", "guidanceRatings", "periods", "terms"], glossary.Sections.Keys.Order());
        Assert.Equal(15, glossary.Sections["periods"].Count);
        Assert.Equal("🔴", glossary.Sections["classifications"]["Avoid"].Icon);
    }

    [Fact]
    public async Task ResolveLocation_ReturnsPlaceWithoutCalculating()
    {
        using var client = factory.CreateClient();

        var location = await client.GetFromJsonAsync<LocationResponse>(
            new Uri("/api/v1/locations/resolve?place=San%20Jose,%20California", UriKind.Relative), JsonOptions);

        Assert.NotNull(location);
        Assert.Equal("America/Los_Angeles", location.TimeZone);
        Assert.Equal("US", location.Place?.CountryCode);
    }

    [Theory]
    [InlineData("/api/v1/panchang/today")]
    [InlineData("/api/v1/panchang/today?latitude=17")]
    [InlineData("/api/v1/panchang/today?place=Qwertyzxville,%20Nowhere")]
    [InlineData("/api/v1/panchang/today?place=%3Cscript%3E")]
    [InlineData("/api/v1/locations/resolve")]
    [InlineData("/api/v1/panchang/today?latitude=17&longitude=78&language=not_a_tag")]
    [InlineData("/api/v1/panchang/today?latitude=100&longitude=78")]
    [InlineData("/api/v1/panchang/today?longitude=78")]
    [InlineData("/api/v1/panchang/today?latitude=17&longitude=78&timezone=Not/A_Zone")]
    [InlineData("/api/v1/panchang/today?latitude=17&longitude=78&tradition=unknown")]
    [InlineData("/api/v1/panchang/range?start=2026-01-01&days=0&latitude=17&longitude=78")]
    [InlineData("/api/v1/panchang/range?days=5&latitude=17&longitude=78")]
    [InlineData("/api/v1/panchang/month/2026/13?latitude=17&longitude=78")]
    [InlineData("/api/v1/panchang/year/1800?latitude=17&longitude=78")]
    [InlineData("/api/v1/panchang/2026-02-30?latitude=17&longitude=78")]
    public async Task InvalidInput_ReturnsValidationProblemDetails(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PolarDay_ReturnsUnprocessableEntity()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/panchang/2026-06-21?latitude=69.65&longitude=18.96", UriKind.Relative));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.False(problem.TryGetProperty("exception", out _));
    }

    [Fact]
    public async Task CorrelationId_IsEchoedAndSecurityHeadersAreSet()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/v1/panchang/today?{Hyderabad}", UriKind.Relative));
        request.Headers.Add("X-Correlation-ID", "test-correlation-123");

        using var response = await client.SendAsync(request);

        Assert.Equal("test-correlation-123", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_ReturnOk(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<PanchangResponse> GetPanchangAsync(string url)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<PanchangResponse>(JsonOptions)
            ?? throw new InvalidOperationException("Empty response body.");
    }
}
