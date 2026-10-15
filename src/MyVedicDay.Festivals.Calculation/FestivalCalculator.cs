using MyVedicDay.Astronomy;
using MyVedicDay.Festivals.Calculation.Models;
using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Festivals.Calculation;

/// <summary>
/// Evaluates festival rules over the Panchang days of a date range (plus a margin, so festivals near the edges are
/// assigned with full knowledge of the neighbouring days).
/// </summary>
internal sealed class FestivalCalculator(IPanchangCalculator panchang, ILunisolarCalendar calendar, IMoonEventCalculator moonEvents)
    : IFestivalCalculator
{
    // A Tithi or Nakshatra spans at most two sunrises; the margin also covers the largest rule offset.
    private const int BaseMarginDays = 2;

    // A solar month lasts at most 32 days: a "last occurrence" rule may need to see the rest of the month.
    private const int SolarMonthMarginDays = 32;

    public IReadOnlyList<FestivalOccurrence> Calculate(FestivalCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.To < request.From)
        {
            throw new ArgumentException("The end date precedes the start date.", nameof(request));
        }

        var margin = BaseMarginDays + request.Festivals
            .SelectMany(festival => festival.Rules)
            .Select(rule => Math.Abs(rule.OffsetDays))
            .DefaultIfEmpty(0)
            .Max();

        var days = CalculateDays(request, request.From.PlusDays(-margin), request.To.PlusDays(margin));
        var occurrences = Evaluate(request, days);

        // A "last occurrence" festival in the range may be followed by a later one in the same solar month. Only then is
        // the rest of the month calculated and the range evaluated again.
        var lastOccurrenceFestivals = request.Festivals
            .Where(festival => festival.Rules.Any(rule => rule.Occurrence == SolarMonthOccurrence.Last))
            .Select(festival => festival.Key)
            .ToHashSet(StringComparer.Ordinal);
        var monthEnd = request.To.PlusDays(Math.Max(margin, SolarMonthMarginDays));
        if (occurrences.Any(occurrence => lastOccurrenceFestivals.Contains(occurrence.Key)) && monthEnd > request.To.PlusDays(margin))
        {
            days = [.. days, .. CalculateDays(request, request.To.PlusDays(margin + 1), monthEnd)];
            occurrences = Evaluate(request, days);
        }

        return occurrences;
    }

    private IReadOnlyList<FestivalOccurrence> Evaluate(FestivalCalculationRequest request, PanchangDay[] days)
    {
        var context = new EvaluationContext(
            days,
            days.SelectMany(day => day.Tithis).DistinctBy(period => period.Start).ToArray(),
            days.SelectMany(day => day.Nakshatras).DistinctBy(period => period.Start).ToArray(),
            days.SelectMany(day => day.Karanas)
                .Where(period => period.Element == Karana.Vishti)
                .DistinctBy(period => period.Start)
                .Select(period => (period.Start, period.End))
                .ToArray(),
            calendar.FindSankrantis(days[0].Sun.Sunrise, days[^1].Sun.NextSunrise),
            new MoonriseCache(
                moonEvents,
                request.Location,
                request.Festivals.SelectMany(festival => festival.Rules).Any(rule => rule.Time == ObservanceTime.Moonrise)),
            new Dictionary<(Instant Start, Instant End), (LunarMonth Month, bool IsAdhika)>());

        return request.Festivals
            .SelectMany(festival => festival.Rules.SelectMany(rule => Evaluate(festival.Key, rule, context)))
            .Where(occurrence => occurrence.Date >= request.From && occurrence.Date <= request.To)
            .DistinctBy(occurrence => (occurrence.Key, occurrence.Date))
            .OrderBy(occurrence => occurrence.Date)
            .ToArray();
    }

    /// <summary>The Panchang days from <paramref name="first"/> to <paramref name="last"/>, inclusive.</summary>
    private PanchangDay[] CalculateDays(FestivalCalculationRequest request, LocalDate first, LocalDate last)
    {
        var count = Period.Between(first, last, PeriodUnits.Days).Days + 1;

        return Enumerable.Range(0, count)
            .Select(offset => panchang.Calculate(new PanchangCalculationRequest(
                first.PlusDays(offset), request.Location, request.TimeZone, PanchangTraditions.Drik)))
            .ToArray();
    }

    private IEnumerable<FestivalOccurrence> Evaluate(string key, FestivalRule rule, EvaluationContext context) =>
        rule.Basis switch
        {
            FestivalBasis.Tithi => KeepLastInSolarMonth(rule, context, EvaluateElement(key, rule, context, context.Tithis, period => period.Element == rule.Tithi)),
            FestivalBasis.Nakshatra => KeepLastInSolarMonth(rule, context, EvaluateElement(key, rule, context, context.Nakshatras, period => period.Element == rule.Nakshatra)),
            FestivalBasis.Sankranti => EvaluateSankranti(key, rule, context),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Basis, "Unknown festival basis."),
        };

    /// <summary>
    /// For <see cref="SolarMonthOccurrence.Last"/> rules, keeps only the latest occurrence within each solar month
    /// (months are told apart by the Sankranti into the rule's sign that starts them).
    /// </summary>
    private static IEnumerable<FestivalOccurrence> KeepLastInSolarMonth(
        FestivalRule rule, EvaluationContext context, IEnumerable<FestivalOccurrence> occurrences)
    {
        if (rule.SolarMonth is not { } sign || rule.Occurrence != SolarMonthOccurrence.Last)
        {
            return occurrences;
        }

        return occurrences
            .GroupBy(occurrence => context.Sankrantis.LastOrDefault(sankranti => sankranti.Sign == sign && sankranti.At <= occurrence.BasisEnd).At)
            .Select(month => month.MaxBy(occurrence => occurrence.Date)!);
    }

    private IEnumerable<FestivalOccurrence> EvaluateElement<TElement>(
        string key,
        FestivalRule rule,
        EvaluationContext context,
        IReadOnlyList<ElementPeriod<TElement>> periods,
        Func<ElementPeriod<TElement>, bool> matches)
        where TElement : struct, Enum
    {
        foreach (var period in periods.Where(matches))
        {
            if (rule.LunarMonth is { } lunarMonth)
            {
                var (month, isAdhika) = LunarMonthOf(context, period.Start, period.End);
                if (isAdhika || month != lunarMonth)
                {
                    continue;
                }
            }

            var day = SelectDay(context, rule, period.Start, period.End);
            if (rule.SolarMonth is { } solarMonth && calendar.GetSunSign(day.Sun.Sunset) != solarMonth)
            {
                continue;
            }

            yield return CreateOccurrence(context, key, rule, day, period.Element.ToString(), period.Start, period.End);
        }
    }

    /// <summary>
    /// The lunar month of a Tithi or Nakshatra period (at its midpoint), remembered for the evaluation: many rules share
    /// a period (every Purnima festival), and each lookup searches for two new moons.
    /// </summary>
    private (LunarMonth Month, bool IsAdhika) LunarMonthOf(EvaluationContext context, Instant start, Instant end)
    {
        if (!context.LunarMonths.TryGetValue((start, end), out var month))
        {
            month = calendar.GetLunarMonth(start + ((end - start) / 2));
            context.LunarMonths[(start, end)] = month;
        }

        return month;
    }

    private IEnumerable<FestivalOccurrence> EvaluateSankranti(string key, FestivalRule rule, EvaluationContext context)
    {
        foreach (var sankranti in context.Sankrantis.Where(sankranti => rule.Sign is null || sankranti.Sign == rule.Sign))
        {
            var day = context.Days.FirstOrDefault(day => context.Window(day, rule.Time)?.End >= sankranti.At);
            if (day is not null)
            {
                yield return CreateOccurrence(context, key, rule, day, sankranti.Sign.ToString(), sankranti.At, sankranti.At);
            }
        }
    }

    /// <summary>
    /// The day of a Tithi or Nakshatra period, in the order documented on <see cref="FestivalRule"/>. Rules with
    /// Bhadra avoidance or a sunrise-day minimum first take the first day whose whole window the period covers
    /// (Bhadra-free), then the sunrise day when the period lasts long enough after sunrise. Every rule then takes the
    /// day it covers most of the window (Bhadra-free first, the earlier day on a tie), and when it touches no window,
    /// the day whose sunrise-to-sunrise span it covers most.
    /// </summary>
    private static PanchangDay SelectDay(EvaluationContext context, FestivalRule rule, Instant start, Instant end)
    {
        if (rule.Ekadashi is { } convention)
        {
            return convention == EkadashiConvention.Smarta
                ? SelectSmartaEkadashi(context, rule, start, end)
                : SelectVaishnavaEkadashi(context, start, end);
        }

        // Every window of a day lies within [sunrise − Arunodaya, next sunrise]; other days cannot overlap the period.
        var days = context.Days
            .Where(day => day.Sun.Sunrise - ObservanceWindows.Arunodaya < end && start < day.Sun.NextSunrise)
            .ToArray();
        if (days.Length == 0)
        {
            return context.Days[0];
        }

        IReadOnlyList<(Instant Start, Instant End)> bhadra = rule.AvoidBhadra ? context.Bhadra : [];
        (Instant Start, Instant End)? Window(PanchangDay day) => context.Window(day, rule.Time);

        if (rule.AvoidBhadra || rule.SunriseDayMinimumFraction is not null)
        {
            var covered = days.FirstOrDefault(day =>
                ObservanceWindows.Covers(Window(day), start, end)
                && !bhadra.Any(period => ObservanceWindows.Overlap(Window(day), period.Start, period.End) > Duration.Zero));
            if (covered is not null)
            {
                return covered;
            }

            if (rule.SunriseDayMinimumFraction is { } fraction
                && days.FirstOrDefault(day => start <= day.Sun.Sunrise && day.Sun.Sunrise < end) is { } sunriseDay
                && end - sunriseDay.Sun.Sunrise >= (sunriseDay.Sun.Sunset - sunriseDay.Sun.Sunrise) * fraction)
            {
                return sunriseDay;
            }
        }

        return Best(days, day => ObservanceWindows.Overlap(Window(day), start, end, bhadra))
            ?? Best(days, day => ObservanceWindows.Overlap(Window(day), start, end))
            ?? Best(days, day => ObservanceWindows.Overlap((day.Sun.Sunrise, day.Sun.NextSunrise), start, end))
            ?? days[0];
    }

    /// <summary>
    /// <see cref="EkadashiConvention.Smarta"/>: the first day at whose sunrise the Ekadashi prevails (or, when it
    /// touches no sunrise, the day it covers most). If Dwadashi has ended by the next sunrise, Parana within Dwadashi is
    /// impossible, so the fast moves to the previous day, which the Ekadashi then also touches.
    /// </summary>
    private static PanchangDay SelectSmartaEkadashi(EvaluationContext context, FestivalRule rule, Instant start, Instant end)
    {
        var days = context.Days;
        var index = IndexOfFirst(days, day => start <= day.Sun.Sunrise && day.Sun.Sunrise < end);
        var prevailsAtSunrise = index >= 0;
        if (!prevailsAtSunrise)
        {
            index = IndexOf(days, SelectDay(context, rule with { Ekadashi = null, Time = ObservanceTime.Sunrise }, start, end));
        }

        var dwadashi = context.Tithis.FirstOrDefault(period => Duration.Max(period.Start - end, end - period.Start) < Duration.FromMinutes(1));
        var parana = index + 1 < days.Count ? days[index + 1] : null;
        if (prevailsAtSunrise && index > 0 && dwadashi is not null && parana is not null && dwadashi.End <= parana.Sun.Sunrise)
        {
            return days[index - 1];
        }

        return days[index];
    }

    /// <summary>
    /// <see cref="EkadashiConvention.Vaishnava"/>: the last day whose Arunodaya begins within the Ekadashi (no Dashami
    /// at Arunodaya); when there is none, the first day whose Arunodaya begins after the Ekadashi began (Dwadashi).
    /// </summary>
    private static PanchangDay SelectVaishnavaEkadashi(EvaluationContext context, Instant start, Instant end)
    {
        static Instant Arunodaya(PanchangDay day) => day.Sun.Sunrise - ObservanceWindows.Arunodaya;

        return context.Days.LastOrDefault(day => start <= Arunodaya(day) && Arunodaya(day) < end)
            ?? context.Days.FirstOrDefault(day => start <= Arunodaya(day))
            ?? context.Days[^1];
    }

    private static int IndexOfFirst(IReadOnlyList<PanchangDay> days, Func<PanchangDay, bool> predicate)
    {
        for (var i = 0; i < days.Count; i++)
        {
            if (predicate(days[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOf(IReadOnlyList<PanchangDay> days, PanchangDay day) => IndexOfFirst(days, candidate => candidate.Date == day.Date);

    private static PanchangDay? Best(IReadOnlyList<PanchangDay> days, Func<PanchangDay, Duration> overlap)
    {
        PanchangDay? best = null;
        var bestOverlap = Duration.Zero;
        foreach (var day in days)
        {
            var value = overlap(day);
            if (value > bestOverlap)
            {
                best = day;
                bestOverlap = value;
            }
        }

        return best;
    }

    private static FestivalOccurrence CreateOccurrence(
        EvaluationContext context, string key, FestivalRule rule, PanchangDay day, string basisKey, Instant basisStart, Instant basisEnd)
    {
        var window = context.Window(day, rule.Time);
        var isAnchorDay = rule.OffsetDays == 0;
        return new FestivalOccurrence(
            key,
            day.Date.PlusDays(rule.OffsetDays),
            rule.Basis,
            basisKey,
            basisStart,
            basisEnd,
            rule.Time,
            isAnchorDay ? window?.Start : null,
            isAnchorDay ? window?.End : null,
            rule.OffsetDays);
    }

    private sealed record EvaluationContext(
        IReadOnlyList<PanchangDay> Days,
        IReadOnlyList<ElementPeriod<Tithi>> Tithis,
        IReadOnlyList<ElementPeriod<Nakshatra>> Nakshatras,
        IReadOnlyList<(Instant Start, Instant End)> Bhadra,
        IReadOnlyList<Sankranti> Sankrantis,
        MoonriseCache Moonrises,
        Dictionary<(Instant Start, Instant End), (LunarMonth Month, bool IsAdhika)> LunarMonths)
    {
        /// <summary>The day's observance window; null when it has none (no moonrise that day).</summary>
        public (Instant Start, Instant End)? Window(PanchangDay day, ObservanceTime time) =>
            ObservanceWindows.Get(day.Sun, time, time == ObservanceTime.Moonrise ? Moonrises.Get(day) : null);
    }

    /// <summary>
    /// The moonrise between a day's sunrise and the next, searched only for the days a Moonrise rule looks at (the
    /// search costs more than the Panchang day itself) and remembered for the rest of the evaluation.
    /// </summary>
    private sealed class MoonriseCache(IMoonEventCalculator moonEvents, GeoCoordinate location, bool enabled)
    {
        private readonly Dictionary<LocalDate, Instant?> _moonrises = [];

        public Instant? Get(PanchangDay day)
        {
            if (!enabled)
            {
                return null;
            }

            if (!_moonrises.TryGetValue(day.Date, out var moonrise))
            {
                moonrise = moonEvents.FindMoonrise(day.Sun.Sunrise, day.Sun.NextSunrise, location);
                _moonrises[day.Date] = moonrise;
            }

            return moonrise;
        }
    }
}
