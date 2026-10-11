using MyVedicDay.Astronomy;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Muhurta.Tests.Calculation;

/// <summary>Synthetic days and profiles, so the requirement examples can be reproduced to the minute.</summary>
internal static class TestDays
{
    public static readonly LocalDate Date = new(2026, 10, 1); // Thursday

    /// <summary>A UTC instant on <see cref="Date"/>: the test zone is UTC, so local and UTC times coincide.</summary>
    public static Instant At(int hour, int minute) => Date.At(new LocalTime(hour, minute)).InUtc().ToInstant();

    /// <summary>Sunrise 06:00, sunset 18:00, one value of each limb all day, and the given periods.</summary>
    public static PanchangDay Day(
        IEnumerable<TimePeriod> periods,
        IsoDayOfWeek weekday = IsoDayOfWeek.Thursday,
        IReadOnlyList<ElementPeriod<Tithi>>? tithis = null)
    {
        var sunrise = At(6, 0);
        var nextSunrise = sunrise + Duration.FromDays(1);
        return new PanchangDay(
            Date,
            weekday,
            DateTimeZone.Utc,
            new SunEvents(sunrise - Duration.FromHours(12), sunrise, At(18, 0), nextSunrise),
            tithis ?? [new(Tithi.ShuklaDwitiya, sunrise - Duration.FromHours(3), nextSunrise + Duration.FromHours(3))],
            [new(Nakshatra.Rohini, sunrise - Duration.FromHours(5), nextSunrise + Duration.FromHours(5))],
            [new(Yoga.Siddhi, sunrise - Duration.FromHours(2), nextSunrise + Duration.FromHours(2))],
            [new(Karana.Bava, sunrise - Duration.FromHours(1), nextSunrise + Duration.FromHours(1))],
            periods.ToArray());
    }

    public static TimePeriod Period(PanchangPeriodKind kind, Instant start, Instant end, bool auspicious) =>
        new(kind, auspicious ? PeriodNature.Auspicious : PeriodNature.Inauspicious, PeriodGroup.Muhurta, start, end);

    /// <summary>
    /// Every limb neutral (score 65 = Favorable), Abhijit +15, Amrit Kalam +20, and the default conflict matrix.
    /// </summary>
    public static ActivityRuleProfile Profile(
        IReadOnlyDictionary<Limb, IReadOnlyDictionary<string, FactorEffect>>? factors = null,
        MuhurtaStatus? maximumStatus = null,
        IReadOnlySet<LunarMonth>? excludedMonths = null,
        bool excludeAdhika = false,
        EvaluationWindow window = EvaluationWindow.Daytime) =>
        new(
            "test",
            window,
            24,
            maximumStatus,
            Enum.GetValues<Limb>().ToDictionary(limb => limb, _ => 1d),
            factors ?? new Dictionary<Limb, IReadOnlyDictionary<string, FactorEffect>>(),
            new Dictionary<FactorEffect, int>
            {
                [FactorEffect.Supportive] = 100, [FactorEffect.Neutral] = 65, [FactorEffect.Adverse] = 30, [FactorEffect.Excluded] = 0,
            },
            [
                new(MuhurtaStatus.HighlyFavorable, 85), new(MuhurtaStatus.Favorable, 65), new(MuhurtaStatus.Mixed, 45),
                new(MuhurtaStatus.Caution, 30), new(MuhurtaStatus.Avoid, 0),
            ],
            new Dictionary<PanchangPeriodKind, FavorablePeriodRule>
            {
                [PanchangPeriodKind.AbhijitMuhurta] = new(15, new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Wednesday }),
                [PanchangPeriodKind.AmritKalam] = new(20, new HashSet<IsoDayOfWeek>()),
            },
            new Dictionary<PanchangPeriodKind, UnfavorablePeriodRule>
            {
                [PanchangPeriodKind.RahuKalam] = new(MuhurtaStatus.Avoid, MuhurtaStatus.Mixed),
                [PanchangPeriodKind.DurMuhurtam] = new(MuhurtaStatus.Avoid, MuhurtaStatus.Caution),
                [PanchangPeriodKind.GulikaKalam] = new(MuhurtaStatus.Caution, MuhurtaStatus.Mixed),
            },
            excludedMonths ?? new HashSet<LunarMonth>(),
            excludeAdhika);
}
