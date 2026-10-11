using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Panchang.Tests.Calculation;

/// <summary>
/// End-to-end checks of the calculation engine against known astronomical events and structural invariants.
/// </summary>
public sealed class PanchangCalculatorTests
{
    private static readonly GeoCoordinate Hyderabad = new(17.385, 78.4867);
    private static readonly DateTimeZone Kolkata = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private static readonly Duration Tolerance = Duration.FromMinutes(2);

    private readonly IPanchangCalculator _calculator = new ServiceCollection()
        .AddPanchangCalculation()
        .BuildServiceProvider()
        .GetRequiredService<IPanchangCalculator>();

    [Fact]
    public void ShuklaPratipada_StartsAtNewMoon()
    {
        // New moon (total solar eclipse): 2024-04-08 18:21 UTC.
        var day = Calculate(new LocalDate(2024, 4, 8));

        var pratipada = Assert.Single(day.Tithis, period => period.Element == Tithi.ShuklaPratipada);
        AssertClose(Instant.FromUtc(2024, 4, 8, 18, 21), pratipada.Start);
        Assert.Equal(Tithi.Amavasya, day.Tithis[^2].Element);
    }

    [Fact]
    public void KrishnaPratipada_StartsAtFullMoon()
    {
        // Full moon: 2024-04-23 23:49 UTC.
        var day = Calculate(new LocalDate(2024, 4, 23));

        var pratipada = Assert.Single(day.Tithis, period => period.Element == Tithi.KrishnaPratipada);
        AssertClose(Instant.FromUtc(2024, 4, 23, 23, 49), pratipada.Start);
    }

    [Theory]
    [InlineData(2024, 1, 1)]
    [InlineData(2025, 6, 15)]
    [InlineData(2026, 9, 28)]
    [InlineData(2026, 12, 31)]
    public void ElementPeriods_AreContiguousAndCoverThePanchangDay(int year, int month, int dayOfMonth)
    {
        var day = Calculate(new LocalDate(year, month, dayOfMonth));

        AssertCoversDay(day.Tithis, day.Sun, 30, Duration.FromHours(19), Duration.FromHours(27));
        AssertCoversDay(day.Nakshatras, day.Sun, 27, Duration.FromHours(19), Duration.FromHours(28));
        AssertCoversDay(day.Yogas, day.Sun, 27, Duration.FromHours(19), Duration.FromHours(27));
        AssertCoversDay(day.Karanas, day.Sun, 11, Duration.FromHours(9), Duration.FromHours(14));
    }

    [Fact]
    public void RahuKalam_OnMonday_IsSecondEighthOfDaytime()
    {
        var day = Calculate(new LocalDate(2026, 9, 28));
        Assert.Equal(IsoDayOfWeek.Monday, day.Weekday);

        var eighth = (day.Sun.Sunset - day.Sun.Sunrise) / 8;
        var rahu = Assert.Single(day.Periods, period => period.Kind == PanchangPeriodKind.RahuKalam);

        Assert.Equal(PeriodNature.Inauspicious, rahu.Nature);
        Assert.Equal(day.Sun.Sunrise + eighth, rahu.Start);
        Assert.Equal(day.Sun.Sunrise + (eighth * 2), rahu.End);
    }

    [Fact]
    public void DurMuhurtam_OnTuesday_IncludesANightMuhurta()
    {
        var day = Calculate(new LocalDate(2026, 9, 29));
        Assert.Equal(IsoDayOfWeek.Tuesday, day.Weekday);

        var durMuhurtams = day.Periods.Where(period => period.Kind == PanchangPeriodKind.DurMuhurtam).ToArray();

        Assert.Equal(2, durMuhurtams.Length);
        Assert.Contains(durMuhurtams, period => period.Start >= day.Sun.Sunset && period.End <= day.Sun.NextSunrise);
    }

    [Fact]
    public void AbhijitMuhurat_IsOmittedOnWednesday()
    {
        var wednesday = Calculate(new LocalDate(2026, 9, 30));
        var thursday = Calculate(new LocalDate(2026, 10, 1));

        Assert.DoesNotContain(wednesday.Periods, period => period.Kind == PanchangPeriodKind.AbhijitMuhurta);
        Assert.Contains(thursday.Periods, period => period.Kind == PanchangPeriodKind.AbhijitMuhurta);
    }

    [Fact]
    public void Varjyam_LiesWithinItsNakshatraAndOverlapsThePanchangDay()
    {
        for (var date = new LocalDate(2026, 1, 1); date < new LocalDate(2026, 1, 31); date = date.PlusDays(1))
        {
            var day = Calculate(date);

            foreach (var varjyam in day.Periods.Where(period => period.Kind == PanchangPeriodKind.Varjyam))
            {
                Assert.Contains(day.Nakshatras, nakshatra => nakshatra.Start <= varjyam.Start && varjyam.End <= nakshatra.End);
                Assert.True(varjyam.End > day.Sun.Sunrise && varjyam.Start < day.Sun.NextSunrise);
            }
        }
    }

