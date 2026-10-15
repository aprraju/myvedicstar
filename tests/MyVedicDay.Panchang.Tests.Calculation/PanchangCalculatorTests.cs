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

    /// <summary>
    /// The Drik Panchang day for New Delhi (28°38′08″ N, 77°13′28″ E) on 2025-10-20 (Diwali), whose times are rounded to
    /// the nearest minute: every element end and period boundary must round to the published minute (±1 minute where
    /// the engine lies within a few seconds of a rounding boundary).
    /// </summary>
    [Fact]
    public void Day_MatchesDrikPanchang_NewDelhi_2025_10_20()
    {
        var delhi = new GeoCoordinate(28 + (38 / 60d) + (8 / 3600d), 77 + (13 / 60d) + (28 / 3600d));
        var day = _calculator.Calculate(new PanchangCalculationRequest(new LocalDate(2025, 10, 20), delhi, Kolkata, PanchangTraditions.Drik));
        Instant At(int dayOfMonth, int hour, int minute) => new LocalDateTime(2025, 10, dayOfMonth, hour, minute).InZoneStrictly(Kolkata).ToInstant();
        TimePeriod Period(PanchangPeriodKind kind) => day.Periods.First(period => period.Kind == kind);

        var expectations = new (string Name, Instant Engine, Instant Drik)[]
        {
            ("Sunrise", day.Sun.Sunrise, At(20, 6, 25)),
            ("Sunset", day.Sun.Sunset, At(20, 17, 46)),
            ("Chaturdashi ends", day.Tithis[0].End, At(20, 15, 44)),
            ("Hasta ends", day.Nakshatras[0].End, At(20, 20, 17)),
            ("Vaidhriti ends", day.Yogas[0].End, At(21, 2, 35)),
            ("Shakuni ends", day.Karanas[0].End, At(20, 15, 44)),
            ("Chatushpada ends", day.Karanas[1].End, At(21, 4, 47)),
            ("Brahma Muhurta starts", Period(PanchangPeriodKind.BrahmaMuhurta).Start, At(20, 4, 44)),
            ("Brahma Muhurta ends", Period(PanchangPeriodKind.BrahmaMuhurta).End, At(20, 5, 34)),
            ("Abhijit starts", Period(PanchangPeriodKind.AbhijitMuhurta).Start, At(20, 11, 43)),
            ("Abhijit ends", Period(PanchangPeriodKind.AbhijitMuhurta).End, At(20, 12, 28)),
            ("Rahu Kalam starts", Period(PanchangPeriodKind.RahuKalam).Start, At(20, 7, 50)),
            ("Rahu Kalam ends", Period(PanchangPeriodKind.RahuKalam).End, At(20, 9, 15)),
            ("Yamagandam starts", Period(PanchangPeriodKind.Yamagandam).Start, At(20, 10, 40)),
            ("Yamagandam ends", Period(PanchangPeriodKind.Yamagandam).End, At(20, 12, 6)),
            ("Gulika starts", Period(PanchangPeriodKind.GulikaKalam).Start, At(20, 13, 31)),
            ("Gulika ends", Period(PanchangPeriodKind.GulikaKalam).End, At(20, 14, 56)),
            ("Amrit Kalam starts", Period(PanchangPeriodKind.AmritKalam).Start, At(20, 13, 40)),
            ("Amrit Kalam ends", Period(PanchangPeriodKind.AmritKalam).End, At(20, 15, 26)),
            ("Varjyam starts", Period(PanchangPeriodKind.Varjyam).Start, At(21, 5, 11)),
            ("Varjyam ends", Period(PanchangPeriodKind.Varjyam).End, At(21, 6, 57)),
            ("Dur Muhurtam starts", Period(PanchangPeriodKind.DurMuhurtam).Start, At(20, 12, 28)),
        };

        Assert.All(expectations, expectation =>
        {
            var error = expectation.Engine - expectation.Drik;
            Assert.True(Duration.Max(error, -error) <= Duration.FromSeconds(60), $"{expectation.Name}: engine {expectation.Engine.InZone(Kolkata):HH:mm:ss}, Drik {expectation.Drik.InZone(Kolkata):HH:mm}.");
        });
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
