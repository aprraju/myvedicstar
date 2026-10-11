using MyVedicDay.Localization.Data;
using MyVedicDay.Muhurta.Business.Rules;
using MyVedicDay.Muhurta.Calculation;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Muhurta.Data;
using MyVedicDay.Traditions;
using NodaTime;

namespace MyVedicDay.Muhurta.Tests.Unit;

/// <summary>
/// The activity rules and content are consistent: every activity resolves in every tradition, and every code the
/// engine can produce for them has a label.
/// </summary>
public sealed class MuhurtaRulesTests
{
    private static readonly MuhurtaTestHost Host = new(Instant.FromUtc(2026, 10, 1, 3, 0));
    private static readonly MuhurtaRulesDocument Document =
        Host.GetRequiredService<IMuhurtaRulesRepository>().GetRulesAsync(CancellationToken.None).GetAwaiter().GetResult();
    private static readonly ContentCatalog English =
        Host.GetRequiredService<IContentRepository>().GetCatalogAsync("en", CancellationToken.None).GetAwaiter().GetResult()!;

    [Fact]
    public void Activities_AreTheRequiredElevenInOrder_WithGeneralAndOther()
    {
        Assert.Equal(
            ["business", "travel", "property", "vehicle", "agreement", "education", "puja", "wedding", "housewarming", "general", "other"],
            Document.Activities.Select(activity => activity.Key));
        Assert.True(Document.Activities.Single(activity => activity.Key == "other").RequiresInput);
        Assert.All(Document.Activities.Where(activity => activity.Key is "wedding" or "housewarming"), activity => Assert.Equal("partial", activity.Completeness));
    }

    [Fact]
    public async Task EveryActivity_ResolvesInEveryTradition_WithLabelsForEveryCode()
    {
        var provider = Host.GetRequiredService<MuhurtaRulesProvider>();
        var traditions = await Host.GetRequiredService<ITraditionRegistry>().GetKeysAsync(CancellationToken.None);

        foreach (var activity in Document.Activities.Where(activity => activity.Rules is not null))
        {
            Assert.True(English.Entries.ContainsKey($"muhurtaActivities.{activity.Key}"), activity.Key);
            foreach (var tradition in traditions)
            {
                var profile = await provider.GetProfileAsync(activity.Key, tradition, CancellationToken.None);

                Assert.NotEmpty(profile.UnfavorablePeriods);
                foreach (var kind in profile.FavorablePeriods.Keys)
                {
                    AssertLabel($"muhurtaReasons.{ReasonCodes.ForPeriod(kind)}");
                }

                foreach (var kind in profile.UnfavorablePeriods.Keys)
                {
                    AssertLabel($"muhurtaConflicts.{ReasonCodes.ForPeriod(kind)}");
                    AssertLabel($"muhurtaReasons.{ReasonCodes.NoConflict(kind)}");
                }

                foreach (var (limb, effects) in profile.Factors)
                {
                    foreach (var effect in effects.Values.Distinct().Where(effect => effect != FactorEffect.Neutral))
                    {
                        AssertLabel($"muhurtaReasons.{ReasonCodes.ForLimb(effect, limb)}");
                    }

                    AssertLabel($"muhurtaReasons.{ReasonCodes.ForLimb(FactorEffect.Supportive, limb)}");
                }
            }
        }
    }

    [Fact]
    public void StatusesMessagesAndMonthReasons_HaveText()
    {
        Assert.All(Enum.GetNames<MuhurtaStatus>(), status => AssertLabel($"muhurtaStatuses.{status}"));
        Assert.All(new[] { NoGoodTimeReasons.NoQualifyingPeriod, NoGoodTimeReasons.DayExcludedForActivity }, code =>
            Assert.False(string.IsNullOrWhiteSpace(English.Entries[$"muhurtaNoGoodTime.{code}"].Message)));
        AssertLabel($"muhurtaReasons.{ReasonCodes.AdhikaMasa}");
        AssertLabel($"muhurtaReasons.{ReasonCodes.ExcludedLunarMonth}");
        Assert.Contains("consult a qualified astrologer", English.Entries["muhurtaText.disclaimer"].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TraditionOverrides_ApplyOnTopOfTheActivity()
    {
        var provider = Host.GetRequiredService<MuhurtaRulesProvider>();

        var gujarati = await provider.GetProfileAsync("business", "gujarati", CancellationToken.None);
        var tamil = await provider.GetProfileAsync("business", "tamil", CancellationToken.None);
        var telugu = await provider.GetProfileAsync("business", "telugu", CancellationToken.None);
        var wedding = await provider.GetProfileAsync("wedding", "all", CancellationToken.None);

        Assert.Contains(Panchang.Calculation.Models.PanchangPeriodKind.ChoghadiyaLabh, gujarati.FavorablePeriods.Keys);
        Assert.DoesNotContain(Panchang.Calculation.Models.PanchangPeriodKind.ChoghadiyaLabh, tamil.FavorablePeriods.Keys);
        Assert.Equal(MuhurtaStatus.Caution, telugu.UnfavorablePeriods[Panchang.Calculation.Models.PanchangPeriodKind.Varjyam].WithFavorable);
        Assert.Equal(FactorEffect.Excluded, gujarati.Factors[Limb.Karana]["Vishti"]);

        Assert.Equal(MuhurtaStatus.Favorable, wedding.MaximumStatus);
        Assert.True(wedding.ExcludeAdhikaMonth);
        Assert.Equal(FactorEffect.Supportive, wedding.Factors[Limb.Nakshatra]["Rohini"]);
        Assert.Equal(FactorEffect.Excluded, wedding.Factors[Limb.Nakshatra]["Ardra"]); // '*' then values
    }

    private static void AssertLabel(string key)
    {
        Assert.True(English.Entries.TryGetValue(key, out var entry), $"Missing {key}.");
        Assert.False(string.IsNullOrWhiteSpace(entry.Name ?? entry.Message), key);
    }
}
