using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>
/// Boundary tests for Vedic Profile validation: each rule is asserted over HTTP, on both the create and the replace
/// path, by its specific public message.
/// </summary>
/// <remarks>
/// <para>
/// The hole this closes (#93): the rules were unit-tested at the facade but never boundary-tested, because account
/// resolution runs first and no Star-host fixture had a registered account. A regression in exception-to-400 mapping, or
/// a validator skipped on one of the two write paths, would have shipped green.
/// </para>
/// <para>
/// Every case asserts four things together, because any one alone can pass for the wrong reason: the status is 400,
/// the problem body names the rule, the failure is not a 401/503 from account resolution (which is what a missing seed
/// looks like), and nothing was written or changed.
/// </para>
/// </remarks>
public sealed partial class SharedHostTests
{
    private const string ValidTimezone = "Asia/Kolkata";

    /// <summary>Rule name, payload mutator, and the message the caller must see.</summary>
    public static TheoryData<string, string, Func<Dictionary<string, object?>, Dictionary<string, object?>>> ProfileRules() => new()
    {
        { "blank-name", VedicProfileErrors.InvalidDetails, body => With(body, "name", " ") },
        { "name-too-long", VedicProfileErrors.InvalidDetails, body => With(body, "name", new string('a', 257)) },
        { "name-with-control-characters", VedicProfileErrors.InvalidDetails, body => With(body, "name", "Asha\u0007") },
        { "blank-location", VedicProfileErrors.InvalidDetails, body => With(body, "locationName", "") },
        { "blank-gender", VedicProfileErrors.InvalidDetails, body => With(body, "gender", " ") },
        { "occupation-too-long", VedicProfileErrors.InvalidDetails, body => With(body, "occupation", new string('a', 257)) },
        { "accuracy-note-too-long", VedicProfileErrors.InvalidDetails, body => With(body, "timeAccuracyNote", new string('a', 2001)) },
        { "future-date-of-birth", VedicProfileErrors.InvalidDate, body => With(body, "dateOfBirth", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd")) },
        { "known-mode-without-a-time", VedicProfileErrors.InvalidTimes, body => With(body, "birthTime", null) },
        { "known-mode-with-a-window", VedicProfileErrors.InvalidTimes, body => With(body, "birthTimeTo", "13:00:00") },
        { "approximate-window-across-midnight", VedicProfileErrors.BirthWindowCrossesMidnight, body => Approximate(body, "23:30:00", "00:30:00") },
        { "approximate-window-without-ends", VedicProfileErrors.InvalidTimes, body => Approximate(body, null, null) },
        { "unknown-mode-carrying-times", VedicProfileErrors.InvalidTimes, body => With(With(body, "birthTimeMode", "Unknown"), "birthTime", "12:34:56") },
        { "unsupported-birth-time-mode", VedicProfileErrors.InvalidTimes, body => With(body, "birthTimeMode", "Sometimes") },
        { "unparseable-time", VedicProfileErrors.InvalidTimes, body => With(body, "birthTime", "25:99:99") },
        { "unsupported-relationship", VedicProfileErrors.InvalidRelationship, body => With(body, "relationship", "Cousin") },
        { "other-without-custom-text", VedicProfileErrors.InvalidRelationship, body => With(body, "relationship", "Other") },
        { "custom-text-without-other", VedicProfileErrors.InvalidRelationship, body => With(body, "customRelationship", "Family friend") },
        { "latitude-out-of-range", VedicProfileErrors.InvalidLocation, body => With(body, "latitude", 90.000001m) },
        { "longitude-out-of-range", VedicProfileErrors.InvalidLocation, body => With(body, "longitude", -180.000001m) },
        { "windows-timezone-id", VedicProfileErrors.InvalidLocation, body => With(body, "timezoneId", "Pacific Standard Time") },
        { "unknown-timezone-id", VedicProfileErrors.InvalidLocation, body => With(body, "timezoneId", "Not/AZone") },
    };

    [Theory]
    [MemberData(nameof(ProfileRules))]
    public async Task Create_rejects_each_validation_rule_with_its_own_message(
        string rule, string expectedMessage, Func<Dictionary<string, object?>, Dictionary<string, object?>> mutate)
    {
        var seed = new SeededProfilesPersistence(Guid.NewGuid());
        await using var host = SeededHost(seed);
        using var client = AuthenticatedClient(host);

        var response = await client.PostAsJsonAsync("/api/v1/vedic-profiles", mutate(ValidProfile()));

        await AssertRuleRejection(response, rule, expectedMessage);
        Assert.Equal(0, seed.StoredCount);
    }

    [Theory]
    [MemberData(nameof(ProfileRules))]
    public async Task Replace_rejects_each_validation_rule_with_its_own_message(
        string rule, string expectedMessage, Func<Dictionary<string, object?>, Dictionary<string, object?>> mutate)
    {
        var seed = new SeededProfilesPersistence(Guid.NewGuid());
        var existing = seed.Seed(AcceptedProfile());
        await using var host = SeededHost(seed);
        using var client = AuthenticatedClient(host);

        var body = mutate(ValidProfile());
        body["revision"] = 1;
        var response = await client.PutAsJsonAsync($"/api/v1/vedic-profiles/{existing.Id}", body);

        await AssertRuleRejection(response, rule, expectedMessage);

        // The row must be untouched: a rejected replace that still bumped the revision would hand the client a stale
        // revision for its next attempt and make the validation error lie about what happened.
        var unchanged = await client.GetFromJsonAsync<JsonElement>($"/api/v1/vedic-profiles/{existing.Id}");
        Assert.Equal(1, unchanged.GetProperty("revision").GetInt64());
        Assert.Equal(existing.Details.Name, unchanged.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_seeded_account_reaches_the_validator_while_an_unseeded_one_does_not()
    {
        // The control for the whole file. Without the seed the same payload fails at account resolution, which is the
        // 401/503 the issue describes; with the seed the validator answers. If this ever passes trivially - both requests
        // returning the same thing - the fixture has stopped proving anything and the rule tests above are vacuous.
        var payload = ValidProfile();
        var invalid = With(ValidProfile(), "name", " ");

        await using var unseeded = new CombinedFactory();
        using var unseededClient = AuthenticatedClient(unseeded);
        var unseededResponse = await unseededClient.PostAsJsonAsync("/api/v1/vedic-profiles", invalid);
        Assert.NotEqual(HttpStatusCode.BadRequest, unseededResponse.StatusCode);

        var seed = new SeededProfilesPersistence(Guid.NewGuid());
        await using var host = SeededHost(seed);
        using var client = AuthenticatedClient(host);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/vedic-profiles", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/vedic-profiles", payload)).StatusCode);
        Assert.Equal(1, seed.StoredCount);
    }

    [Fact]
    public async Task A_validation_rejection_is_a_real_problem_details_body_and_never_names_internal_detail()
    {
        var seed = new SeededProfilesPersistence(Guid.NewGuid());
        await using var host = SeededHost(seed);
        using var client = AuthenticatedClient(host);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "profile-validation");

        var response = await client.PostAsJsonAsync("/api/v1/vedic-profiles", With(ValidProfile(), "latitude", 91m));

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(VedicProfileErrors.InvalidLocation, problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal("profile-validation", problem.RootElement.GetProperty("correlationId").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    /// <summary>A host whose Vedic Profiles ports are backed by the seeded account and in-memory storage.</summary>
    private static CombinedFactory SeededHost(SeededProfilesPersistence seed) => new(profiles: seed);

    /// <summary>A client for the seeded host, signed by the same key the factory published to it.</summary>
    private static HttpClient AuthenticatedClient(CombinedFactory host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.Token());
        return client;
    }

    /// <summary>A payload every rule accepts: known time, valid coordinates, an IANA timezone.</summary>
    private static Dictionary<string, object?> ValidProfile() => new()
    {
        ["name"] = "Asha",
        ["dateOfBirth"] = "1990-04-12",
        ["birthTimeMode"] = "Known",
        ["birthTime"] = "12:34:56",
        ["birthTimeFrom"] = "12:34:56",
        ["birthTimeTo"] = "12:34:56",
        ["timeAccuracyNote"] = null,
        ["locationName"] = "Chennai",
        ["latitude"] = 13.0827m,
        ["longitude"] = 80.2707m,
        ["timezoneId"] = ValidTimezone,
        ["gender"] = "Female",
        ["occupation"] = "Engineer",
        ["relationship"] = "Self",
        ["customRelationship"] = null,
    };

    /// <summary>The same payload as a domain value, for seeding an existing row.</summary>
    private static VedicProfileDetails AcceptedProfile() => new("Asha", new DateOnly(1990, 4, 12), BirthTimeMode.Known,
        new TimeOnly(12, 34, 56), new TimeOnly(12, 34, 56), new TimeOnly(12, 34, 56), null,
        "Chennai", 13.0827m, 80.2707m, ValidTimezone, "Female", "Engineer", Relationship.Self, null);

    private static Dictionary<string, object?> With(Dictionary<string, object?> body, string field, object? value) =>
        new(body) { [field] = value };

    private static Dictionary<string, object?> Approximate(Dictionary<string, object?> body, string? from, string? to) =>
        new(body) { ["birthTimeMode"] = "Approximate", ["birthTime"] = null, ["birthTimeFrom"] = from, ["birthTimeTo"] = to };

    private static async Task AssertRuleRejection(HttpResponseMessage response, string rule, string expectedMessage)
    {
        var payload = await response.Content.ReadAsStringAsync();

        // 401/403/503 would mean the request never reached the validator, so the rule assertion would be meaningless even
        // if the body happened to look right.
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
            $"Rule '{rule}' must be a 400 from the validator, got {(int)response.StatusCode}. {payload}");

        using var problem = JsonDocument.Parse(payload);
        Assert.Equal(expectedMessage, problem.RootElement.GetProperty("detail").GetString());
    }
}

