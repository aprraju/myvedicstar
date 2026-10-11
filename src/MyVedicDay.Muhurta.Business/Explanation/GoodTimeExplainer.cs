using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MyVedicDay.Ai;
using MyVedicDay.Caching;
using MyVedicDay.Localization;
using MyVedicDay.Muhurta.Data;
using NodaTime;

namespace MyVedicDay.Muhurta.Business.Explanation;

/// <summary>
/// Writes the "Why this time?" explanation of an evaluation. The deterministic template is always built; the AI
/// narrative (ADR-0012) replaces it only when AI is enabled and its output passes every guardrail:
/// <list type="bullet">
/// <item><description>notes refer only to segments of the evaluation;</description></item>
/// <item><description>every time mentioned appears in the evaluation;</description></item>
/// <item><description>nothing that is not a Good Time is called good, recommended or best;</description></item>
/// <item><description>when there is no Good Time, no time is suggested.</description></item>
/// </list>
/// AI results are cached per evaluation, language, provider/model and prompt version, so a repeated view never calls
/// the model again.
/// </summary>
internal sealed partial class GoodTimeExplainer(
    IStructuredTextGenerator ai,
    IMuhurtaPromptRepository prompts,
    IDistributedObjectCache cache,
    IClock clock,
    IOptions<MuhurtaOptions> options)
{
    public const string TemplateSource = "template";
    public const string AiSource = "ai";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] PromotionWords = ["good time", "recommended", "best time", "ideal", "auspicious time to"];

    public async Task<GoodTimeExplanation> ExplainAsync(GoodTimesResult result, LocalizedContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(content);

        var prompt = await prompts.GetPromptsAsync(cancellationToken);
        var template = Template(result, content, prompt.Version);
        if (!ai.IsEnabled)
        {
            return template;
        }

        var input = BuildInput(result);
        var key = "muhurta-explanation:" + Hash($"{input}|{result.Language}|{ai.Fingerprint}|{prompt.Version}");
        if (await cache.GetAsync<GoodTimeExplanation>(key, cancellationToken) is { } cached)
        {
            return cached;
        }

        var response = await ai.GenerateAsync<AiExplanation>(new AiRequest("muhurta.explain", prompt.Explanation, input, prompt.Version), cancellationToken);
        if (response.Outcome != AiOutcome.Succeeded || response.Value is null)
        {
            return template with { FallbackReason = response.Outcome.ToString() };
        }

        var violation = Validate(response.Value, result);
        if (violation is not null)
        {
            return template with { FallbackReason = violation };
        }

        var explanation = new GoodTimeExplanation(
            AiSource,
            response.Value.Summary.Trim(),
            response.Value.Notes.Select(note => new GoodTimeNote(note.SegmentId, note.Text.Trim())).ToArray(),
            response.Provider,
            response.Model,
            prompt.Version,
            null);

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(result.Location.TimeZoneId) ?? DateTimeZone.Utc;
        var expiration = LocalDayCacheExpiration.Calculate(
            LocalDate.FromDateOnly(result.Date), zone, clock.GetCurrentInstant(), options.Value.PastDateCacheLifetime, options.Value.MaxCacheLifetime);
        await cache.SetAsync(key, explanation, expiration, CancellationToken.None);
        return explanation;
    }

    /// <summary>The deterministic explanation, from the localized reasons and conflicts.</summary>
    public static GoodTimeExplanation Template(GoodTimesResult result, LocalizedContent content, string promptVersion)
    {
        string Text(string id) => content.GetEntry(MuhurtaContent.Text, id).Message ?? string.Empty;
        var separator = Text("noteSeparator");
        var segments = result.Segments.ToDictionary(segment => segment.Id);

        var sentences = new List<string>();
        if (result.GoodTimes.Count > 0)
        {
            var best = segments[result.GoodTimes[0]];
            sentences.Add(Text("explainBest")
                .Replace("{activity}", result.Activity.Name, StringComparison.Ordinal)
                .Replace("{range}", best.TimeRangeDisplay, StringComparison.Ordinal)
                .Replace("{status}", best.Status.Name, StringComparison.Ordinal));

            var why = best.Reasons.Where(reason => reason.Type == "positive" && !reason.Code.StartsWith("NO_", StringComparison.Ordinal)).Select(reason => reason.Label).Take(4).ToArray();
            if (why.Length > 0)
            {
                sentences.Add(Text("explainWhy").Replace("{reasons}", string.Join(", ", why), StringComparison.Ordinal));
            }

            if (result.GoodTimes.Count > 1)
            {
                sentences.Add(Text("explainOthers").Replace("{count}", (result.GoodTimes.Count - 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }
        }
        else if (result.NoGoodTimeMessage is { } message)
        {
            sentences.Add(message);
        }

        // On a day ruled out for the activity the overlap is not the reason, so it is not explained.
        var conflicted = result.NoGoodTimeReason == Calculation.Models.NoGoodTimeReasons.DayExcludedForActivity
            ? null
            : result.Segments.FirstOrDefault(segment => segment.Conflicts.Any(conflict => conflict.WithFavorable));
        if (conflicted is not null)
        {
            sentences.Add(Text("explainConflict")
                .Replace("{range}", conflicted.TimeRangeDisplay, StringComparison.Ordinal)
                .Replace("{status}", conflicted.Status.Name, StringComparison.Ordinal)
                .Replace("{favorable}", string.Join(", ", conflicted.Reasons.Where(IsFavorablePeriod).Select(reason => reason.Label)), StringComparison.Ordinal)
                .Replace("{conflicts}", string.Join(", ", conflicted.Conflicts.Select(conflict => conflict.Label.Replace("Overlaps ", string.Empty, StringComparison.Ordinal))), StringComparison.Ordinal));
        }

        var notes = result.Segments
            .Where(segment => segment.IsGoodTime || segment.Conflicts.Any(conflict => conflict.WithFavorable))
            .Select(segment => new GoodTimeNote(
                segment.Id,
                string.Join(
                    separator,
                    segment.Reasons.Where(reason => reason.Type == "positive").Select(reason => "✓ " + reason.Label)
                        .Concat(segment.Conflicts.Select(conflict => "⚠ " + conflict.Label)))))
            .ToArray();

        return new GoodTimeExplanation(TemplateSource, string.Join(' ', sentences), notes, null, null, promptVersion, null);
    }

    /// <summary>The evaluation as compact JSON: everything the narrative may use, nothing more (no coordinates).</summary>
    private static string BuildInput(GoodTimesResult result) =>
        JsonSerializer.Serialize(
            new
            {
                language = result.Language,
                activity = result.Activity.Name,
                requestedText = result.Activity.RequestedText,
                date = result.DateDisplay,
                place = result.Location.Place?.City,
                tradition = result.Tradition.Name,
                dayStatus = result.DayStatus.Name,
                goodTimes = result.GoodTimes,
                noGoodTimeMessage = result.NoGoodTimeMessage,
                segments = result.Segments.Select(segment => new
                {
                    segmentId = segment.Id,
                    time = segment.TimeRangeDisplay,
                    status = segment.Status.Status,
                    isGoodTime = segment.IsGoodTime,
                    reasons = segment.Reasons.Select(reason => reason.Subject?.StartsWith("birth=", StringComparison.Ordinal) == true
                        ? $"{reason.Label} ({reason.Subject})" : reason.Label),
                    conflicts = segment.Conflicts.Select(conflict => $"{conflict.Label} ({Time(conflict.OverlapStart)}–{Time(conflict.OverlapEnd)})"),
                }),
            },
            SerializerOptions);

    /// <summary>Returns the first guardrail the AI output breaks, or null when it is safe to show.</summary>
    internal static string? Validate(AiExplanation explanation, GoodTimesResult result)
    {
        if (string.IsNullOrWhiteSpace(explanation.Summary))
        {
            return "EMPTY_SUMMARY";
        }

        var segments = result.Segments.ToDictionary(segment => segment.Id);
        if (explanation.Notes.Any(note => !segments.ContainsKey(note.SegmentId)))
        {
            return "UNKNOWN_SEGMENT";
        }

        var allowedTimes = result.Segments
            .SelectMany(segment => new[] { segment.Start, segment.End }.Concat(segment.Conflicts.SelectMany(conflict =>
                new[] { conflict.Start, conflict.End, conflict.OverlapStart, conflict.OverlapEnd })))
            .Select(Time)
            .ToHashSet(StringComparer.Ordinal);
        var texts = explanation.Notes.Select(note => note.Text).Append(explanation.Summary);
        if (texts.SelectMany(text => Times().Matches(text).Select(match => Normalize(match.Value))).Any(time => !allowedTimes.Contains(time)))
        {
            return "UNKNOWN_TIME";
        }

        if (explanation.Notes.Any(note => !segments[note.SegmentId].IsGoodTime && PromotesTime(note.Text)))
        {
            return "PROMOTES_NON_GOOD_SEGMENT";
        }

        if (result.GoodTimes.Count == 0 && (PromotesTime(explanation.Summary) && !explanation.Summary.Contains("no ", StringComparison.OrdinalIgnoreCase)
            || Times().IsMatch(explanation.Summary)))
        {
            return "SUGGESTS_TIME_WITHOUT_GOOD_TIME";
        }

        return null;
    }

    private static bool PromotesTime(string text) => PromotionWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static bool IsFavorablePeriod(GoodTimeReason reason) =>
        reason.Type == "positive" && reason.Subject is not null && !reason.Code.StartsWith("FAVORABLE_", StringComparison.Ordinal)
        && !reason.Code.StartsWith("NO_", StringComparison.Ordinal)
        && reason.Subject?.StartsWith("birth=", StringComparison.Ordinal) != true;

    private static string Time(DateTimeOffset value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Normalize(string time)
    {
        var parts = time.Split(':');
        return $"{int.Parse(parts[0], CultureInfo.InvariantCulture):00}:{parts[1]}";
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b")]
    private static partial Regex Times();
}

/// <summary>The AI's answer for an explanation.</summary>
internal sealed record AiExplanation(string Summary, IReadOnlyList<AiExplanationNote> Notes);

/// <summary>One note of the AI's answer.</summary>
internal sealed record AiExplanationNote(string SegmentId, string Text);
