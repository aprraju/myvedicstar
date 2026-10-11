using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicDay.Festivals.Calculation;
using MyVedicDay.Festivals.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Festivals.Tests.Calculation;

/// <summary>
/// Festival dates against published Drik Panchang dates (New Delhi for North Indian festivals, Chennai and Kochi for
/// solar-calendar festivals), plus the lunar-month, Adhika and Sankranti building blocks.
/// </summary>
public sealed class FestivalCalculatorTests
{
    private static readonly DateTimeZone India = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private static readonly GeoCoordinate NewDelhi = new(28.61, 77.21);
    private static readonly GeoCoordinate Chennai = new(13.08, 80.27);
    private static readonly GeoCoordinate Kochi = new(9.93, 76.27);

    private static readonly ServiceProvider Services = new ServiceCollection().AddFestivalCalculation().BuildServiceProvider();
    private static readonly IFestivalCalculator Calculator = Services.GetRequiredService<IFestivalCalculator>();
    private static readonly ILunisolarCalendar Calendar = Services.GetRequiredService<ILunisolarCalendar>();

    public static TheoryData<string, FestivalRule, string> LunarFestivals() => new()
    {
        { "Ugadi", Tithi(LunarMonth.Chaitra, Panchang.Calculation.Models.Tithi.ShuklaPratipada, ObservanceTime.Sunrise), "2025-03-30" },
        { "Rama Navami", Tithi(LunarMonth.Chaitra, Panchang.Calculation.Models.Tithi.ShuklaNavami, ObservanceTime.Madhyahna), "2025-04-06" },
        { "Raksha Bandhan", Tithi(LunarMonth.Shravana, Panchang.Calculation.Models.Tithi.Purnima, ObservanceTime.Sunrise), "2025-08-09" },
        { "Ganesh Chaturthi", Tithi(LunarMonth.Bhadrapada, Panchang.Calculation.Models.Tithi.ShuklaChaturthi, ObservanceTime.Madhyahna), "2025-08-27" },
        { "Navaratri", Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.ShuklaPratipada, ObservanceTime.Sunrise), "2025-09-22" },
        { "Vijayadashami", Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.ShuklaDashami, ObservanceTime.Aparahna), "2025-10-02" },
        { "Diwali", Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.Amavasya, ObservanceTime.Pradosha), "2025-10-20" },
        { "Maha Shivaratri", Tithi(LunarMonth.Magha, Panchang.Calculation.Models.Tithi.KrishnaChaturdashi, ObservanceTime.Nishita), "2025-02-26" },
        { "Holika Dahan", Tithi(LunarMonth.Phalguna, Panchang.Calculation.Models.Tithi.Purnima, ObservanceTime.Pradosha), "2025-03-13" },
        { "Holi", Tithi(LunarMonth.Phalguna, Panchang.Calculation.Models.Tithi.Purnima, ObservanceTime.Pradosha) with { OffsetDays = 1 }, "2025-03-14" },

        // 2026 has an Adhika Jyeshtha (17 May – 15 June), which moves every later festival by a month.
        { "Raksha Bandhan 2026", Tithi(LunarMonth.Shravana, Panchang.Calculation.Models.Tithi.Purnima, ObservanceTime.Sunrise), "2026-08-28" },
        { "Ganesh Chaturthi 2026", Tithi(LunarMonth.Bhadrapada, Panchang.Calculation.Models.Tithi.ShuklaChaturthi, ObservanceTime.Madhyahna), "2026-09-14" },
        { "Diwali 2026", Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.Amavasya, ObservanceTime.Pradosha), "2026-11-08" },
    };

    [Theory]
    [MemberData(nameof(LunarFestivals))]
    public void LunarFestival_FallsOnThePublishedDate(string festival, FestivalRule rule, string expected)
    {
        var date = LocalDate.FromDateTime(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

        var occurrence = Assert.Single(Calculate(NewDelhi, date.PlusDays(-20), date.PlusDays(20), rule));

        Assert.True(date == occurrence.Date, $"{festival}: expected {date}, got {occurrence.Date}.");
    }

    [Fact]
    public void MakarSankranti_IsOnTheDayOfTheSunEnteringMakara()
    {
        var occurrence = Assert.Single(Calculate(
            Chennai, new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31), new FestivalRule(FestivalBasis.Sankranti, ObservanceTime.Sunset, Sign: Rashi.Makara)));

        Assert.Equal(new LocalDate(2026, 1, 14), occurrence.Date);
        Assert.Equal("Makara", occurrence.BasisKey);
        Assert.Equal(occurrence.BasisStart, occurrence.BasisEnd);
    }

    [Fact]
    public void SankrantiOffset_GivesTheEveAndTheDayAfter()
    {
        var bhogi = new FestivalRule(FestivalBasis.Sankranti, ObservanceTime.Sunset, Sign: Rashi.Makara, OffsetDays: -1);
        var kanuma = bhogi with { OffsetDays = 1 };

        var occurrences = Calculate(Chennai, new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31), bhogi, kanuma);

        Assert.Equal([new LocalDate(2026, 1, 13), new LocalDate(2026, 1, 15)], occurrences.Select(occurrence => occurrence.Date));
        Assert.All(occurrences, occurrence => Assert.Null(occurrence.WindowStart));
    }

    [Fact]
    public void PuthanduAndVishu_AreTheMeshaSankranti()
    {
        var rule = new FestivalRule(FestivalBasis.Sankranti, ObservanceTime.Sunset, Sign: Rashi.Mesha);

        var occurrence = Assert.Single(Calculate(Chennai, new LocalDate(2025, 4, 1), new LocalDate(2025, 4, 30), rule));

        Assert.Equal(new LocalDate(2025, 4, 14), occurrence.Date);
    }

    [Fact]
    public void Onam_IsShravanaNakshatraInTheSolarMonthSimha()
    {
        var rule = new FestivalRule(FestivalBasis.Nakshatra, ObservanceTime.Sunrise, Nakshatra: Nakshatra.Shravana, SolarMonth: Rashi.Simha);

        var occurrence = Assert.Single(Calculate(Kochi, new LocalDate(2025, 8, 1), new LocalDate(2025, 9, 30), rule));

        Assert.Equal(new LocalDate(2025, 9, 5), occurrence.Date);
    }

    [Fact]
    public void MonthlyRule_FindsEveryOccurrence_IncludingInAdhikaMonths()
    {
        var ekadashi = new FestivalRule(FestivalBasis.Tithi, ObservanceTime.Sunrise, Tithi: Panchang.Calculation.Models.Tithi.ShuklaEkadashi);

        // 2026-05-17 to 2026-06-15 is Adhika Jyeshtha; its Shukla Ekadashi is still an Ekadashi.
        var occurrences = Calculate(NewDelhi, new LocalDate(2026, 5, 1), new LocalDate(2026, 7, 31), ekadashi);

        Assert.Equal(3, occurrences.Count);
        Assert.Contains(occurrences, occurrence => occurrence.Date > new LocalDate(2026, 5, 17) && occurrence.Date < new LocalDate(2026, 6, 15));
    }

    [Fact]
    public void ObservanceWindow_IsReportedForTheAnchorDay()
    {
        var rule = Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.Amavasya, ObservanceTime.Pradosha);

        var diwali = Assert.Single(Calculate(NewDelhi, new LocalDate(2025, 10, 1), new LocalDate(2025, 10, 31), rule));

        Assert.NotNull(diwali.WindowStart);
        Assert.True(diwali.WindowEnd > diwali.WindowStart);
        Assert.True(diwali.BasisStart < diwali.WindowEnd && diwali.WindowStart < diwali.BasisEnd, "The Amavasya overlaps the Pradosha window.");
        Assert.Equal("Amavasya", diwali.BasisKey);
    }

    [Theory]
    [InlineData("2026-06-01", LunarMonth.Jyeshtha, true)]
    [InlineData("2026-07-01", LunarMonth.Jyeshtha, false)]
    [InlineData("2026-10-01", LunarMonth.Bhadrapada, false)]
    [InlineData("2025-04-01", LunarMonth.Chaitra, false)]
    public void LunarMonth_IsAmanta_WithAdhikaDetection(string date, LunarMonth expected, bool isAdhika)
    {
        var instant = LocalDate.FromDateTime(DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture)).AtStartOfDayInZone(India).ToInstant();

        var (month, adhika) = Calendar.GetLunarMonth(instant);

        Assert.Equal(expected, month);
        Assert.Equal(isAdhika, adhika);
    }

    [Fact]
    public void Sankrantis_AreTwelveAYear_InSignOrder()
    {
        var from = new LocalDate(2026, 1, 1).AtStartOfDayInZone(India).ToInstant();
        var to = new LocalDate(2027, 1, 1).AtStartOfDayInZone(India).ToInstant();

        var sankrantis = Calendar.FindSankrantis(from, to);

        Assert.Equal(12, sankrantis.Count);
        Assert.Equal(Rashi.Makara, sankrantis[0].Sign);
        Assert.Equal(Rashi.Dhanu, sankrantis[^1].Sign);
        Assert.True(sankrantis.Zip(sankrantis.Skip(1)).All(pair => pair.First.At < pair.Second.At));
    }

    private static FestivalRule Tithi(LunarMonth month, Panchang.Calculation.Models.Tithi tithi, ObservanceTime time) =>
        new(FestivalBasis.Tithi, time, Tithi: tithi, LunarMonth: month);

    private static IReadOnlyList<FestivalOccurrence> Calculate(GeoCoordinate location, LocalDate from, LocalDate to, params FestivalRule[] rules) =>
        Calculator.Calculate(new FestivalCalculationRequest(from, to, location, India, [new FestivalDefinition("Test", rules)]));
}
