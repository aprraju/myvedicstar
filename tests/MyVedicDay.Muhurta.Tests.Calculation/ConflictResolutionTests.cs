using MyVedicDay.Astronomy;
using MyVedicDay.Muhurta.Calculation;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;
using static MyVedicDay.Muhurta.Tests.Calculation.TestDays;

namespace MyVedicDay.Muhurta.Tests.Calculation;

/// <summary>
/// The requirement's conflict examples, reproduced exactly, and the rules that keep a conflicting period from ever
/// being a Good Time.
/// </summary>
public sealed class ConflictResolutionTests
{
    [Fact]
    public void AbhijitWithDurMuhurtam_SplitsIntoFavorableConflictFavorable()
    {
        var day = Day([
            Period(PanchangPeriodKind.AbhijitMuhurta, At(16, 0), At(16, 48), auspicious: true),
            Period(PanchangPeriodKind.DurMuhurtam, At(16, 6), At(16, 27), auspicious: false),
        ]);

        var result = MuhurtaDayEvaluator.Evaluate(day, Profile(), null);

        var first = Segment(result, At(16, 0));
        var conflict = Segment(result, At(16, 6));
        var last = Segment(result, At(16, 27));
        Assert.Equal((At(16, 0), At(16, 6), MuhurtaStatus.Favorable), (first.Start, first.End, first.Status));
        Assert.Equal((At(16, 6), At(16, 27), MuhurtaStatus.Caution), (conflict.Start, conflict.End, conflict.Status));
        Assert.Equal((At(16, 27), At(16, 48), MuhurtaStatus.Favorable), (last.Start, last.End, last.Status));

        var durMuhurtam = Assert.Single(conflict.Conflicts);
        Assert.Equal("DUR_MUHURTAM", durMuhurtam.Code);
        Assert.True(durMuhurtam.WithFavorable);
        Assert.False(conflict.IsGoodTime);
        Assert.Contains(conflict.Reasons, reason => reason.Code == "ABHIJIT_MUHURTA");
    }

    [Fact]
    public void AmritKalamWithRahuKalam_IsMixed_WithTheFullRahuKalamTimes()
    {
        var day = Day([
            Period(PanchangPeriodKind.AmritKalam, At(14, 58), At(16, 27), auspicious: true),
            Period(PanchangPeriodKind.RahuKalam, At(14, 49), At(16, 18), auspicious: false),
        ]);

        var result = MuhurtaDayEvaluator.Evaluate(day, Profile(), null);

        var rahuOnly = Segment(result, At(14, 49));
        var mixed = Segment(result, At(14, 58));
        var amritOnly = Segment(result, At(16, 18));
        Assert.Equal((At(14, 49), At(14, 58), MuhurtaStatus.Avoid), (rahuOnly.Start, rahuOnly.End, rahuOnly.Status));
        Assert.Equal((At(14, 58), At(16, 18), MuhurtaStatus.Mixed), (mixed.Start, mixed.End, mixed.Status));
        Assert.Equal(MuhurtaStatus.HighlyFavorable, amritOnly.Status); // 65 + 20

        var rahu = Assert.Single(mixed.Conflicts);
        Assert.Equal(("RAHU_KALAM", At(14, 49), At(16, 18), At(14, 58), At(16, 18)), (rahu.Code, rahu.PeriodStart, rahu.PeriodEnd, rahu.OverlapStart, rahu.OverlapEnd));
        Assert.Contains(mixed.Reasons, reason => reason is { Type: ReasonType.Positive, Code: "AMRIT_KALAM" });
        Assert.False(mixed.IsGoodTime);
        Assert.False(amritOnly.IsGoodTime); // only 9 minutes
    }

