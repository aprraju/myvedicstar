using NodaTime;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Moonrise and moonset at a location. The Moon rises about 50 minutes later each day, so a given interval may hold no
/// moonrise (or no moonset).
/// </summary>
public interface IMoonEventCalculator
{
    /// <summary>The first moonrise in [<paramref name="from"/>, <paramref name="to"/>), or null when there is none.</summary>
    Instant? FindMoonrise(Instant from, Instant to, GeoCoordinate location);

    /// <summary>The first moonset in [<paramref name="from"/>, <paramref name="to"/>), or null when there is none.</summary>
    Instant? FindMoonset(Instant from, Instant to, GeoCoordinate location);
}

/// <summary>
/// Finds moonrise and moonset as the instants the centre of the Moon's disc, seen from the observer, crosses the
/// geometric horizon: no refraction, and the parallax applied, so the geocentric altitude is h₀ = +π (the Moon's
/// horizontal parallax). This is the convention of Drik Panchang, whose published New Delhi moonrise times (the twelve
/// Sankashti Chaturthis of 2025) it reproduces within half a minute; the almanac convention of Meeus, "Astronomical
/// Algorithms", chapter 15 (upper limb with refraction, h₀ = 0.7275·π − 0°34′) gives moonrise about 4 minutes earlier.
/// The altitude is sampled every hour (the Moon cannot rise and set within an hour outside the polar regions) and each
/// crossing is bisected to a tenth of a second.
/// </summary>
internal sealed class MoonEventCalculator(IEphemeris ephemeris) : IMoonEventCalculator
{
    private const double SampleDays = 1d / 24d;
    private const double ToleranceDays = 0.1 / 86400d;

    public Instant? FindMoonrise(Instant from, Instant to, GeoCoordinate location) => FindCrossing(from, to, location, rising: true);

    public Instant? FindMoonset(Instant from, Instant to, GeoCoordinate location) => FindCrossing(from, to, location, rising: false);

    private Instant? FindCrossing(Instant from, Instant to, GeoCoordinate location, bool rising)
    {
        if (to <= from)
        {
            return null;
        }

        var start = JulianDay.FromInstant(from);
        var end = JulianDay.FromInstant(to);

        var previous = start;
        var previousHeight = HeightAboveRisingAltitude(previous, location);
        while (previous < end)
        {
            var next = Math.Min(previous + SampleDays, end);
            var nextHeight = HeightAboveRisingAltitude(next, location);

            var crosses = rising ? previousHeight < 0 && nextHeight >= 0 : previousHeight >= 0 && nextHeight < 0;
            if (crosses)
            {
                return JulianDay.ToInstant(Bisect(previous, next, location, rising));
            }

            previous = next;
            previousHeight = nextHeight;
        }

        return null;
    }

    private double Bisect(double below, double above, GeoCoordinate location, bool rising)
    {
        // Invariant: the Moon is on the "before" side of the crossing at `below` and on the "after" side at `above`.
        while (above - below > ToleranceDays)
        {
            var middle = (below + above) / 2;
            var afterCrossing = HeightAboveRisingAltitude(middle, location) >= 0 == rising;
            if (afterCrossing)
            {
                above = middle;
            }
            else
            {
                below = middle;
            }
        }

        return above;
    }

    /// <summary>The Moon's geocentric altitude minus the altitude of moonrise (its horizontal parallax), in degrees.</summary>
    private double HeightAboveRisingAltitude(double julianDayUt, GeoCoordinate location)
    {
        var moon = ephemeris.GetMoonEquatorial(julianDayUt);
        var hourAngle = SiderealTime.GreenwichMeanDegrees(julianDayUt) + location.Longitude - moon.RightAscension;

        var sinAltitude = (AngleMath.Sin(location.Latitude) * AngleMath.Sin(moon.Declination))
            + (AngleMath.Cos(location.Latitude) * AngleMath.Cos(moon.Declination) * AngleMath.Cos(hourAngle));

        return AngleMath.Asin(sinAltitude) - moon.HorizontalParallax;
    }
}
