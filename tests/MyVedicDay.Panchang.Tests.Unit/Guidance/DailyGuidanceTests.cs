using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Localization.Data;
using MyVedicDay.Panchang.Business.Models;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Panchang.Data;
using MyVedicDay.Panchang.Data.Guidance;
using MyVedicDay.Panchang.Tests.Unit.TestDoubles;
using NodaTime;

namespace MyVedicDay.Panchang.Tests.Unit.Guidance;

/// <summary>
/// Daily guidance: a day rating from the five Panchanga limbs (configurable methodology) plus tradition-specific time
/// windows that never change the day rating.
/// </summary>
public sealed class DailyGuidanceTests
{
    private static readonly DateOnly Tuesday = new(2026, 9, 29);
    private static readonly PanchangQuery Hyderabad = new(17.385, 78.4867);

    private static readonly GuidanceRulesDocument Rules = new ServiceCollection()
        .AddLogging()
        .AddPanchangData(new ConfigurationBuilder().Build())
        .BuildServiceProvider()
        .GetRequiredService<IGuidanceRulesRepository>()
        .GetRulesAsync(CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public void Rules_RateEveryValueOfEveryLimb()
    {
        Assert.Equal(Enum.GetNames<Tithi>().Order(), Rules.Default.Tithi!.Keys.Order());
        Assert.Equal(Enum.GetNames<Nakshatra>().Order(), Rules.Default.Nakshatra!.Keys.Order());
        Assert.Equal(Enum.GetNames<Yoga>().Order(), Rules.Default.Yoga!.Keys.Order());
        Assert.Equal(Enum.GetNames<Karana>().Order(), Rules.Default.Karana!.Keys.Order());
        Assert.Equal(Enum.GetNames<DayOfWeek>().Order(), Rules.Default.Vara!.Keys.Order());
        Assert.All(new[] { "tithi", "vara", "nakshatra", "yoga", "karana" }, limb => Assert.True(Rules.Default.Weights![limb] > 0));
    }

    [Fact]
    public async Task Rules_UseKnownRatingsAndExplainedGroups()
    {
        var english = (await new ServiceCollection().AddLogging().AddPanchangData(new ConfigurationBuilder().Build())
            .BuildServiceProvider().GetRequiredService<IContentRepository>()
            .GetCatalogAsync("en", CancellationToken.None))!;

        var limbs = new Dictionary<string, IReadOnlyDictionary<string, GuidanceValueRule>>
        {
            ["tithi"] = Rules.Default.Tithi!, ["vara"] = Rules.Default.Vara!, ["nakshatra"] = Rules.Default.Nakshatra!,
            ["yoga"] = Rules.Default.Yoga!, ["karana"] = Rules.Default.Karana!,
        };

        foreach (var (limb, values) in limbs)
        {
            foreach (var (key, rule) in values)
            {
                Assert.True(Enum.TryParse<GuidanceRating>(rule.Rating, out _), $"{limb}.{key}: {rule.Rating}");
                Assert.True(english.Entries.ContainsKey($"guidanceGroups.{limb}.{rule.Group}"), $"No explanation for {limb}.{rule.Group}");
            }
        }
    }

    [Fact]
    public async Task EveryDay_HasGuidanceWithFiveFactorsHeadlineAndSummary()
    {
        using var host = CreateHost();

        var month = await host.Facade.GetMonthAsync(Hyderabad, 2026, 10, CancellationToken.None);

        Assert.All(month.Days, day =>
        {
            var guidance = Assert.IsType<DailyGuidance>(day.Guidance);
            Assert.Equal(["tithi", "vara", "nakshatra", "yoga", "karana"], guidance.Factors.Select(factor => factor.Limb));
            Assert.False(string.IsNullOrWhiteSpace(guidance.Headline));
            Assert.StartsWith(guidance.Icon, guidance.Headline, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(guidance.Summary));
            Assert.All(guidance.Factors, factor => Assert.False(string.IsNullOrWhiteSpace(factor.Reason)));
            Assert.Equal(Rules.Version, guidance.RulesVersion);
        });
    }

    [Fact]
    public async Task DayRating_FollowsTheConfiguredWeightsAndThresholds()
    {
        using var host = CreateHost();

        var month = await host.Facade.GetMonthAsync(Hyderabad, 2026, 11, CancellationToken.None);

        foreach (var guidance in month.Days.Select(day => day.Guidance!))
        {
            var score = guidance.Factors.Sum(factor => Rules.Default.Weights![factor.Limb] * Rules.RatingScores[factor.Rating.ToString()])
                / guidance.Factors.Sum(factor => Rules.Default.Weights![factor.Limb]);
            var expected = Rules.Thresholds.OrderByDescending(threshold => threshold.MinScore).First(threshold => score >= threshold.MinScore).Rating;

            Assert.Equal(expected, guidance.Rating.ToString());
        }
    }

    [Fact]
    public async Task Tradition_ChangesWindowsButNotTheDayRating()
    {
        using var host = CreateHost();

        var telugu = (await host.Facade.GetDateAsync(Hyderabad with { Tradition = "telugu" }, Tuesday, CancellationToken.None)).Days[0].Guidance!;
        var tamil = (await host.Facade.GetDateAsync(Hyderabad with { Tradition = "tamil" }, Tuesday, CancellationToken.None)).Days[0].Guidance!;

        Assert.Equal(telugu.Rating, tamil.Rating);
        Assert.Equal(telugu.Factors.Select(factor => factor.Key), tamil.Factors.Select(factor => factor.Key));
        Assert.Contains(telugu.AvoidWindows, window => window.Kind == "Varjyam");
        Assert.DoesNotContain(tamil.AvoidWindows, window => window.Kind == "Varjyam");
        Assert.Contains(tamil.CautionWindows, window => window.Name == "Kuligai");
    }

    [Fact]
    public async Task InauspiciousWindows_AreListed_ButDoNotLowerTheRating()
    {
        using var host = CreateHost();

        var guidance = (await host.Facade.GetDateAsync(Hyderabad, Tuesday, CancellationToken.None)).Days[0].Guidance!;

        Assert.Contains(guidance.AvoidWindows, window => window.Kind == "RahuKalam");
        Assert.Contains(guidance.SpiritualWindows, window => window.Kind == "BrahmaMuhurta");
        Assert.DoesNotContain(guidance.FavorableWindows, window => window.Kind == "BrahmaMuhurta");
        Assert.Contains("Rahu Kalam", guidance.Summary, StringComparison.Ordinal);
        Assert.Contains(" IST", guidance.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BestWindows_AreDaytimeStretchesFreeOfCautionAndAvoidPeriods()
    {
        using var host = CreateHost();

        var day = (await host.Facade.GetDateAsync(Hyderabad with { Tradition = "telugu" }, Tuesday, CancellationToken.None)).Days[0];
        var guidance = day.Guidance!;
        var blocked = guidance.CautionWindows.Concat(guidance.AvoidWindows).ToArray();

        Assert.InRange(guidance.BestWindows.Count, 1, Rules.BestWindows.MaximumCount);
        Assert.All(guidance.BestWindows, window =>
        {
            Assert.True(window.Start >= day.Sun.Sunrise && window.End <= day.Sun.Sunset);
            Assert.True(window.End - window.Start >= TimeSpan.FromMinutes(Rules.BestWindows.MinimumMinutes));
            Assert.DoesNotContain(blocked, period => period.Start < window.End && window.Start < period.End);
        });
    }

    [Fact]
    public async Task OverlapOfAmritKalamAndRahuKalam_IsAMixedWindow_AndNeitherPeriodIsCancelled()
    {
        // Amrit Kalam 14:58–16:27 IST (Nakshatra-based) and Rahu Kalam 14:49–16:18 IST (weekday-based) overlap 14:58–16:18.
        static Instant Ist(int hour, int minute) => Instant.FromUtc(2026, 9, 29, hour, minute).Minus(Duration.FromMinutes(330));
        var pinned = new Dictionary<PanchangPeriodKind, (Instant, Instant)>
        {
            [PanchangPeriodKind.AmritKalam] = (Ist(14, 58), Ist(16, 27)),
            [PanchangPeriodKind.RahuKalam] = (Ist(14, 49), Ist(16, 18)),
        };
        using var host = new BusinessTestHost(
            Instant.FromUtc(2026, 9, 28, 6, 0),
            services => services.AddSingleton<IPanchangCalculator>(new PinnedPeriodsPanchangCalculator(pinned)));

        var day = (await host.Facade.GetDateAsync(Hyderabad, Tuesday, CancellationToken.None)).Days[0];
        var guidance = day.Guidance!;

        var mixed = Assert.Single(guidance.MixedWindows, window => window.FavorablePeriods.Any(period => period.Kind == "AmritKalam"));
        Assert.Equal(Ist(14, 58).ToDateTimeOffset(), mixed.Start.ToUniversalTime());
        Assert.Equal(Ist(16, 18).ToDateTimeOffset(), mixed.End.ToUniversalTime());
        Assert.Equal(GuidanceRating.Mixed, mixed.Rating);
        Assert.Equal("🟡", mixed.Icon);
        Assert.Equal(["AmritKalam"], mixed.FavorablePeriods.Select(period => period.Kind));
        Assert.Equal(["RahuKalam"], mixed.UnfavorablePeriods.Select(period => period.Kind));

        // Both periods keep their full times.
        var amrit = Assert.Single(guidance.FavorableWindows, period => period.Kind == "AmritKalam");
        var rahu = Assert.Single(guidance.AvoidWindows, period => period.Kind == "RahuKalam");
        Assert.Equal(Ist(16, 27).ToDateTimeOffset(), amrit.End.ToUniversalTime());
        Assert.Equal(Ist(14, 49).ToDateTimeOffset(), rahu.Start.ToUniversalTime());

        Assert.Contains("Amrit Kalam with Rahu Kalam 14:58–16:18 IST", guidance.Summary, StringComparison.Ordinal);

        // The auspicious period itself names the times to avoid, in the day's periods and the guidance windows alike.
        var period = Assert.Single(day.Periods, period => period.Kind == "AmritKalam");
        Assert.Equal(period, amrit);
        var overlap = Assert.Single(period.Overlaps!);
        Assert.Equal("RahuKalam", overlap.Kind);
        Assert.Equal(PeriodClassification.Avoid, overlap.Classification);
        Assert.Equal(Ist(14, 58).ToDateTimeOffset(), overlap.Start.ToUniversalTime());
        Assert.Equal(Ist(16, 18).ToDateTimeOffset(), overlap.End.ToUniversalTime());
        Assert.Equal("Overlaps Rahu Kalam (14:49–16:18 IST): avoid 14:58–16:18 IST for new beginnings.", period.OverlapMessage);

        // Unfavorable periods carry no note.
        Assert.Null(rahu.Overlaps);
        Assert.Null(rahu.OverlapMessage);

        // A caution overlap: Abhijit Muhurta 11:42–12:30 with Gulika Kalam 12:06–13:36.
        var abhijit = Assert.Single(day.Periods, period => period.Kind == "AbhijitMuhurta");
        Assert.Equal("GulikaKalam", Assert.Single(abhijit.Overlaps!).Kind);
        Assert.StartsWith("Overlaps Gulika Kalam (12:06–13:36 IST): use caution 12:06–12:30 IST.", abhijit.OverlapMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FavorablePeriods_ListEveryOverlappingCautionOrAvoidPeriod()
    {
        using var host = CreateHost();

        var month = await host.Facade.GetMonthAsync(Hyderabad, 2026, 10, CancellationToken.None);

        foreach (var day in month.Days)
        {
            var muhurta = day.Periods.Where(period => period.Group == "Muhurta").ToArray();
            var unfavorable = muhurta.Where(period => period.Classification is PeriodClassification.Caution or PeriodClassification.Avoid).ToArray();

            foreach (var period in muhurta.Where(period => period.Classification is PeriodClassification.HighlyFavorable or PeriodClassification.Favorable))
            {
                var expected = unfavorable.Where(other => other.Start < period.End && period.Start < other.End).Select(other => other.Kind).ToArray();
                Assert.Equal(expected, period.Overlaps?.Select(overlap => overlap.Kind).ToArray() ?? []);
                Assert.Equal(expected.Length > 0, period.OverlapMessage is not null);
                Assert.All(period.Overlaps ?? [], overlap =>
                    Assert.True(overlap.Start >= period.Start && overlap.End <= period.End && overlap.Start < overlap.End));
            }
        }
    }

    [Fact]
    public async Task MixedWindows_AreExactlyWhereFavorableAndUnfavorablePeriodsOverlap()
    {
        using var host = CreateHost();

        var month = await host.Facade.GetMonthAsync(Hyderabad with { Tradition = "telugu" }, 2026, 10, CancellationToken.None);

        foreach (var guidance in month.Days.Select(day => day.Guidance!))
        {
            var favorable = guidance.FavorableWindows.Concat(guidance.SpiritualWindows).ToArray();
            var unfavorable = guidance.CautionWindows.Concat(guidance.AvoidWindows).ToArray();
            var expectedOverlap = favorable
                .SelectMany(good => unfavorable
                    .Where(bad => good.Start < bad.End && bad.Start < good.End)
                    .Select(bad => (Max(good.Start, bad.Start), Min(good.End, bad.End))))
                .Sum(overlap => (overlap.Item2 - overlap.Item1).Ticks);

            Assert.True(expectedOverlap == 0 || guidance.MixedWindows.Count > 0);
            Assert.All(guidance.MixedWindows, window =>
            {
                Assert.True(window.Start < window.End);
                Assert.NotEmpty(window.FavorablePeriods);
                Assert.NotEmpty(window.UnfavorablePeriods);
                Assert.All(window.FavorablePeriods.Concat(window.UnfavorablePeriods), period =>
                    Assert.True(period.Start < window.End && window.Start < period.End));
            });
            Assert.True(guidance.MixedWindows.Zip(guidance.MixedWindows.Skip(1)).All(pair => pair.First.End < pair.Second.Start));
        }
    }

    [Fact]
    public async Task PredominantDaytimeValue_IsRated_NotAValueEndingMinutesAfterSunrise()
    {
        using var host = CreateHost();

        // On this day the Yoga and Karana at sunrise end about 8 minutes after sunrise.
        var guidance = (await host.Facade.GetDateAsync(Hyderabad, Tuesday, CancellationToken.None)).Days[0].Guidance!;

        Assert.Equal("Vishti", guidance.Factors.Single(factor => factor.Limb == "karana").Key);
        Assert.Equal("Harshana", guidance.Factors.Single(factor => factor.Limb == "yoga").Key);
    }

    [Fact]
    public async Task Rules_AreConfigurable_IncludingSunriseValueSelectionAndTraditionOverrides()
    {
        var directory = Directory.CreateTempSubdirectory("mvd-guidance-").FullName;
        try
        {
            var rules = Rules with
            {
                ValueSelection = "Sunrise",
                Traditions = new Dictionary<string, GuidanceRuleSet>
                {
                    ["tamil"] = new(new Dictionary<string, double> { ["vara"] = 100 }, null, null, null, null, null),
                },
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "panchang-guidance-rules.json"),
                System.Text.Json.JsonSerializer.Serialize(rules, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
            using var host = CreateHost(new Dictionary<string, string?> { ["Content:Directory"] = directory });

            var telugu = (await host.Facade.GetDateAsync(Hyderabad with { Tradition = "telugu" }, Tuesday, CancellationToken.None)).Days[0].Guidance!;
            var tamil = (await host.Facade.GetDateAsync(Hyderabad with { Tradition = "tamil" }, Tuesday, CancellationToken.None)).Days[0].Guidance!;

            Assert.Equal("Vanija", telugu.Factors.Single(factor => factor.Limb == "karana").Key); // value at sunrise
            Assert.Equal(GuidanceRating.Caution, tamil.Rating); // Tamil override: Vara (Tuesday, Caution) dominates
            Assert.NotEqual(tamil.Rating, telugu.Rating);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SummaryText_IsContent_AndCanBeReworded()
    {
        var directory = Directory.CreateTempSubdirectory("mvd-guidance-text-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "panchang-content.en.json"), """
                { "language": "en", "entries": { "guidanceText.check": { "description": "Please review the timings below." } } }
                """);
            using var host = CreateHost(new Dictionary<string, string?> { ["Content:Directory"] = directory });

            var guidance = (await host.Facade.GetDateAsync(Hyderabad, Tuesday, CancellationToken.None)).Days[0].Guidance!;

            Assert.EndsWith("Please review the timings below.", guidance.Summary, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private static BusinessTestHost CreateHost(Dictionary<string, string?>? settings = null) =>
        new(Instant.FromUtc(2026, 9, 28, 6, 0), settings: settings);
}