    [Theory]
    [InlineData(2026, 9, 27, PanchangPeriodKind.ChoghadiyaUdveg, PanchangPeriodKind.ChoghadiyaShubh)] // Sunday
    [InlineData(2026, 9, 28, PanchangPeriodKind.ChoghadiyaAmrit, PanchangPeriodKind.ChoghadiyaChal)] // Monday
    [InlineData(2026, 10, 3, PanchangPeriodKind.ChoghadiyaKaal, PanchangPeriodKind.ChoghadiyaLabh)] // Saturday
    public void Choghadiya_StartsWithTheWeekdaysSegment(int year, int month, int dayOfMonth, PanchangPeriodKind firstDay, PanchangPeriodKind firstNight)
    {
        var day = Calculate(new LocalDate(year, month, dayOfMonth));
        var choghadiya = day.Periods.Where(period => period.Group == PeriodGroup.Choghadiya).ToArray();

        Assert.Equal(firstDay, choghadiya.Single(period => period.Start == day.Sun.Sunrise).Kind);
        Assert.Equal(firstNight, choghadiya.Single(period => period.Start == day.Sun.Sunset).Kind);
    }

    [Fact]
    public void Choghadiya_SundayDaySequence_MatchesTheTraditionalOrder()
    {
        var day = Calculate(new LocalDate(2026, 9, 27));

        var daytime = day.Periods
            .Where(period => period.Group == PeriodGroup.Choghadiya && period.Start < day.Sun.Sunset)
            .Select(period => period.Kind);

        Assert.Equal(
            [
                PanchangPeriodKind.ChoghadiyaUdveg, PanchangPeriodKind.ChoghadiyaChal, PanchangPeriodKind.ChoghadiyaLabh,
                PanchangPeriodKind.ChoghadiyaAmrit, PanchangPeriodKind.ChoghadiyaKaal, PanchangPeriodKind.ChoghadiyaShubh,
                PanchangPeriodKind.ChoghadiyaRog, PanchangPeriodKind.ChoghadiyaUdveg,
            ],
            daytime);
    }

    [Fact]
    public void Choghadiya_SixteenContiguousSegmentsCoverDayAndNight()
    {
        var day = Calculate(new LocalDate(2026, 9, 29));
        var choghadiya = day.Periods.Where(period => period.Group == PeriodGroup.Choghadiya).ToArray();

        Assert.Equal(16, choghadiya.Length);
        Assert.Equal(day.Sun.Sunrise, choghadiya[0].Start);
        Assert.True(Duration.Max(choghadiya[^1].End - day.Sun.NextSunrise, day.Sun.NextSunrise - choghadiya[^1].End) < Duration.FromMilliseconds(1));
        for (var i = 1; i < choghadiya.Length; i++)
        {
            Assert.True(Duration.Max(choghadiya[i].Start - choghadiya[i - 1].End, choghadiya[i - 1].End - choghadiya[i].Start) < Duration.FromMilliseconds(1));
        }

        Assert.Equal(PeriodNature.Neutral, choghadiya.First(period => period.Kind == PanchangPeriodKind.ChoghadiyaChal).Nature);
    }

    [Fact]
    public void SameDate_DiffersByLocation()
    {
        var date = new LocalDate(2026, 9, 28);
        var austin = _calculator.Calculate(new PanchangCalculationRequest(
            date, new GeoCoordinate(30.2672, -97.7431), DateTimeZoneProviders.Tzdb["America/Chicago"], PanchangTraditions.Drik));
        var sanJose = _calculator.Calculate(new PanchangCalculationRequest(
            date, new GeoCoordinate(37.3382, -121.8863), DateTimeZoneProviders.Tzdb["America/Los_Angeles"], PanchangTraditions.Drik));

        Assert.NotEqual(austin.Sun.Sunrise, sanJose.Sun.Sunrise);

        // Tithi transitions are global instants: the same transition occurs at the same instant everywhere.
        Assert.Contains(austin.Tithis, tithi => sanJose.Tithis.Any(other => other.Element == tithi.Element && other.End == tithi.End));
    }

    private PanchangDay Calculate(LocalDate date) =>
        _calculator.Calculate(new PanchangCalculationRequest(date, Hyderabad, Kolkata, PanchangTraditions.Drik));

    private static void AssertCoversDay<TElement>(
        IReadOnlyList<ElementPeriod<TElement>> periods, SunEvents sun, int elementCount, Duration minLength, Duration maxLength)
        where TElement : struct, Enum
    {
        Assert.NotEmpty(periods);
        Assert.True(periods[0].Start <= sun.Sunrise, "First period must be the one prevailing at sunrise.");
        Assert.True(periods[^1].End >= sun.NextSunrise, "Last period must extend to the next sunrise.");

        for (var i = 0; i < periods.Count; i++)
        {
            var length = periods[i].End - periods[i].Start;
            Assert.InRange(length, minLength, maxLength);

            if (i > 0)
            {
                Assert.Equal(periods[i - 1].End, periods[i].Start);
                Assert.NotEqual(periods[i - 1].Element, periods[i].Element);
                Assert.InRange(Convert.ToInt32(periods[i].Element, System.Globalization.CultureInfo.InvariantCulture), 1, elementCount);
            }
        }
    }

    private static void AssertClose(Instant expected, Instant actual)
    {
        var difference = actual - expected;
        Assert.True(
            Duration.Negate(Tolerance) <= difference && difference <= Tolerance,
            $"Expected {expected} but was {actual} (difference {difference.TotalSeconds:0} s).");
    }
}
