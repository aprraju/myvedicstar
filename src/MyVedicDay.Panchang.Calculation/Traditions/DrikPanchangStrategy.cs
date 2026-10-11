using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Panchang.Calculation.Traditions;

/// <summary>
/// Drik (observational) Panchang: Lahiri ayanamsa, upper-limb refracted sunrise and the commonly published
/// rule tables. The Varjyam and Amrit Kalam ghati tables differ between regional almanacs and are pending
/// validation against the chosen reference Panchang (see docs/architecture).
/// </summary>
internal sealed class DrikPanchangStrategy(LahiriAyanamsa ayanamsa)
    : RuleBasedPanchangTraditionStrategy(Rules, ayanamsa)
{
    internal static PanchangTraditionRules Rules { get; } = new()
    {
        Key = PanchangTraditions.Drik,
        SunriseDefinition = SunriseDefinition.UpperLimbWithRefraction,
        RahuKalamSegments = ByWeekday(sunday: 8, monday: 2, tuesday: 7, wednesday: 5, thursday: 6, friday: 4, saturday: 3),
        YamagandamSegments = ByWeekday(sunday: 5, monday: 4, tuesday: 3, wednesday: 2, thursday: 1, friday: 7, saturday: 6),
        GulikaKalamSegments = ByWeekday(sunday: 7, monday: 6, tuesday: 5, wednesday: 4, thursday: 3, friday: 2, saturday: 1),
        DurMuhurtamSlots = new Dictionary<IsoDayOfWeek, IReadOnlyList<MuhurtaSlot>>
        {
            [IsoDayOfWeek.Sunday] = [Day(14)],
            [IsoDayOfWeek.Monday] = [Day(9), Day(12)],
            [IsoDayOfWeek.Tuesday] = [Day(4), Night(7)],
            [IsoDayOfWeek.Wednesday] = [Day(8)],
            [IsoDayOfWeek.Thursday] = [Day(6), Day(12)],
            [IsoDayOfWeek.Friday] = [Day(4), Day(9)],
            [IsoDayOfWeek.Saturday] = [Day(1), Day(2)],
        },
        AbhijitExcludedWeekdays = new HashSet<IsoDayOfWeek> { IsoDayOfWeek.Wednesday },
        VarjyamStartGhatis = [50, 24, 30, 40, 14, 11, 30, 20, 32, 30, 20, 18, 21, 20, 14, 14, 10, 14, 20, 24, 20, 10, 10, 18, 16, 24, 30],
        AmritKalamStartGhatis = [42, 48, 54, 52, 38, 35, 54, 44, 56, 54, 44, 42, 45, 44, 38, 38, 34, 38, 44, 48, 44, 34, 34, 42, 40, 48, 54],
        NakshatraSubPeriodGhatis = 4,
        ChoghadiyaDaySequence =
        [
            PanchangPeriodKind.ChoghadiyaUdveg, PanchangPeriodKind.ChoghadiyaChal, PanchangPeriodKind.ChoghadiyaLabh,
            PanchangPeriodKind.ChoghadiyaAmrit, PanchangPeriodKind.ChoghadiyaKaal, PanchangPeriodKind.ChoghadiyaShubh,
            PanchangPeriodKind.ChoghadiyaRog,
        ],
        ChoghadiyaNightSequence =
        [
            PanchangPeriodKind.ChoghadiyaShubh, PanchangPeriodKind.ChoghadiyaAmrit, PanchangPeriodKind.ChoghadiyaChal,
            PanchangPeriodKind.ChoghadiyaRog, PanchangPeriodKind.ChoghadiyaKaal, PanchangPeriodKind.ChoghadiyaLabh,
            PanchangPeriodKind.ChoghadiyaUdveg,
        ],
        ChoghadiyaDayStart = new Dictionary<IsoDayOfWeek, PanchangPeriodKind>
        {
            [IsoDayOfWeek.Sunday] = PanchangPeriodKind.ChoghadiyaUdveg,
            [IsoDayOfWeek.Monday] = PanchangPeriodKind.ChoghadiyaAmrit,
            [IsoDayOfWeek.Tuesday] = PanchangPeriodKind.ChoghadiyaRog,
            [IsoDayOfWeek.Wednesday] = PanchangPeriodKind.ChoghadiyaLabh,
            [IsoDayOfWeek.Thursday] = PanchangPeriodKind.ChoghadiyaShubh,
            [IsoDayOfWeek.Friday] = PanchangPeriodKind.ChoghadiyaChal,
            [IsoDayOfWeek.Saturday] = PanchangPeriodKind.ChoghadiyaKaal,
        },
        ChoghadiyaNightStart = new Dictionary<IsoDayOfWeek, PanchangPeriodKind>
        {
            [IsoDayOfWeek.Sunday] = PanchangPeriodKind.ChoghadiyaShubh,
            [IsoDayOfWeek.Monday] = PanchangPeriodKind.ChoghadiyaChal,
            [IsoDayOfWeek.Tuesday] = PanchangPeriodKind.ChoghadiyaKaal,
            [IsoDayOfWeek.Wednesday] = PanchangPeriodKind.ChoghadiyaUdveg,
            [IsoDayOfWeek.Thursday] = PanchangPeriodKind.ChoghadiyaAmrit,
            [IsoDayOfWeek.Friday] = PanchangPeriodKind.ChoghadiyaRog,
            [IsoDayOfWeek.Saturday] = PanchangPeriodKind.ChoghadiyaLabh,
        },
    };

    private static MuhurtaSlot Day(int number) => new(DayHalf.Day, number);

    private static MuhurtaSlot Night(int number) => new(DayHalf.Night, number);

    private static Dictionary<IsoDayOfWeek, int> ByWeekday(
        int sunday, int monday, int tuesday, int wednesday, int thursday, int friday, int saturday) => new()
    {
        [IsoDayOfWeek.Sunday] = sunday,
        [IsoDayOfWeek.Monday] = monday,
        [IsoDayOfWeek.Tuesday] = tuesday,
        [IsoDayOfWeek.Wednesday] = wednesday,
        [IsoDayOfWeek.Thursday] = thursday,
        [IsoDayOfWeek.Friday] = friday,
        [IsoDayOfWeek.Saturday] = saturday,
    };
}
