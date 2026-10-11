using NodaTime;

namespace MyVedicDay.Muhurta.Business.Daily;

/// <summary>A day's chosen Good Time.</summary>
internal sealed record BestGoodTimeChoice(LocalDate Date, MuhurtaDayData Day, int SegmentIndex);

/// <summary>
/// Chooses the next best Good Times from days given in date order: at most one per day (the engine's highest-ranked
/// Good Time that has not ended), ranked Highly Favorable before Favorable, then soonest. Only segments the engine marked
/// <c>IsGoodTime</c> are considered, so nothing is ever promoted.
/// </summary>
internal sealed class BestGoodTimeSelector(int count, DateTimeOffset now)
{
    private const string HighlyFavorable = "HighlyFavorable";

    private readonly List<BestGoodTimeChoice> candidates = [];
    private int highlyFavorableCount;

    /// <summary>Enough Highly Favorable days were found: later days cannot rank above them, so the search can stop.</summary>
    public bool IsComplete => highlyFavorableCount >= count;

    public IReadOnlyList<BestGoodTimeChoice> Results =>
        candidates
            .OrderBy(choice => IsHighlyFavorable(choice) ? 0 : 1)
            .ThenBy(choice => choice.Date)
            .Take(count)
            .ToArray();

    public void Consider(LocalDate date, MuhurtaDayData day)
    {
        ArgumentNullException.ThrowIfNull(day);

        foreach (var index in day.GoodTimes)
        {
            var segment = day.Segments[index];
            if (!segment.IsGoodTime || segment.End <= now)
            {
                continue;
            }

            var choice = new BestGoodTimeChoice(date, day, index);
            candidates.Add(choice);
            if (IsHighlyFavorable(choice))
            {
                highlyFavorableCount++;
            }

            return;
        }
    }

    private static bool IsHighlyFavorable(BestGoodTimeChoice choice) =>
        choice.Day.Segments[choice.SegmentIndex].Status == HighlyFavorable;
}
