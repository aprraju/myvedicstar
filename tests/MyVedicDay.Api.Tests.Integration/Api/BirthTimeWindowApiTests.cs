using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyVedicStar.API.Contracts;
using MyVedicDay.Api.Tests.Integration.Infrastructure;

namespace MyVedicDay.Api.Tests.Integration.Api;

/// <summary>
/// KUN-06 boundary behaviour: a birth-time window that does not run forward must be rejected as a 400 with the
/// specific midnight message, not a 200 with a clamped window and not a 500 from the domain.
/// </summary>
/// <remarks>
/// These assert the HTTP contract only. The rule itself is enforced in the business validator, which throws before
/// any persistence is reached - so no database is needed and the test stays a true boundary test.
/// </remarks>
public sealed class BirthTimeWindowApiTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    private static VedicProfileRequest Profile(string mode, string? time, string? from, string? to) =>
        new("Asha", new DateOnly(1990, 4, 12), mode, time, from, to, null, "Chennai", 13.0827m, 80.2707m,
            "Asia/Kolkata", "Female", "Engineer", "Self", null);

    [Theory]
    [InlineData("23:30:00", "00:30:00")]  // wraps midnight - the reported bug
    [InlineData("23:59:00", "00:00:00")]  // wraps at the boundary
    [InlineData("12:00:00", "12:00:00")]  // equal bounds: zero-length is not "approximate"
    [InlineData("12:00:00", "11:59:00")]  // backwards within the day
    public async Task A_window_that_does_not_run_forward_returns_400_with_the_midnight_message(
        string from, string to)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/vedic-profiles",
            Profile("Approximate", null, from, to));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        // The specific message, so the client can tell the user which field to fix rather than "invalid input".
        Assert.Contains("must not cross midnight", problem.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_long_window_within_the_day_is_not_rejected_as_a_midnight_crossing()
    {
        using var client = factory.CreateClient();

        // 00:00 -> 23:59 runs forward with no wrap, so the midnight rule must not fire. It may still fail later for
        // an unrelated reason (persistence), but it must not produce the midnight 400 - that is the assertion.
        using var response = await client.PostAsJsonAsync("/api/v1/vedic-profiles",
            Profile("Approximate", null, "00:00:00", "23:59:00"));

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.DoesNotContain("must not cross midnight", problem?.Detail ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}

