using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Localization.Data;
using MyVedicDay.Panchang.Data;

namespace MyVedicDay.Panchang.Tests.Unit.Localization;

/// <summary>
/// The default-language catalog is the fallback for every other language, so it must be complete.
/// </summary>
public sealed class ContentCatalogCompletenessTests
{
    private static readonly ContentCatalog English = new ServiceCollection()
        .AddLogging()
        .AddPanchangData(new ConfigurationBuilder().Build())
        .BuildServiceProvider()
        .GetRequiredService<IContentRepository>()
        .GetCatalogAsync("en", CancellationToken.None).GetAwaiter().GetResult()
        ?? throw new InvalidOperationException("English catalog is missing.");

    public static TheoryData<string> ValueNameKeys()
    {
        var keys = new TheoryData<string>();
        foreach (var name in Enum.GetNames<Tithi>()) { keys.Add($"tithi.{name}"); }
        foreach (var name in Enum.GetNames<Nakshatra>()) { keys.Add($"nakshatra.{name}"); }
        foreach (var name in Enum.GetNames<Yoga>()) { keys.Add($"yoga.{name}"); }
        foreach (var name in Enum.GetNames<Karana>()) { keys.Add($"karana.{name}"); }
        foreach (var name in Enum.GetNames<DayOfWeek>()) { keys.Add($"weekday.{name}"); }
        return keys;
    }

    public static TheoryData<string> ExplainedKeys()
    {
        var keys = new TheoryData<string>();
        foreach (var name in Enum.GetNames<PanchangPeriodKind>()) { keys.Add($"periods.{name}"); }
        foreach (var name in new[] { "tithi", "nakshatra", "yoga", "karana" }) { keys.Add($"elements.{name}"); }
        foreach (var name in new[] { "sunrise", "sunset", "vara" }) { keys.Add($"terms.{name}"); }
        return keys;
    }

    [Theory]
    [MemberData(nameof(ValueNameKeys))]
    public void EveryValue_HasAName(string key)
    {
        Assert.True(English.Entries.TryGetValue(key, out var entry), $"Missing {key}.");
        Assert.False(string.IsNullOrWhiteSpace(entry.Name));
    }

    [Theory]
    [MemberData(nameof(ExplainedKeys))]
    public void EveryPeriodElementAndTerm_HasNameMeaningAndDescription(string key)
    {
        Assert.True(English.Entries.TryGetValue(key, out var entry), $"Missing {key}.");
        Assert.False(string.IsNullOrWhiteSpace(entry.Name));
        Assert.False(string.IsNullOrWhiteSpace(entry.Meaning));
        Assert.False(string.IsNullOrWhiteSpace(entry.Description));
    }

    [Fact]
    public void EveryPeriod_HasASimpleMessage()
    {
        foreach (var kind in Enum.GetNames<PanchangPeriodKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(English.Entries[$"periods.{kind}"].Message), kind);
        }
    }

    [Theory]
    [InlineData(nameof(PeriodClassification.Caution))]
    [InlineData(nameof(PeriodClassification.Avoid))]
    public void EveryOverlapClassification_HasANoteTemplate(string classification)
    {
        var message = English.Entries[$"periodOverlap.{classification}"].Message;
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains("{name}", message, StringComparison.Ordinal);
        Assert.Contains("{range}", message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGuidanceRating_HasLabelIconHeadlineAndSummary()
    {
        foreach (var rating in Enum.GetNames<GuidanceRating>())
        {
            var entry = English.Entries[$"guidanceRatings.{rating}"];
            Assert.False(string.IsNullOrWhiteSpace(entry.Name), rating);
            Assert.False(string.IsNullOrWhiteSpace(entry.Icon), rating);
            Assert.False(string.IsNullOrWhiteSpace(entry.Message), rating);
            Assert.False(string.IsNullOrWhiteSpace(entry.Description), rating);
        }
    }

    [Theory]
    [InlineData("supportiveOne")]
    [InlineData("supportiveMany")]
    [InlineData("cautionOne")]
    [InlineData("cautionMany")]
    [InlineData("bestTime")]
    [InlineData("favorable")]
    [InlineData("caution")]
    [InlineData("avoid")]
    [InlineData("spiritual")]
    [InlineData("and")]
    [InlineData("separator")]
    [InlineData("nextDay")]
    [InlineData("reason")]
    public void EveryGuidanceTemplate_Exists(string template)
    {
        Assert.False(string.IsNullOrEmpty(English.Entries[$"guidanceText.{template}"].Message));
    }

    [Fact]
    public void EveryClassification_HasALabelAndIcon()
    {
        foreach (var classification in Enum.GetNames<PeriodClassification>())
        {
            var entry = English.Entries[$"classifications.{classification}"];
            Assert.False(string.IsNullOrWhiteSpace(entry.Name), classification);
            Assert.False(string.IsNullOrWhiteSpace(entry.Icon), classification);
        }
    }
}
