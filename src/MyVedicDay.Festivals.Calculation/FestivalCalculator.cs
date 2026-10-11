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
internal sealed class FestivalCalculator(IPanchangCalculator panchang, ILunisolarCalendar calendar) : IFestivalCalculator
{
    // A Tithi or Nakshatra spans at most two sunrises; the margin also covers the largest rule offset.
    private const int BaseMarginDays = 2;

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

        var days = CalculateDays(request, margin);
        var context = new EvaluationContext(
            days,
            days.SelectMany(day => day.Tithis).DistinctBy(period => period.Start).ToArray(),
            days.SelectMany(day => day.Nakshatras).DistinctBy(period => period.Start).ToArray(),
            calendar.FindSankrantis(days[0].Sun.Sunrise, days[^1].Sun.NextSunrise));

        return request.Festivals
            .SelectMany(festival => festival.Rules.SelectMany(rule => Evaluate(festival.Key, rule, context)))
            .Where(occurrence => occurrence.Date >= request.From && occurrence.Date <= request.To)
            .DistinctBy(occurrence => (occurrence.Key, occurrence.Date))
            .OrderBy(occurrence => occurrence.Date)
            .ToArray();
    }

    private PanchangDay[] CalculateDays(FestivalCalculationRequest request, int margin)
    {
        var first = request.From.PlusDays(-margin);
        var count = Period.Between(first, request.To.PlusDays(margin), PeriodUnits.Days).Days + 1;

        return Enumerable.Range(0, count)
            .Select(offset => panchang.Calculate(new PanchangCalculationRequest(
                first.PlusDays(offset), request.Location, request.TimeZone, PanchangTraditions.Drik)))
            .ToArray();
    }

    private IEnumerable<FestivalOccurrence> Evaluate(string key, FestivalRule rule, EvaluationContext context) =>
        rule.Basis switch
        {
            FestivalBasis.Tithi => EvaluateElement(key, rule, context, context.Tithis, period => period.Element == rule.Tithi),
            FestivalBasis.Nakshatra => EvaluateElement(key, rule, context, context.Nakshatras, period => period.Element == rule.Nakshatra),
            FestivalBasis.Sankranti => EvaluateSankranti(key, rule, context),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Basis, "Unknown festival basis."),
        };

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
                var (month, isAdhika) = calendar.GetLunarMonth(period.Start + ((period.End - period.Start) / 2));
                if (isAdhika || month != lunarMonth)
                {
                    continue;
                }
            }

            var day = SelectDay(context.Days, period.Start, period.End, rule.Time);
            if (rule.SolarMonth is { } solarMonth && calendar.GetSunSign(day.Sun.Sunset) != solarMonth)
            {
                continue;
            }

            yield return CreateOccurrence(key, rule, day, period.Element.ToString(), period.Start, period.End);
        }
    }

    private IEnumerable<FestivalOccurrence> EvaluateSankranti(string key, FestivalRule rule, EvaluationContext context)
    {
        foreach (var sankranti in context.Sankrantis.Where(sankranti => rule.Sign is null || sankranti.Sign == rule.Sign))
        {
            var day = context.Days.FirstOrDefault(day => ObservanceWindows.Get(day.Sun, rule.Time).End >= sankranti.At);
            if (day is not null)
            {
                yield return CreateOccurrence(key, rule, day, sankranti.Sign.ToString(), sankranti.At, sankranti.At);
            }
        }
    }

    /// <summary>
    /// The day on which the period covers most of the observance window (the earlier day on a tie, e.g. a Tithi that
    /// prevails at two sunrises); when it touches no window, the day whose sunrise-to-sunrise span it covers most.
    /// </summary>
    private static PanchangDay SelectDay(IReadOnlyList<PanchangDay> days, Instant start, Instant end, ObservanceTime time)
    {
        var best = Best(days, day => ObservanceWindows.Overlap(ObservanceWindows.Get(day.Sun, time), start, end));
        return best ?? Best(days, day => ObservanceWindows.Overlap((day.Sun.Sunrise, day.Sun.NextSunrise), start, end)) ?? days[0];
    }

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
        string key, FestivalRule rule, PanchangDay day, string basisKey, Instant basisStart, Instant basisEnd)
    {
        var window = ObservanceWindows.Get(day.Sun, rule.Time);
        var isAnchorDay = rule.OffsetDays == 0;
        return new FestivalOccurrence(
            key,
            day.Date.PlusDays(rule.OffsetDays),
            rule.Basis,
            basisKey,
            basisStart,
            basisEnd,
            rule.Time,
            isAnchorDay ? window.Start : null,
            isAnchorDay ? window.End : null,
            rule.OffsetDays);
    }

    private sealed record EvaluationContext(
        IReadOnlyList<PanchangDay> Days,
        IReadOnlyList<ElementPeriod<Tithi>> Tithis,
        IReadOnlyList<ElementPeriod<Nakshatra>> Nakshatras,
        IReadOnlyList<Sankranti> Sankrantis);
}
