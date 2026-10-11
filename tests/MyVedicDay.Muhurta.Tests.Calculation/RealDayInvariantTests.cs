using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicDay.Muhurta.Calculation;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Muhurta.Tests.Calculation;

/// <summary>
/// Invariants over real calculated days (India and the USA, a full season, both windows): the timeline covers the
/// window without gaps, and no Good Time is ever Mixed, Caution, Avoid, conflicting, too short or inside an
/// unfavorable period.
/// </summary>
public sealed class RealDayInvariantTests
{
    private static readonly IMuhurtaCalculator Calculator =
        new ServiceCollection().AddMuhurtaCalculation().BuildServiceProvider().GetRequiredService<IMuhurtaCalculator>();

    public static TheoryData<string, double, double, EvaluationWindow> Locations() => new()
    {
        { "Asia/Kolkata", 17.385, 78.4867, EvaluationWindow.Daytime },
        { "America/Chicago", 30.2672, -97.7431, EvaluationWindow.Any },
    };

    [Theory]
    [MemberData(nameof(Locations))]
    public void GoodTimesNeverConflict_AndSegmentsCoverTheWindow(string zoneId, double latitude, double longitude, EvaluationWindow window)
    {
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var profile = WithAllConflicts(TestDays.Profile(window: window));
        var goodTimes = 0;

        for (var date = new LocalDate(2026, 10, 1); date < new LocalDate(2026, 12, 1); date = date.PlusDays(1))
        {
            var result = Calculator.Evaluate(new MuhurtaCalculationRequest(date, new GeoCoordinate(latitude, longitude), zone, profile));

            Assert.All(result.Segments.Zip(result.Segments.Skip(1)), pair => Assert.Equal(pair.First.End, pair.Second.Start));
            Assert.Equal(result.GoodTimes.Count == 0, result.NoGoodTimeReason is not null);
            foreach (var segment in result.GoodTimes.Select(index => result.Segments[index]))
            {
                Assert.True(segment.Status <= MuhurtaStatus.Favorable);
                Assert.Empty(segment.Conflicts);
                Assert.True(segment.End - segment.Start >= Duration.FromMinutes(profile.MinimumMinutes));
            }

            Assert.All(result.Segments.Where(segment => segment.Conflicts.Count > 0), segment => Assert.False(segment.IsGoodTime));
            goodTimes += result.GoodTimes.Count;
        }

        Assert.True(goodTimes > 0);
    }

    private static ActivityRuleProfile WithAllConflicts(ActivityRuleProfile profile) => profile with
    {
        UnfavorablePeriods = new Dictionary<PanchangPeriodKind, UnfavorablePeriodRule>
        {
            [PanchangPeriodKind.RahuKalam] = new(MuhurtaStatus.Avoid, MuhurtaStatus.Mixed),
            [PanchangPeriodKind.Yamagandam] = new(MuhurtaStatus.Avoid, MuhurtaStatus.Mixed),
            [PanchangPeriodKind.DurMuhurtam] = new(MuhurtaStatus.Avoid, MuhurtaStatus.Caution),
            [PanchangPeriodKind.Varjyam] = new(MuhurtaStatus.Avoid, MuhurtaStatus.Mixed),
            [PanchangPeriodKind.GulikaKalam] = new(MuhurtaStatus.Caution, MuhurtaStatus.Mixed),
        },
    };
}
