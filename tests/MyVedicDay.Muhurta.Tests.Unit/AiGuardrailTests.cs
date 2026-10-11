using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Ai;
using MyVedicDay.Location;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Muhurta.Business.Activities;
using MyVedicDay.Muhurta.Business.Explanation;
using NodaTime;

namespace MyVedicDay.Muhurta.Tests.Unit;

/// <summary>
/// The AI may explain and classify, never decide (ADR-0012): its text is used only when it passes every guardrail,
/// and the deterministic template is used otherwise.
/// </summary>
public sealed class AiGuardrailTests
{
    private static readonly MuhurtaQuery Hyderabad = new(new LocationQuery(17.385, 78.4867));
    private static readonly GoodTimesRequest Business = new(new DateOnly(2026, 10, 1), "business");
    private static readonly Instant Morning = Instant.FromUtc(2026, 10, 1, 3, 0);

    [Fact]
    public async Task WithoutAi_TheTemplateExplainsTheBestTimeAndTheConflicts()
    {
        using var host = new MuhurtaTestHost(Morning);

        var result = await host.Facade.GetExplanationAsync(Hyderabad, Business, CancellationToken.None);
        var day = await host.Facade.GetGoodTimesAsync(Hyderabad, Business, CancellationToken.None);

        Assert.Equal(GoodTimeExplainer.TemplateSource, result.Explanation.Source);
        Assert.Null(result.Explanation.FallbackReason);
        if (day.GoodTimes.Count > 0)
        {
            var best = day.Segments.Single(segment => segment.Id == day.GoodTimes[0]);
            Assert.Contains(best.TimeRangeDisplay, result.Explanation.Summary, StringComparison.Ordinal);
            Assert.All(day.GoodTimes, id => Assert.Contains(result.Explanation.Notes, note => note.SegmentId == id));
        }
        else
        {
            Assert.Contains(day.NoGoodTimeMessage!, result.Explanation.Summary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WithoutAi_TheTemplateExplainsAFavorablePeriodThatAConflictSpoils()
    {
        // Austin, General, 2026-10-20: a favorable period overlaps an unfavorable one and is not recommended.
        var austin = new MuhurtaQuery(new LocationQuery(30.27, -97.74));
        var request = new GoodTimesRequest(new DateOnly(2026, 10, 20), "general");
        using var host = new MuhurtaTestHost(Morning);

        var result = await host.Facade.GetExplanationAsync(austin, request, CancellationToken.None);
        var day = await host.Facade.GetGoodTimesAsync(austin, request, CancellationToken.None);

        var conflicted = day.Segments.First(segment => segment.Conflicts.Any(conflict => conflict.WithFavorable));
        // The favorable period itself (e.g. Abhijit Muhurta), not a supportive limb such as "Favorable Tithi".
        var favorable = conflicted.Reasons.First(reason =>
            reason.Type == "positive" && reason.Subject is not null
            && !reason.Code.StartsWith("FAVORABLE_", StringComparison.Ordinal) && !reason.Code.StartsWith("NO_", StringComparison.Ordinal));
        Assert.Contains(
            $"{conflicted.TimeRangeDisplay} is {conflicted.Status.Name}: ",
            result.Explanation.Summary,
            StringComparison.Ordinal);
        Assert.Contains($"{favorable.Label} overlaps ", result.Explanation.Summary, StringComparison.Ordinal);
        Assert.Contains(result.Explanation.Notes, note => note.SegmentId == conflicted.Id && note.Text.Contains('⚠'));
    }

    [Fact]
    public async Task ValidAiText_IsUsed_AndCached()
    {
        var ai = new ScriptedGenerator(request => GoodTimeNarrative(request));
        using var host = new MuhurtaTestHost(Morning, services => services.AddSingleton<IStructuredTextGenerator>(ai));

        var first = await host.Facade.GetExplanationAsync(Hyderabad, Business, CancellationToken.None);
        var second = await host.Facade.GetExplanationAsync(Hyderabad, Business, CancellationToken.None);

        Assert.Equal(GoodTimeExplainer.AiSource, first.Explanation.Source);
        Assert.Equal(("Fake", "fake-model"), (first.Explanation.Provider, first.Explanation.Model));
        Assert.StartsWith("Your best window", first.Explanation.Summary, StringComparison.Ordinal);
        Assert.Equal(first.Explanation, second.Explanation);
        Assert.Equal(1, ai.Calls);
    }

    [Theory]
    [InlineData("Start at 03:33 for the best results.", "UNKNOWN_TIME")]
    [InlineData(null, "UNKNOWN_SEGMENT")]
    public async Task InventedTimesOrSegments_FallBackToTheTemplate(string? summary, string reason)
    {
        var ai = new ScriptedGenerator(_ => new AiExplanation(
            summary ?? "A calm day.",
            summary is null ? [new AiExplanationNote("s999", "A note.")] : []));
        using var host = new MuhurtaTestHost(Morning, services => services.AddSingleton<IStructuredTextGenerator>(ai));

        var result = await host.Facade.GetExplanationAsync(Hyderabad, Business, CancellationToken.None);

        Assert.Equal(GoodTimeExplainer.TemplateSource, result.Explanation.Source);
        Assert.Equal(reason, result.Explanation.FallbackReason);
    }

    [Fact]
    public async Task CallingAConflictingSegmentGood_FallsBackToTheTemplate()
    {
        var ai = new ScriptedGenerator(request =>
        {
            var conflicted = Segments(request).First(segment => !segment.GetProperty("isGoodTime").GetBoolean());
            return new AiExplanation("A mixed day.", [new AiExplanationNote(conflicted.GetProperty("segmentId").GetString()!, "This is the best time to start.")]);
        });
        using var host = new MuhurtaTestHost(Morning, services => services.AddSingleton<IStructuredTextGenerator>(ai));

        var result = await host.Facade.GetExplanationAsync(Hyderabad, Business, CancellationToken.None);

        Assert.Equal("PROMOTES_NON_GOOD_SEGMENT", result.Explanation.FallbackReason);
    }

    [Fact]
    public async Task ProviderFailure_FallsBackToTheTemplate()
    {
        var ai = new ScriptedGenerator(_ => null, AiOutcome.TimedOut);
        using var host = new MuhurtaTestHost(Morning, services => services.AddSingleton<IStructuredTextGenerator>(ai));

        var result = await host.Facade.GetExplanationAsync(Hyderabad, Business, CancellationToken.None);

        Assert.Equal(GoodTimeExplainer.TemplateSource, result.Explanation.Source);
        Assert.Equal("TimedOut", result.Explanation.FallbackReason);
    }

    [Fact]
    public async Task Explanation_NeverChangesTheEvaluation()
    {
        var ai = new ScriptedGenerator(request => GoodTimeNarrative(request));
        using var withAi = new MuhurtaTestHost(Morning, services => services.AddSingleton<IStructuredTextGenerator>(ai));
        using var withoutAi = new MuhurtaTestHost(Morning);

        await withAi.Facade.GetExplanationAsync(Hyderabad, Business, CancellationToken.None);
        var a = await withAi.Facade.GetGoodTimesAsync(Hyderabad, Business, CancellationToken.None);
        var b = await withoutAi.Facade.GetGoodTimesAsync(Hyderabad, Business, CancellationToken.None);

        Assert.Equal(a.GoodTimes, b.GoodTimes);
        Assert.Equal(a.Segments.Select(segment => (segment.Start, segment.Status.Status)), b.Segments.Select(segment => (segment.Start, segment.Status.Status)));
    }

    [Theory]
    [InlineData("puja", 0.9, "puja")]
    [InlineData("skydiving", 0.95, "business")] // not a listed key: keywords decide ("shop")
    [InlineData("puja", 0.3, "business")]        // not confident: keywords decide
    public async Task OtherText_UsesTheAiOnlyForAListedConfidentKey(string aiKey, double confidence, string expected)
    {
        var ai = new ScriptedGenerator(_ => new ActivityClassification(aiKey, confidence));
        using var host = new MuhurtaTestHost(Morning, services => services.AddSingleton<IStructuredTextGenerator>(ai));

        var result = await host.Facade.GetGoodTimesAsync(
            Hyderabad, new GoodTimesRequest(Activity: "other", OtherText: "Blessing my new shop"), CancellationToken.None);

        Assert.Equal(expected, result.Activity.Key);
        Assert.Equal(1, ai.Calls);
        Assert.Contains("Blessing my new shop", ai.LastInput, StringComparison.Ordinal);
    }

    /// <summary>A valid narrative built from the evaluation the explainer sent: real segment ids and times only.</summary>
    private static AiExplanation? GoodTimeNarrative(AiRequest request)
    {
        var good = Segments(request).Where(segment => segment.GetProperty("isGoodTime").GetBoolean()).ToArray();
        return good.Length == 0
            ? new AiExplanation("Your best window today: none qualifies; please choose another date.", [])
            : new AiExplanation(
                $"Your best window today is {good[0].GetProperty("time").GetString()}.",
                [new AiExplanationNote(good[0].GetProperty("segmentId").GetString()!, "Supportive Panchang factors and no conflicting period.")]);
    }

    private static JsonElement[] Segments(AiRequest request) =>
        JsonDocument.Parse(request.Input).RootElement.GetProperty("segments").EnumerateArray().ToArray();

    /// <summary>An <see cref="IStructuredTextGenerator"/> that answers with scripted values.</summary>
    private sealed class ScriptedGenerator(Func<AiRequest, object?> answer, AiOutcome outcome = AiOutcome.Succeeded) : IStructuredTextGenerator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string LastInput { get; private set; } = string.Empty;

        public bool IsEnabled => true;

        public string Fingerprint => "Fake/fake-model";

        public Task<AiResult<T>> GenerateAsync<T>(AiRequest request, CancellationToken cancellationToken)
            where T : class
        {
            Interlocked.Increment(ref _calls);
            LastInput = request.Input;
            var value = outcome == AiOutcome.Succeeded ? answer(request) as T : null;
            return Task.FromResult(new AiResult<T>(value, value is null && outcome == AiOutcome.Succeeded ? AiOutcome.InvalidOutput : outcome, "Fake", "fake-model"));
        }
    }
}