    [Fact]
    public void GoodTimes_AreFavorable_ConflictFree_LongEnough_AndExplained()
    {
        var day = Day([
            Period(PanchangPeriodKind.RahuKalam, At(13, 30), At(15, 0), auspicious: false),
            Period(PanchangPeriodKind.AbhijitMuhurta, At(11, 36), At(12, 24), auspicious: true),
        ]);

        var result = MuhurtaDayEvaluator.Evaluate(day, Profile(), null);

        Assert.NotEmpty(result.GoodTimes);
        Assert.All(result.GoodTimes.Select(index => result.Segments[index]), segment =>
        {
            Assert.True(segment.Status <= MuhurtaStatus.Favorable);
            Assert.Empty(segment.Conflicts);
            Assert.True(segment.End - segment.Start >= Duration.FromMinutes(24));
            Assert.False(segment.Start < At(15, 0) && At(13, 30) < segment.End, "A Good Time never overlaps Rahu Kalam.");
            Assert.Contains(segment.Reasons, reason => reason.Code == "NO_RAHU_KALAM_CONFLICT");
        });

        // Abhijit ranks first: same limbs plus its bonus.
        Assert.Equal(At(11, 36), result.Segments[result.GoodTimes[0]].Start);
        Assert.Equal(MuhurtaStatus.Favorable, result.DayStatus);
        Assert.Null(result.NoGoodTimeReason);
    }

    [Fact]
    public void UnfavorablePeriodAlone_IsAvoid_AndGulikaAloneIsCaution()
    {
        var day = Day([
            Period(PanchangPeriodKind.RahuKalam, At(9, 0), At(10, 30), auspicious: false),
            Period(PanchangPeriodKind.GulikaKalam, At(12, 0), At(13, 30), auspicious: false),
        ]);

        var result = MuhurtaDayEvaluator.Evaluate(day, Profile(), null);

        Assert.Equal(MuhurtaStatus.Avoid, Segment(result, At(9, 0)).Status);
        Assert.Equal(MuhurtaStatus.Caution, Segment(result, At(12, 0)).Status);
    }

    [Fact]
    public void ExcludedValueAllDay_GivesNoGoodTime_AndNeverPromotesAnotherPeriod()
    {
        var factors = new Dictionary<Limb, IReadOnlyDictionary<string, FactorEffect>>
        {
            [Limb.Karana] = new Dictionary<string, FactorEffect> { ["Bava"] = FactorEffect.Excluded },
        };
        var day = Day([Period(PanchangPeriodKind.AmritKalam, At(10, 0), At(12, 0), auspicious: true)]);

        var result = MuhurtaDayEvaluator.Evaluate(day, Profile(factors), null);

        Assert.Empty(result.GoodTimes);
        Assert.All(result.Segments, segment => Assert.Equal(MuhurtaStatus.Avoid, segment.Status));
        Assert.Equal(NoGoodTimeReasons.DayExcludedForActivity, result.NoGoodTimeReason);
        Assert.Contains(result.Segments[0].Reasons, reason => reason is { Code: "EXCLUDED_KARANA", Subject: "Bava" });
    }

    [Fact]
    public void ElementTransition_SplitsSegments_AndChangesTheStatus()
    {
        var factors = new Dictionary<Limb, IReadOnlyDictionary<string, FactorEffect>>
        {
            [Limb.Tithi] = new Dictionary<string, FactorEffect> { ["ShuklaChaturthi"] = FactorEffect.Excluded },
        };
        var tithis = new ElementPeriod<Tithi>[]
        {
            new(Tithi.ShuklaTritiya, At(1, 0), At(11, 0)),
            new(Tithi.ShuklaChaturthi, At(11, 0), At(23, 59) + Duration.FromHours(10)),
        };

        var result = MuhurtaDayEvaluator.Evaluate(Day([], tithis: tithis), Profile(factors), null);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal((At(6, 0), At(11, 0), true), (result.Segments[0].Start, result.Segments[0].End, result.Segments[0].IsGoodTime));
        Assert.Equal(MuhurtaStatus.Avoid, result.Segments[1].Status);
    }

    [Fact]
    public void AdhikaMonth_RulesTheDayOut_WhenTheProfileSaysSo()
    {
        var result = MuhurtaDayEvaluator.Evaluate(Day([]), Profile(excludeAdhika: true), (LunarMonth.Jyeshtha, true));

        Assert.All(result.Segments, segment => Assert.Equal(MuhurtaStatus.Avoid, segment.Status));
        Assert.Contains(result.Segments[0].Reasons, reason => reason is { Code: ReasonCodes.AdhikaMasa, Subject: "Jyeshtha" });
        Assert.Equal(NoGoodTimeReasons.DayExcludedForActivity, result.NoGoodTimeReason);
        Assert.Equal(LunarMonth.Jyeshtha, result.LunarMonth);
    }

