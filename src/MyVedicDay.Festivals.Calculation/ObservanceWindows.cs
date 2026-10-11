using MyVedicDay.Festivals.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;

namespace MyVedicDay.Festivals.Calculation;

/// <summary>
/// The observance windows (Kala) of a Panchang day, from its sunrise, sunset and next sunrise. Sunrise and Sunset are
/// instants (start equals end).
/// </summary>
internal static class ObservanceWindows
{
    private static readonly Duration Arunodaya = Duration.FromMinutes(96);

    public static (Instant Start, Instant End) Get(SunEvents sun, ObservanceTime time)
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
            _ => throw new ArgumentOutOfRangeException(nameof(time), time, "Unknown observance time."),
        };
    }

    /// <summary>
    /// How much of the window the period covers. An instant window counts as fully covered when the period contains it
    /// (start inclusive, end exclusive), so at a Tithi change exactly at sunrise the new Tithi prevails.
    /// </summary>
    public static Duration Overlap((Instant Start, Instant End) window, Instant periodStart, Instant periodEnd)
    {
        if (window.Start == window.End)
        {
            return periodStart <= window.Start && window.Start < periodEnd ? Duration.Epsilon : Duration.Zero;
        }

        var start = window.Start > periodStart ? window.Start : periodStart;
        var end = window.End < periodEnd ? window.End : periodEnd;
        return end > start ? end - start : Duration.Zero;
    }
}
