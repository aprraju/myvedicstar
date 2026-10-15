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

    private static readonly FestivalRule HolikaDahan = Tithi(LunarMonth.Phalguna, Panchang.Calculation.Models.Tithi.Purnima, ObservanceTime.Pradosha)
        with { AvoidBhadra = true, SunriseDayMinimumFraction = 0.75 };

    private static readonly FestivalRule RakshaBandhan = Tithi(LunarMonth.Shravana, Panchang.Calculation.Models.Tithi.Purnima, ObservanceTime.Aparahna)
        with { AvoidBhadra = true, SunriseDayMinimumFraction = 0.2 };

    private static readonly FestivalRule Vijayadashami = Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.ShuklaDashami, ObservanceTime.Aparahna)
        with { SunriseDayMinimumFraction = 0.2 };

    public static TheoryData<string, FestivalRule, string> LunarFestivals() => new()
    {
        { "Ugadi", Tithi(LunarMonth.Chaitra, Panchang.Calculation.Models.Tithi.ShuklaPratipada, ObservanceTime.Sunrise), "2025-03-30" },
        { "Rama Navami", Tithi(LunarMonth.Chaitra, Panchang.Calculation.Models.Tithi.ShuklaNavami, ObservanceTime.Madhyahna), "2025-04-06" },
        { "Raksha Bandhan", RakshaBandhan, "2025-08-09" },
        { "Ganesh Chaturthi", Tithi(LunarMonth.Bhadrapada, Panchang.Calculation.Models.Tithi.ShuklaChaturthi, ObservanceTime.Madhyahna), "2025-08-27" },
        { "Navaratri", Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.ShuklaPratipada, ObservanceTime.Sunrise), "2025-09-22" },
        { "Vijayadashami", Vijayadashami, "2025-10-02" },
        { "Diwali", Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.Amavasya, ObservanceTime.Pradosha), "2025-10-20" },
        { "Maha Shivaratri", Tithi(LunarMonth.Magha, Panchang.Calculation.Models.Tithi.KrishnaChaturdashi, ObservanceTime.Nishita), "2025-02-26" },
        { "Holika Dahan", HolikaDahan, "2025-03-13" },
        { "Holi", HolikaDahan with { OffsetDays = 1 }, "2025-03-14" },

        // Bhadra covers the Pradosha of the day Purnima begins, and Purnima lasts three Praharas into the next day.
        { "Holika Dahan 2023", HolikaDahan, "2023-03-07" },
        { "Holi 2023", HolikaDahan with { OffsetDays = 1 }, "2023-03-08" },
        { "Holika Dahan 2026", HolikaDahan, "2026-03-03" },
        { "Holi 2026", HolikaDahan with { OffsetDays = 1 }, "2026-03-04" },

        // Bhadra covers Purnima's first Aparahna, and Purnima ends within three Muhurtas of the next sunrise.
        { "Raksha Bandhan 2021", RakshaBandhan, "2021-08-22" },
        { "Raksha Bandhan 2022", RakshaBandhan, "2022-08-11" },
        { "Raksha Bandhan 2023", RakshaBandhan, "2023-08-30" },
        { "Raksha Bandhan 2024", RakshaBandhan, "2024-08-19" },

        // Dashami touches only part of one Aparahna, and lasts three Muhurtas past the next sunrise.
        { "Vijayadashami 2022", Vijayadashami, "2022-10-05" },
        { "Vijayadashami 2024", Vijayadashami, "2024-10-12" },

        // Ashtami covers Nishita on both nights: the plain rule keeps the later, longer window (published date).
        { "Krishna Janmashtami 2021", Tithi(LunarMonth.Shravana, Panchang.Calculation.Models.Tithi.KrishnaAshtami, ObservanceTime.Nishita), "2021-08-30" },

        // 2026 has an Adhika Jyeshtha (17 May – 15 June), which moves every later festival by a month.
        { "Raksha Bandhan 2026", RakshaBandhan, "2026-08-28" },
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

    /// <summary>
    /// The Nakshatra occurs twice in the solar month; only the last one is the festival. The requested range holds only
    /// the first, so the engine must look past it to the end of the month.
    /// </summary>
    [Theory]
    [InlineData(Nakshatra.Shravana, Rashi.Simha, ObservanceTime.Sunrise, "2024-08-10", "2024-08-25", null)]
    [InlineData(Nakshatra.Shravana, Rashi.Simha, ObservanceTime.Sunrise, "2024-08-10", "2024-09-30", "2024-09-15")]
    [InlineData(Nakshatra.Krittika, Rashi.Vrischika, ObservanceTime.Pradosha, "2024-11-10", "2024-12-31", "2024-12-13")]
    [InlineData(Nakshatra.Pushya, Rashi.Makara, ObservanceTime.Sunrise, "2025-01-10", "2025-02-28", "2025-02-11")]
    public void LastOccurrenceRule_KeepsOnlyTheMonthsLastNakshatra(
        Nakshatra nakshatra, Rashi solarMonth, ObservanceTime time, string from, string to, string? expected)
    {
        var rule = new FestivalRule(FestivalBasis.Nakshatra, time, Nakshatra: nakshatra, SolarMonth: solarMonth, Occurrence: SolarMonthOccurrence.Last);

        var occurrences = Calculate(Chennai, Parse(from), Parse(to), rule);

        Assert.Equal(expected is null ? [] : [Parse(expected)], occurrences.Select(occurrence => occurrence.Date));
    }

    [Fact]
    public void AllOccurrencesRule_ListsBothNakshatrasInTheMonth()
    {
        var rule = new FestivalRule(FestivalBasis.Nakshatra, ObservanceTime.Sunrise, Nakshatra: Nakshatra.Shravana, SolarMonth: Rashi.Simha);

        var occurrences = Calculate(Kochi, new LocalDate(2024, 8, 10), new LocalDate(2024, 9, 30), rule);

        Assert.Equal(2, occurrences.Count);
    }

    [Fact]
    public void AvoidBhadra_ReportsTheWindowOfTheChosenDay()
    {
        var holika = Assert.Single(Calculate(NewDelhi, new LocalDate(2026, 2, 20), new LocalDate(2026, 3, 20), HolikaDahan));

        Assert.Equal(new LocalDate(2026, 3, 3), holika.Date);
        Assert.NotNull(holika.WindowStart);
        Assert.True(holika.WindowStart > holika.BasisEnd, "Purnima has ended before this Pradosha: the sunrise-day rule chose it.");
    }

    /// <summary>
    /// Sankashti Chaturthi is the day Krishna Chaturthi prevails at moonrise. In March 2025 Chaturthi begins at 19:33 on
    /// the 17th, before that night's moonrise, and ends before the moonrise of the 18th: Pradosha would wrongly pick the 18th.
    /// In October 2025 Chaturthi (9th 22:54 – 10th 19:38) misses both moonrises, so the day it covers most is kept.
    /// </summary>
    [Theory]
    [InlineData("2021-01-31", true)]
    [InlineData("2024-10-20", true)]
    [InlineData("2025-01-17", true)]
    [InlineData("2025-03-17", true)]
    [InlineData("2025-10-10", false)]
    public void SankashtiChaturthi_IsTheDayChaturthiPrevailsAtMoonrise(string expected, bool prevailsAtMoonrise)
    {
        var date = Parse(expected);
        var rule = new FestivalRule(FestivalBasis.Tithi, ObservanceTime.Moonrise, Tithi: Panchang.Calculation.Models.Tithi.KrishnaChaturthi);

        var occurrence = Assert.Single(Calculate(NewDelhi, date.PlusDays(-10), date.PlusDays(10), rule));

        Assert.Equal(date, occurrence.Date);
        Assert.NotNull(occurrence.WindowStart);
        Assert.Equal(occurrence.WindowStart, occurrence.WindowEnd);
        Assert.Equal(prevailsAtMoonrise, occurrence.BasisStart <= occurrence.WindowStart && occurrence.WindowStart < occurrence.BasisEnd);
    }

    /// <summary>
    /// Smarta and Vaishnava (Gauna) Ekadashi in New Delhi, 2025: Nirjala (Ekadashi at both Arunodayas), Devshayani
    /// (one day for both), Prabodhini (Dwadashi ends before the next sunrise, so Smarta moves back a day) and Mokshada
    /// (Ekadashi touches no sunrise; Dashami at the first Arunodaya, so Vaishnava fasts on Dwadashi).
    /// </summary>
    [Theory]
    [InlineData("2025-06-01", "2025-06-06", "2025-06-07")]
    [InlineData("2025-07-01", "2025-07-06", "2025-07-06")]
    [InlineData("2025-10-28", "2025-11-01", "2025-11-02")]
    [InlineData("2025-12-26", "2025-12-30", "2025-12-31")]
    public void Ekadashi_FollowsTheSmartaAndVaishnavaConventions(string from, string smarta, string vaishnava)
    {
        var start = Parse(from);
        var rule = new FestivalRule(FestivalBasis.Tithi, ObservanceTime.Sunrise, Tithi: Panchang.Calculation.Models.Tithi.ShuklaEkadashi);

        var smartaDay = Assert.Single(Calculate(NewDelhi, start, start.PlusDays(8), rule with { Ekadashi = EkadashiConvention.Smarta })).Date;
        var vaishnavaDay = Assert.Single(Calculate(NewDelhi, start, start.PlusDays(8), rule with { Ekadashi = EkadashiConvention.Vaishnava, Time = ObservanceTime.Arunodaya })).Date;

        Assert.Equal((Parse(smarta), Parse(vaishnava)), (smartaDay, vaishnavaDay));
    }

    /// <summary>
    /// Drik Panchang lists Vaikuntha Ekadashi on the Vaishnava day: 10 January and 31 December 2025 in New Delhi
    /// (Smarta Ekadashi 30 December), 9 January and 30 December 2025 in Georgetown, Texas.
    /// </summary>
    [Theory]
    [InlineData("Asia/Kolkata", 28.6356, 77.2244, "2025-01-01", "2025-01-10")]
    [InlineData("Asia/Kolkata", 28.6356, 77.2244, "2025-12-20", "2025-12-31")]
    [InlineData("America/Chicago", 30.6327, -97.6772, "2025-01-01", "2025-01-09")]
    [InlineData("America/Chicago", 30.6327, -97.6772, "2025-12-20", "2025-12-30")]
    public void VaikunthaEkadashi_IsTheVaishnavaEkadashiInDhanu(string zone, double latitude, double longitude, string from, string expected)
    {
        var rule = new FestivalRule(
            FestivalBasis.Tithi, ObservanceTime.Arunodaya, Tithi: Panchang.Calculation.Models.Tithi.ShuklaEkadashi, SolarMonth: Rashi.Dhanu,
            Ekadashi: EkadashiConvention.Vaishnava);

        var occurrences = Calculator.Calculate(new FestivalCalculationRequest(
            Parse(from), Parse(from).PlusDays(15), new GeoCoordinate(latitude, longitude), DateTimeZoneProviders.Tzdb[zone], [new FestivalDefinition("Test", [rule])]));

        Assert.Equal(Parse(expected), Assert.Single(occurrences).Date);
    }

    /// <summary>
    /// Drik Panchang's complete 2025 lists for New Delhi: 25 Smarta Ekadashis, the 5 Gauna (Vaishnava) dates that
    /// differ from them, and 12 Sankashti Chaturthis.
    /// </summary>
    [Fact]
    public void Year2025_EkadashiAndSankashti_MatchDrikPanchang_NewDelhi()
    {
        var delhi = new GeoCoordinate(28.6356, 77.2244);
        LocalDate[] Dates(params string[] dates) => dates.Select(Parse).ToArray();
        var ekadashi = new[] { Panchang.Calculation.Models.Tithi.ShuklaEkadashi, Panchang.Calculation.Models.Tithi.KrishnaEkadashi };
        FestivalDefinition Festival(string key, Func<Panchang.Calculation.Models.Tithi, FestivalRule> rule, params Panchang.Calculation.Models.Tithi[] tithis) =>
            new(key, tithis.Select(rule).ToArray());

        var occurrences = Calculator.Calculate(new FestivalCalculationRequest(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31), delhi, India,
        [
            Festival("Smarta", tithi => new FestivalRule(FestivalBasis.Tithi, ObservanceTime.Sunrise, Tithi: tithi, Ekadashi: EkadashiConvention.Smarta), ekadashi),
            Festival("Vaishnava", tithi => new FestivalRule(FestivalBasis.Tithi, ObservanceTime.Arunodaya, Tithi: tithi, Ekadashi: EkadashiConvention.Vaishnava), ekadashi),
            Festival("Sankashti", tithi => new FestivalRule(FestivalBasis.Tithi, ObservanceTime.Moonrise, Tithi: tithi), Panchang.Calculation.Models.Tithi.KrishnaChaturthi),
        ]));
        LocalDate[] Of(string key) => occurrences.Where(occurrence => occurrence.Key == key).Select(occurrence => occurrence.Date).ToArray();

        var smarta = Of("Smarta");
        Assert.Equal(
            Dates("2025-01-10", "2025-01-25", "2025-02-08", "2025-02-24", "2025-03-10", "2025-03-25", "2025-04-08", "2025-04-24", "2025-05-08",
                "2025-05-23", "2025-06-06", "2025-06-21", "2025-07-06", "2025-07-21", "2025-08-05", "2025-08-19", "2025-09-03", "2025-09-17",
                "2025-10-03", "2025-10-17", "2025-11-01", "2025-11-15", "2025-12-01", "2025-12-15", "2025-12-30"),
            smarta);
        Assert.Equal(Dates("2025-03-26", "2025-06-07", "2025-06-22", "2025-11-02", "2025-12-31"), Of("Vaishnava").Except(smarta));
        Assert.Equal(
            Dates("2025-01-17", "2025-02-16", "2025-03-17", "2025-04-16", "2025-05-16", "2025-06-14", "2025-07-14", "2025-08-12", "2025-09-10",
                "2025-10-10", "2025-11-08", "2025-12-07"),
            Of("Sankashti"));
    }

    [Theory]
    [InlineData("2021-10-24")]
    [InlineData("2022-10-13")]
    [InlineData("2023-11-01")]
    [InlineData("2024-10-20")]
    [InlineData("2025-10-10")]
    public void KarwaChauth_FallsOnThePublishedDate(string expected)
    {
        var date = Parse(expected);
        var rule = Tithi(LunarMonth.Ashwin, Panchang.Calculation.Models.Tithi.KrishnaChaturthi, ObservanceTime.Moonrise);

        Assert.Equal(date, Assert.Single(Calculate(NewDelhi, date.PlusDays(-20), date.PlusDays(20), rule)).Date);
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

    private static LocalDate Parse(string date) =>
        LocalDate.FromDateTime(DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

    private static FestivalRule Tithi(LunarMonth month, Panchang.Calculation.Models.Tithi tithi, ObservanceTime time) =>
        new(FestivalBasis.Tithi, time, Tithi: tithi, LunarMonth: month);

    private static IReadOnlyList<FestivalOccurrence> Calculate(GeoCoordinate location, LocalDate from, LocalDate to, params FestivalRule[] rules) =>
        Calculator.Calculate(new FestivalCalculationRequest(from, to, location, India, [new FestivalDefinition("Test", rules)]));
}