    [Fact]
    public void ExcludedLunarMonth_RulesTheDayOut()
    {
        var result = MuhurtaDayEvaluator.Evaluate(Day([]), Profile(excludedMonths: new HashSet<LunarMonth> { LunarMonth.Bhadrapada }), (LunarMonth.Bhadrapada, false));

        Assert.Empty(result.GoodTimes);
        Assert.Contains(result.Segments[0].Reasons, reason => reason.Code == ReasonCodes.ExcludedLunarMonth);
    }

    [Fact]
    public void MaximumStatus_CapsHighlyFavorable()
    {
        var day = Day([Period(PanchangPeriodKind.AmritKalam, At(10, 0), At(12, 0), auspicious: true)]);

        var result = MuhurtaDayEvaluator.Evaluate(day, Profile(maximumStatus: MuhurtaStatus.Favorable), null);

        Assert.All(result.Segments, segment => Assert.True(segment.Status >= MuhurtaStatus.Favorable));
        Assert.Equal(MuhurtaStatus.Favorable, result.DayStatus);
    }

    [Fact]
    public void AbhijitOnWednesday_IsNotFavorable()
    {
        var day = Day([Period(PanchangPeriodKind.AbhijitMuhurta, At(11, 36), At(12, 24), auspicious: true)], IsoDayOfWeek.Wednesday);

        var result = MuhurtaDayEvaluator.Evaluate(day, Profile(), null);

        Assert.DoesNotContain(result.Segments.SelectMany(segment => segment.Reasons), reason => reason.Code == "ABHIJIT_MUHURTA");
        Assert.Single(result.Segments); // nothing to split on
    }

    [Fact]
    public void Window_AnyIncludesTheNight_DaytimeEndsAtSunset()
    {
        Assert.Equal(At(18, 0), MuhurtaDayEvaluator.Evaluate(Day([]), Profile(), null).Segments[^1].End);
        Assert.Equal(At(6, 0) + Duration.FromDays(1), MuhurtaDayEvaluator.Evaluate(Day([]), Profile(window: EvaluationWindow.Any), null).Segments[^1].End);
    }

    [Fact]
    public void NoGoodTime_WhenEveryFavorableStretchIsTooShortOrConflicting()
    {
        var factors = new Dictionary<Limb, IReadOnlyDictionary<string, FactorEffect>>
        {
            [Limb.Nakshatra] = new Dictionary<string, FactorEffect> { ["Rohini"] = FactorEffect.Adverse },
            [Limb.Yoga] = new Dictionary<string, FactorEffect> { ["Siddhi"] = FactorEffect.Adverse },
        };
        var day = Day([Period(PanchangPeriodKind.AbhijitMuhurta, At(11, 36), At(11, 50), auspicious: true)]);

        var result = MuhurtaDayEvaluator.Evaluate(day, Profile(factors), null);

        Assert.Empty(result.GoodTimes);
        Assert.Equal(NoGoodTimeReasons.NoQualifyingPeriod, result.NoGoodTimeReason);
        Assert.True(result.DayStatus >= MuhurtaStatus.Mixed, "Without a Good Time the day is at best Mixed.");
    }

    [Theory]
    [InlineData(PanchangPeriodKind.RahuKalam, "RAHU_KALAM")]
    [InlineData(PanchangPeriodKind.ChoghadiyaLabh, "CHOGHADIYA_LABH")]
    [InlineData(PanchangPeriodKind.DurMuhurtam, "DUR_MUHURTAM")]
    public void PeriodCodes_AreUpperSnakeCase(PanchangPeriodKind kind, string expected)
    {
        Assert.Equal(expected, ReasonCodes.ForPeriod(kind));
        Assert.Equal($"NO_{expected}_CONFLICT", ReasonCodes.NoConflict(kind));
    }

    private static MuhurtaSegment Segment(MuhurtaEvaluation result, Instant start) =>
        Assert.Single(result.Segments, segment => segment.Start == start);
}
