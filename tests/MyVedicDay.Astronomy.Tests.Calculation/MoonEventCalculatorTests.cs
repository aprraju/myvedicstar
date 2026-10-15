using NodaTime;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// Moonrise and moonset against published times, and the structural properties of the search.
/// </summary>
public sealed class MoonEventCalculatorTests
{
    private static readonly DateTimeZone India = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private static readonly GeoCoordinate NewDelhi = new(28.6139, 77.2090);
    private static readonly IMoonEventCalculator Calculator = new MoonEventCalculator(new MeeusEphemeris());

    /// <summary>Moonrise in New Delhi on Karwa Chauth, as published for the moon sighting.</summary>
    [Theory]
    [InlineData(2023, 11, 1, 20, 15)]
    [InlineData(2024, 10, 20, 19, 54)]
    [InlineData(2025, 10, 10, 20, 13)]
    public void Moonrise_MatchesPublishedTimes(int year, int month, int day, int hour, int minute)
    {
        var date = new LocalDate(year, month, day);
        var expected = date.At(new LocalTime(hour, minute)).InZoneStrictly(India).ToInstant();

        var moonrise = Calculator.FindMoonrise(StartOf(date), StartOf(date.PlusDays(1)), NewDelhi);

        Assert.NotNull(moonrise);
        var error = moonrise.Value - expected;
        Assert.True(Duration.Max(error, -error) <= Duration.FromMinutes(3), $"Moonrise {moonrise.Value.InZone(India):HH:mm}, expected {hour:00}:{minute:00}.");
    }

    [Fact]
    public void Moonrise_IsAboutFiftyMinutesLaterEachDay_AndSometimesMissing()
    {
        var rises = new List<Instant>();
        var daysWithout = 0;
        for (var date = new LocalDate(2026, 1, 1); date < new LocalDate(2026, 3, 1); date = date.PlusDays(1))
        {
            if (Calculator.FindMoonrise(StartOf(date), StartOf(date.PlusDays(1)), NewDelhi) is { } rise)
            {
                rises.Add(rise);
            }
            else
            {
                daysWithout++;
            }
        }

        // A lunar day is about 24 h 50 min, so about one civil day per synodic month has no moonrise.
        Assert.InRange(daysWithout, 1, 3);
        var gaps = rises.Zip(rises.Skip(1)).Select(pair => pair.Second - pair.First).ToArray();
        Assert.All(gaps, gap => Assert.InRange(gap, Duration.FromHours(24.2), Duration.FromHours(49.9)));
        Assert.InRange(gaps.Where(gap => gap < Duration.FromHours(26)).Average(gap => gap.TotalMinutes), 24 * 60 + 35, 24 * 60 + 65);
    }

    [Fact]
    public void MoonriseAndMoonset_Alternate()
    {
        var from = StartOf(new LocalDate(2026, 10, 1));
        var to = from + Duration.FromDays(5);
        var events = new List<(Instant At, bool Rise)>();
        for (var cursor = from; cursor < to;)
        {
            var rise = Calculator.FindMoonrise(cursor, to, NewDelhi);
            var set = Calculator.FindMoonset(cursor, to, NewDelhi);
            if (rise is null && set is null)
            {
                break;
            }

            var isRise = set is null || (rise is not null && rise < set);
            var at = isRise ? rise!.Value : set!.Value;
            events.Add((at, isRise));
            cursor = at + Duration.FromMinutes(1);
        }

        Assert.InRange(events.Count, 8, 11);
        Assert.All(events.Zip(events.Skip(1)), pair => Assert.NotEqual(pair.First.Rise, pair.Second.Rise));
    }

    [Fact]
    public void EmptyInterval_HasNoMoonrise()
    {
        var at = StartOf(new LocalDate(2026, 10, 1));

        Assert.Null(Calculator.FindMoonrise(at, at, NewDelhi));
    }

    private static Instant StartOf(LocalDate date) => date.AtStartOfDayInZone(India).ToInstant();
}
