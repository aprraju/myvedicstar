using MyVedicDay.Festivals.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Festivals.Calculation;

/// <summary>
/// The observance windows (Kala) of a Panchang day, from its sunrise, sunset and next sunrise, and its moonrise.
/// Sunrise, Sunset and Moonrise are instants (start equals end). A missing window (no moonrise that day) is null: it
/// overlaps nothing and is covered by nothing.
/// </summary>
internal static class ObservanceWindows
{
    public static readonly Duration Arunodaya = Duration.FromMinutes(96);

    /// <param name="sun">The day's sun events.</param>
    /// <param name="time">The observance time.</param>
    /// <param name="moonrise">The moonrise between the day's sunrise and the next, if any; used only for <see cref="ObservanceTime.Moonrise"/>.</param>
    public static (Instant Start, Instant End)? Get(SunEvents sun, ObservanceTime time, Instant? moonrise = null)
    {
        ArgumentNullException.ThrowIfNull(sun);

        var day = sun.Sunset - sun.Sunrise;
        var night = sun.NextSunrise - sun.Sunset;

        return time switch
        {
            ObservanceTime.Sunrise => (sun.Sunrise, sun.Sunrise),
            ObservanceTime.Arunodaya => (sun.Sunrise - Arunodaya, sun.Sunrise),
            ObservanceTime.Purvahna => (sun.Sunrise, sun.Sunrise + (day / 2)),
            ObservanceTime.Madhyahna => (sun.Sunrise + (day * 2 / 5), sun.Sunrise + (day * 3 / 5)),
            ObservanceTime.Aparahna => (sun.Sunrise + (day * 3 / 5), sun.Sunrise + (day * 4 / 5)),
            ObservanceTime.Sunset => (sun.Sunset, sun.Sunset),
            ObservanceTime.Pradosha => (sun.Sunset, sun.Sunset + (night / 5)),
            ObservanceTime.Nishita => (sun.Sunset + (night * 7 / 15), sun.Sunset + (night * 8 / 15)),
            ObservanceTime.Moonrise => moonrise is { } at ? (at, at) : null,
            _ => throw new ArgumentOutOfRangeException(nameof(time), time, "Unknown observance time."),
        };
    }

    /// <summary>
    /// How much of the window the period covers. An instant window counts as fully covered when the period contains it
    /// (start inclusive, end exclusive), so at a Tithi change exactly at sunrise the new Tithi prevails.
    /// </summary>
    public static Duration Overlap((Instant Start, Instant End)? window, Instant periodStart, Instant periodEnd)
    {
        if (window is not { } w)
        {
            return Duration.Zero;
        }

        if (w.Start == w.End)
        {
            return periodStart <= w.Start && w.Start < periodEnd ? Duration.Epsilon : Duration.Zero;
        }

        var start = w.Start > periodStart ? w.Start : periodStart;
        var end = w.End < periodEnd ? w.End : periodEnd;
        return end > start ? end - start : Duration.Zero;
    }

    /// <summary>Whether the period covers the whole window (contains it, for an instant window).</summary>
    public static bool Covers((Instant Start, Instant End)? window, Instant periodStart, Instant periodEnd) =>
        window is { } w
        && (w.Start == w.End
            ? periodStart <= w.Start && w.Start < periodEnd
            : periodStart <= w.Start && w.End <= periodEnd);

    /// <summary>
    /// <see cref="Overlap(ValueTuple{Instant, Instant}?, Instant, Instant)"/> without the parts where any of
    /// <paramref name="excluded"/> prevails. Excluded periods never overlap each other (they are distinct Karanas).
    /// </summary>
    public static Duration Overlap(
        (Instant Start, Instant End)? window, Instant periodStart, Instant periodEnd, IReadOnlyList<(Instant Start, Instant End)> excluded)
    {
        var overlap = Overlap(window, periodStart, periodEnd);
        if (overlap == Duration.Zero || excluded.Count == 0 || window is not { } w)
        {
            return overlap;
        }

        if (w.Start == w.End)
        {
            return excluded.Any(period => Overlap(w, period.Start, period.End) > Duration.Zero) ? Duration.Zero : overlap;
        }

        var shared = (Start: w.Start > periodStart ? w.Start : periodStart, End: w.End < periodEnd ? w.End : periodEnd);
        foreach (var period in excluded)
        {
            overlap -= Overlap(shared, period.Start, period.End);
        }

        return overlap;
    }
}
