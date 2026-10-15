using MyVedicDay.Festivals.Business.Rules;
using MyVedicDay.Festivals.Data;
using MyVedicDay.Localization.Data;
using MyVedicDay.Traditions;
using NodaTime;

namespace MyVedicDay.Festivals.Tests.Unit;

/// <summary>
/// The festival rules and their content are consistent: every rule parses, every tradition exists, every festival,
/// category and observance time has text, and superseded festivals exist.
/// </summary>
public sealed class FestivalRulesTests
{
    private static readonly FestivalTestHost Host = new(Instant.FromUtc(2026, 9, 14, 3, 0));
    private static readonly FestivalRulesDocument Document =
        Host.GetRequiredService<IFestivalRulesRepository>().GetRulesAsync(CancellationToken.None).GetAwaiter().GetResult();
    private static readonly ContentCatalog English =
        Host.GetRequiredService<IContentRepository>().GetCatalogAsync("en", CancellationToken.None).GetAwaiter().GetResult()!;

    [Fact]
    public async Task EveryRule_Parses()
    {
        var rules = await Host.GetRequiredService<FestivalRulesProvider>().GetRulesAsync(CancellationToken.None);

        Assert.Equal(Document.Festivals.Count, rules.Definitions.Count);
        Assert.All(rules.Definitions, definition => Assert.NotEmpty(definition.Rules));
        Assert.False(string.IsNullOrWhiteSpace(rules.Version));
    }

    [Fact]
    public async Task EveryTradition_IsAKnownTradition()
    {
        var known = (await Host.GetRequiredService<ITraditionRegistry>().GetKeysAsync(CancellationToken.None)).Append("*").ToHashSet();

        Assert.All(Document.Festivals.SelectMany(festival => festival.Traditions), tradition => Assert.Contains(tradition, known));
    }

    [Fact]
    public void EverySupersededFestival_Exists()
    {
        var keys = Document.Festivals.Select(festival => festival.Key).ToHashSet();

        Assert.All(Document.Festivals.SelectMany(festival => festival.Supersedes ?? []), key => Assert.Contains(key, keys));
    }

    [Fact]
    public void EveryFestival_HasANameAndDescription()
    {
        Assert.All(Document.Festivals, festival =>
        {
            Assert.True(English.Entries.TryGetValue($"festivals.{festival.Key}", out var entry), $"Missing festivals.{festival.Key}.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        });
    }

    [Fact]
    public void EveryCategoryAndObservanceTime_HasAName()
    {
        Assert.All(Document.Festivals.Select(festival => festival.Category).Distinct(), category =>
            Assert.True(English.Entries.ContainsKey($"festivalCategories.{category}"), category));
        Assert.All(Enum.GetNames<Calculation.Models.ObservanceTime>(), time =>
            Assert.True(English.Entries.ContainsKey($"observanceTimes.{time}"), time));
    }

    [Fact]
    public async Task DayChoiceOptions_AreParsed()
    {
        var rules = await Parse(
            new FestivalRuleData("Tithi", "Aparahna", Tithi: "Purnima", AvoidBhadra: true, SunriseDayMinimumFraction: 0.2),
            new FestivalRuleData("Nakshatra", "Sunrise", Nakshatra: "Shravana", SolarMonth: "Simha", Occurrence: "Last"));

        var (bhadra, last) = (rules.Definitions[0].Rules[0], rules.Definitions[0].Rules[1]);
        Assert.True(bhadra.AvoidBhadra);
        Assert.Equal(0.2, bhadra.SunriseDayMinimumFraction);
        Assert.Equal(Calculation.Models.SolarMonthOccurrence.All, bhadra.Occurrence);
        Assert.Equal(Calculation.Models.SolarMonthOccurrence.Last, last.Occurrence);
    }

    [Fact]
    public async Task EkadashiConvention_IsParsed_AndOnlyAllowedOnEkadashi()
    {
        var rules = await Parse(new FestivalRuleData("Tithi", "Arunodaya", Tithi: "KrishnaEkadashi", Ekadashi: "Vaishnava"));
        Assert.Equal(Calculation.Models.EkadashiConvention.Vaishnava, rules.Definitions[0].Rules[0].Ekadashi);

        await Assert.ThrowsAsync<MyVedicDay.Shared.Exceptions.CalculationException>(() =>
            Parse(new FestivalRuleData("Tithi", "Sunrise", Tithi: "Purnima", Ekadashi: "Smarta")));
    }

    [Theory]
    [InlineData(0d, null, null)]
    [InlineData(1.5, null, null)]
    [InlineData(null, "Last", null)]
    [InlineData(null, "Second", "Simha")]
    public async Task InvalidDayChoiceOptions_AreRejected(double? fraction, string? occurrence, string? solarMonth)
    {
        var rule = new FestivalRuleData("Tithi", "Sunrise", Tithi: "Purnima", SolarMonth: solarMonth, SunriseDayMinimumFraction: fraction, Occurrence: occurrence);

        await Assert.ThrowsAsync<MyVedicDay.Shared.Exceptions.CalculationException>(() => Parse(rule));
    }

    [Fact]
    public void RegionalNames_OverrideOnlyKnownFestivalsAndTraditions()
    {
        var festivals = Document.Festivals.Select(festival => festival.Key).ToHashSet();
        var regional = English.Entries.Keys.Where(key => key.Contains("/festivals.", StringComparison.Ordinal));

        Assert.All(regional, key => Assert.Contains(key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..], festivals));
    }

    private static Task<FestivalRules> Parse(params FestivalRuleData[] rules) =>
        new FestivalRulesProvider(new StubRulesRepository(new FestivalRulesDocument(
            "test", [new FestivalRuleEntry("Test", "festival", ["*"], rules, null)])))
            .GetRulesAsync(CancellationToken.None);

    private sealed class StubRulesRepository(FestivalRulesDocument document) : IFestivalRulesRepository
    {
        public Task<FestivalRulesDocument> GetRulesAsync(CancellationToken cancellationToken) => Task.FromResult(document);
    }
}
