using NodaTime;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// Moonrise and moonset against published times, and the structural properties of the search.
/// </summary>
public sealed class MoonEventCalculatorTests
{
    private static readonly DateTimeZone India = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private static readonly GeoCoordinate NewDelhi = new(28.6139, 77.2090);
    private static readonly GeoCoordinate DrikNewDelhi = new(28 + (38 / 60d) + (8 / 3600d), 77 + (13 / 60d) + (28 / 3600d));
    private static readonly IMoonEventCalculator Calculator = new MoonEventCalculator(new MeeusEphemeris());

    /// <summary>
    /// Moonrise in New Delhi (28°38′08″ N, 77°13′28″ E) on the Sankashti Chaturthis of 2025, as published by Drik
    /// Panchang to the minute.
    /// </summary>
    [Theory]
    [InlineData(1, 17, 21, 13)]
    [InlineData(2, 16, 21, 43)]
    [InlineData(3, 17, 21, 22)]
    [InlineData(4, 16, 22, 5)]
    [InlineData(5, 16, 22, 44)]
    [InlineData(6, 14, 22, 12)]
    [InlineData(7, 14, 21, 59)]
    [InlineData(8, 12, 21, 4)]
    [InlineData(9, 10, 20, 11)]
    [InlineData(10, 10, 20, 18)]
    [InlineData(11, 8, 20, 4)]
    [InlineData(12, 7, 20, 0)]
    public void Moonrise_MatchesDrikPanchang(int month, int day, int hour, int minute)
    {
        var date = new LocalDate(2025, month, day);
        var expected = date.At(new LocalTime(hour, minute)).InZoneStrictly(India).ToInstant();

        var moonrise = Calculator.FindMoonrise(StartOf(date), StartOf(date.PlusDays(1)), DrikNewDelhi);

        Assert.NotNull(moonrise);
        var error = moonrise.Value - expected;
        Assert.True(Duration.Max(error, -error) <= Duration.FromMinutes(1), $"Moonrise {moonrise.Value.InZone(India):HH:mm:ss}, expected {hour:00}:{minute:00}.");
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
