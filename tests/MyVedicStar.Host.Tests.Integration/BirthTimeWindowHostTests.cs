using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>
/// KUN-06 boundary behaviour in the consolidated host: an approximate birth window that does not run forward must be
/// answered with 400 and the specific midnight message, not a clamped 200 and not a 500.
/// </summary>
/// <remarks>
/// Lives here rather than in the Day API integration project because VedicProfiles is composed into the Star host
/// (MyVedicStarApiModule adds the controller application part and the profile module). Asserting it against the Day
/// host produced a 404 - the route genuinely is not there - so the test belonged to the wrong host.
/// The validator throws before persistence is reached, so no database is required.
/// </remarks>
public sealed partial class SharedHostTests
{
    private static object ProfileBody(string mode, string? time, string? from, string? to) => new
    {
        name = "Asha",
        dateOfBirth = "1990-04-12",
        birthTimeMode = mode,
        birthTime = time,
        birthTimeFrom = from,
        birthTimeTo = to,
        timeAccuracyNote = (string?)null,
        locationName = "Chennai",
        latitude = 13.0827m,
        longitude = 80.2707m,
        timezoneId = "Asia/Kolkata",
        gender = "Female",
        occupation = "Engineer",
        relationship = "Self",
        customRelationship = (string?)null
    };

    [Theory]
    [InlineData("23:30:00", "00:30:00")]
    [InlineData("23:59:00", "00:00:00")]
    [InlineData("12:00:00", "12:00:00")]
    [InlineData("12:00:00", "11:59:00")]
    public async Task Birth_window_that_does_not_run_forward_returns_400_with_the_midnight_message(
        string from, string to)
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());

        using var response = await client.PostAsJsonAsync("/api/v1/vedic-profiles",
            ProfileBody("Approximate", null, from, to));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detail = body.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
        Assert.Contains("must not cross midnight", detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_long_window_within_the_day_is_not_rejected_as_a_midnight_crossing()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());

        using var response = await client.PostAsJsonAsync("/api/v1/vedic-profiles",
            ProfileBody("Approximate", null, "00:00:00", "23:59:00"));

        // 00:00 -> 23:59 runs forward with no wrap, so the midnight rule must not fire. It may fail later for an
        // unrelated persistence reason; what must never happen is the midnight rejection.
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var detail = body.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
            Assert.DoesNotContain("must not cross midnight", detail ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}

