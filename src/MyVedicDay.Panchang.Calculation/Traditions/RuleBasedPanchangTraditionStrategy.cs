using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Panchang.Calculation.Traditions;

/// <summary>
/// Derives the tradition's time periods from its rule tables:
/// Rahu Kalam, Yamagandam and Gulika Kalam are eighths of the daytime; Muhurtas are fifteenths of the day or night;
/// Varjyam and Amrit Kalam are fixed ghati offsets within each Nakshatra; Choghadiya divides day and night into
/// eight segments each, following weekday-dependent sequences.
/// </summary>
internal abstract class RuleBasedPanchangTraditionStrategy(PanchangTraditionRules rules, IAyanamsa ayanamsa)
    : IPanchangTraditionStrategy
{
    private const int DaySegments = 8;
    private const int MuhurtasPerHalf = 15;
    private const int AbhijitMuhurta = 8;
    private const int BrahmaMuhurta = 14;
    private const double GhatisPerNakshatra = 60d;

    public string Key => rules.Key;

    public SunriseDefinition SunriseDefinition => rules.SunriseDefinition;

    public IAyanamsa Ayanamsa => ayanamsa;

    public IReadOnlyList<TimePeriod> CalculatePeriods(PanchangDayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sun = context.Sun;
        var periods = new List<TimePeriod>
        {
            // Brahma Muhurta is the 14th Muhurta of the night that ends at this sunrise.
            CreateSegment(PanchangPeriodKind.BrahmaMuhurta, PeriodNature.Auspicious, sun.PreviousSunset, sun.Sunrise, MuhurtasPerHalf, BrahmaMuhurta),
            CreateSegment(PanchangPeriodKind.RahuKalam, PeriodNature.Inauspicious, sun.Sunrise, sun.Sunset, DaySegments, rules.RahuKalamSegments[context.Weekday]),
            CreateSegment(PanchangPeriodKind.Yamagandam, PeriodNature.Inauspicious, sun.Sunrise, sun.Sunset, DaySegments, rules.YamagandamSegments[context.Weekday]),
            CreateSegment(PanchangPeriodKind.GulikaKalam, PeriodNature.Inauspicious, sun.Sunrise, sun.Sunset, DaySegments, rules.GulikaKalamSegments[context.Weekday]),
        };

        if (!rules.AbhijitExcludedWeekdays.Contains(context.Weekday))
        {
            periods.Add(CreateSegment(PanchangPeriodKind.AbhijitMuhurta, PeriodNature.Auspicious, sun.Sunrise, sun.Sunset, MuhurtasPerHalf, AbhijitMuhurta));
        }

        foreach (var slot in rules.DurMuhurtamSlots[context.Weekday])
        {
            var (start, end) = slot.Half == DayHalf.Day ? (sun.Sunrise, sun.Sunset) : (sun.Sunset, sun.NextSunrise);
            periods.Add(CreateSegment(PanchangPeriodKind.DurMuhurtam, PeriodNature.Inauspicious, start, end, MuhurtasPerHalf, slot.Number));
        }

        foreach (var nakshatra in context.Nakshatras)
        {
            var index = (int)nakshatra.Element - 1;
            AddIfWithinDay(periods, CreateNakshatraPortion(PanchangPeriodKind.Varjyam, PeriodNature.Inauspicious, nakshatra, rules.VarjyamStartGhatis[index]), sun);
            AddIfWithinDay(periods, CreateNakshatraPortion(PanchangPeriodKind.AmritKalam, PeriodNature.Auspicious, nakshatra, rules.AmritKalamStartGhatis[index]), sun);
        }

        AddChoghadiya(periods, sun.Sunrise, sun.Sunset, rules.ChoghadiyaDaySequence, rules.ChoghadiyaDayStart[context.Weekday]);
        AddChoghadiya(periods, sun.Sunset, sun.NextSunrise, rules.ChoghadiyaNightSequence, rules.ChoghadiyaNightStart[context.Weekday]);

        return periods.OrderBy(period => period.Start).ThenBy(period => period.Group).ToArray();
    }

    private static TimePeriod CreateSegment(
        PanchangPeriodKind kind, PeriodNature nature, Instant start, Instant end, int segmentCount, int segmentNumber,
        PeriodGroup group = PeriodGroup.Muhurta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(segmentNumber, segmentCount);

        var length = (end - start) / segmentCount;
        var segmentStart = start + (length * (segmentNumber - 1));
        return new TimePeriod(kind, nature, group, segmentStart, segmentStart + length);
    }

    /// <summary>
    /// Eight consecutive segments between <paramref name="start"/> and <paramref name="end"/>, beginning with the
    /// weekday's starting Choghadiya and continuing cyclically through <paramref name="sequence"/>.
    /// </summary>
    private static void AddChoghadiya(
        List<TimePeriod> periods, Instant start, Instant end, IReadOnlyList<PanchangPeriodKind> sequence, PanchangPeriodKind first)
    {
        var offset = IndexOf(sequence, first);
        for (var segment = 0; segment < DaySegments; segment++)
        {
            var kind = sequence[(offset + segment) % sequence.Count];
            periods.Add(CreateSegment(kind, ChoghadiyaNature(kind), start, end, DaySegments, segment + 1, PeriodGroup.Choghadiya));
        }
    }

    private static int IndexOf(IReadOnlyList<PanchangPeriodKind> sequence, PanchangPeriodKind kind)
    {
        for (var i = 0; i < sequence.Count; i++)
        {
            if (sequence[i] == kind)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Choghadiya is not part of the sequence.");
    }

    private static PeriodNature ChoghadiyaNature(PanchangPeriodKind kind) => kind switch
    {
        PanchangPeriodKind.ChoghadiyaAmrit or PanchangPeriodKind.ChoghadiyaShubh or PanchangPeriodKind.ChoghadiyaLabh => PeriodNature.Auspicious,
        PanchangPeriodKind.ChoghadiyaChal => PeriodNature.Neutral,
        _ => PeriodNature.Inauspicious,
    };

    private TimePeriod CreateNakshatraPortion(
        PanchangPeriodKind kind, PeriodNature nature, ElementPeriod<Nakshatra> nakshatra, double startGhati)
    {
        var ghati = (nakshatra.End - nakshatra.Start) / GhatisPerNakshatra;
        var start = nakshatra.Start + (ghati * startGhati);
        return new TimePeriod(kind, nature, PeriodGroup.Muhurta, start, start + (ghati * rules.NakshatraSubPeriodGhatis));
    }

    /// <summary>
    /// Keeps a Nakshatra-based period when it overlaps the Panchang day (sunrise to next sunrise). The full period is
    /// kept, so a Varjyam that crosses sunrise or midnight is reported with its true start and end.
    /// </summary>
    private static void AddIfWithinDay(List<TimePeriod> periods, TimePeriod period, SunEvents sun)
    {
        if (period.End > sun.Sunrise && period.Start < sun.NextSunrise)
        {
            periods.Add(period);
        }
    }
}
