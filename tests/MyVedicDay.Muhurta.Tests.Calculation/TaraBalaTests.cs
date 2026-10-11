using System.Text.Json;
using MyVedicDay.Muhurta.Calculation;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;
using static MyVedicDay.Muhurta.Tests.Calculation.TestDays;

namespace MyVedicDay.Muhurta.Tests.Calculation;

public sealed class TaraBalaTests
{
    [Fact]
    public void AllBirthAndActiveStars_UseInclusiveWrappingNineCycle()
    {
        foreach (var birth in Enum.GetValues<Nakshatra>())
        {
            for (var distance = 0; distance < 27; distance++)
            {
                var active = (Nakshatra)(((int)birth - 1 + distance) % 27 + 1);
                var position = TaraBala.Position(birth, active);
                Assert.Equal(distance % 9 + 1, position);
                Assert.Equal(position is 1 ? MuhurtaStatus.Mixed : position is 3 or 5 or 7 ? MuhurtaStatus.Caution : (MuhurtaStatus?)null,
                    TaraBala.MaximumStatus(position));
            }
        }
    }

    [Fact]
    public void OmittedAndNullBirthStar_KeepExistingEvaluationAndOrdering()
    {
        var day = Day([Period(PanchangPeriodKind.AmritKalam, At(12, 0), At(13, 0), true)]);
        var original = MuhurtaDayEvaluator.Evaluate(day, Profile(), null);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(MuhurtaDayEvaluator.Evaluate(day, Profile(), null, null)));
        Assert.Equal(MuhurtaStatus.HighlyFavorable, original.Segments.Single(segment => segment.Start == At(12, 0)).Status);
    }

    [Fact]
    public void BirthStar_CapsAtTransitions_WithoutChangingScoresOrPromotingRestrictions()
    {
        var day = Day([Period(PanchangPeriodKind.RahuKalam, At(13, 0), At(14, 0), false)]) with
        {
            Nakshatras = [new(Nakshatra.Ashwini, At(6, 0), At(9, 0)), new(Nakshatra.Bharani, At(9, 0), At(12, 0)),
                new(Nakshatra.Krittika, At(12, 0), At(18, 0))],
        };
        var standard = MuhurtaDayEvaluator.Evaluate(day, Profile(), null);
        var personal = MuhurtaDayEvaluator.Evaluate(day, Profile(), null, Nakshatra.Ashwini);
        Assert.Equal(MuhurtaStatus.Mixed, personal.Segments.Single(segment => segment.Start == At(6, 0)).Status);
        Assert.True(personal.Segments.Single(segment => segment.Start == At(9, 0)).IsGoodTime);
        Assert.Equal(MuhurtaStatus.Caution, personal.Segments.Single(segment => segment.Start == At(12, 0)).Status);
        foreach (var segment in personal.Segments)
        {
            var baseline = standard.Segments.Single(item => item.Start <= segment.Start && item.End >= segment.End);
            Assert.Equal(baseline.Score, segment.Score);
            Assert.True(segment.Status >= baseline.Status);
            if (segment.Conflicts.Count > 0) Assert.False(segment.IsGoodTime);
        }
        var compatible = MuhurtaDayEvaluator.Evaluate(day, Profile(), null, Nakshatra.Revati);
        Assert.Equal(MuhurtaStatus.Avoid, compatible.Segments.Single(segment => segment.Start == At(13, 0)).Status);
    }

    [Theory]
    [InlineData(Limb.Tithi, "ShuklaDwitiya")]
    [InlineData(Limb.Vara, "Thursday")]
    [InlineData(Limb.Yoga, "Siddhi")]
    [InlineData(Limb.Karana, "Bava")]
    [InlineData(Limb.Nakshatra, "Rohini")]
    public void CompatibleTara_DoesNotOverrideActivityExclusions(Limb limb, string value)
    {
        var factors = new Dictionary<Limb, IReadOnlyDictionary<string, FactorEffect>>
        {
            [limb] = new Dictionary<string, FactorEffect> { [value] = FactorEffect.Excluded },
        };
        var result = MuhurtaDayEvaluator.Evaluate(Day([]), Profile(factors), null, Nakshatra.Krittika);
        Assert.Empty(result.GoodTimes);
        Assert.All(result.Segments, segment => Assert.Equal(MuhurtaStatus.Avoid, segment.Status));
    }
}
