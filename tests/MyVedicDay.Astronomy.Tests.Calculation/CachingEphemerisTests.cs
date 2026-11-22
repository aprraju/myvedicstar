using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// api#72 (AST-07): the cache in front of the ephemeris port must answer repeats without changing a number, must not
/// cache a refusal, and its invalidations must be observable rather than possibly-silent.
/// </summary>
public sealed class CachingEphemerisTests
{
    [Fact]
    public void Second_call_for_the_same_day_is_answered_from_the_cache()
    {
        var inner = new CountingEphemeris();
        var cache = new AstronomyResultCache(64);
        var sut = new CachingEphemeris(inner, cache);

        var first = sut.GetSunLongitude(2451545.10);
        var second = sut.GetSunLongitude(2451545.90);   // same quantised UT day

        Assert.Equal(first, second);
        Assert.Equal(1, inner.SunCalls);

        var stats = cache.Statistics();
        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
    }

    [Fact]
    public void A_different_day_is_a_different_entry()
    {
        var inner = new CountingEphemeris();
        var sut = new CachingEphemeris(inner, new AstronomyResultCache(64));

        sut.GetSunLongitude(2451545.0);
        sut.GetSunLongitude(2451546.0);

        Assert.Equal(2, inner.SunCalls);
    }

    [Fact]
    public void The_cached_value_is_the_engine_value()
    {
        var inner = new CountingEphemeris { SunLongitude = 279.5, MoonLongitude = 118.25 };
        var sut = new CachingEphemeris(inner, new AstronomyResultCache(64));

        Assert.Equal(279.5, sut.GetSunLongitude(2451545.0), 12);
        Assert.Equal(118.25, sut.GetMoonLongitude(2451545.0), 12);
    }

    [Fact]
    public void Sibling_bodies_sharing_a_day_do_not_collide()
    {
        var inner = new CountingEphemeris { SunLongitude = 10, MoonLongitude = 200 };
        var sut = new CachingEphemeris(inner, new AstronomyResultCache(64));

        sut.GetSunLongitude(2451545.0);
        sut.GetMoonLongitude(2451545.0);
        sut.GetSunLongitude(2451545.0);
        sut.GetMoonLongitude(2451545.0);

        Assert.Equal(1, inner.SunCalls);
        Assert.Equal(1, inner.MoonCalls);
    }

    [Fact]
    public void A_refusal_is_not_cached_and_propagates()
    {
        var inner = new CountingEphemeris { Refuse = true };
        var cache = new AstronomyResultCache(64);
        var sut = new CachingEphemeris(inner, cache);

        Assert.Throws<AstronomyNotSupportedException>(() => sut.GetSunLongitude(2451545.0));
        Assert.Throws<AstronomyNotSupportedException>(() => sut.GetSunLongitude(2451545.0));

        Assert.Equal(2, inner.SunCalls);
        Assert.Equal(0, cache.Statistics().Count);
    }

    [Fact]
    public void Equatorial_and_parallax_calls_are_forwarded_not_cached()
    {
        // Documented limitation, asserted so it cannot change silently: this key cannot name the inputs of an apparent
        // position or a parallax, so those two methods always reach the engine.
        var inner = new CountingEphemeris();
        var cache = new AstronomyResultCache(64);
        var sut = new CachingEphemeris(inner, cache);

        sut.GetSunEquatorial(2451545.0);
        sut.GetSunEquatorial(2451545.0);
        sut.GetMoonEquatorial(2451545.0);
        sut.GetMoonEquatorial(2451545.0);

        Assert.Equal(2, inner.SunEquatorialCalls);
        Assert.Equal(2, inner.MoonEquatorialCalls);
        Assert.Equal(0, cache.Statistics().Count);
    }

    [Fact]
    public void Invalidation_is_observable_and_repeatable()
    {
        // api#159 shape: an invalidation that works once and then silently no-ops would leave the caller believing
        // fresh values are served. Each call must remove what matches and report it.
        var inner = new CountingEphemeris();
        var cache = new AstronomyResultCache(64);
        var sut = new CachingEphemeris(inner, cache);
        sut.GetSunLongitude(2451545.0);
        var versionBefore = cache.Version;

        Assert.Equal(1, sut.InvalidateOutsideWindow(2451546.0, 2451550.0));
        Assert.True(cache.Version > versionBefore);

        sut.GetSunLongitude(2451545.0);
        Assert.Equal(1, sut.InvalidateOutsideWindow(2451546.0, 2451550.0));

        sut.GetSunLongitude(2451545.0);
        Assert.Equal(1, sut.InvalidateOutsideWindow(2451546.0, 2451550.0));
        Assert.Equal(0, cache.Statistics().Count);
    }

    [Fact]
    public void Provider_change_clears_the_cache_and_reports_the_count()
    {
        var inner = new CountingEphemeris();
        var cache = new AstronomyResultCache(64);
        var sut = new CachingEphemeris(inner, cache);
        sut.GetSunLongitude(2451545.0);
        sut.GetMoonLongitude(2451545.0);

        Assert.Equal(2, sut.InvalidateProviderChange());
        Assert.Equal(0, cache.Statistics().Count);
        Assert.Equal(0, sut.InvalidateProviderChange());
    }

    [Fact]
    public void Non_finite_instant_is_rejected_rather_than_keyed()
    {
        var sut = new CachingEphemeris(new CountingEphemeris(), new AstronomyResultCache(64));
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.GetSunLongitude(double.NaN));
    }

    /// <summary>A port double that counts engine calls, so a cache hit is visible as a call that did not happen.</summary>
    private sealed class CountingEphemeris : IEphemeris
    {
        public int SunCalls { get; private set; }
        public int MoonCalls { get; private set; }
        public int SunEquatorialCalls { get; private set; }
        public int MoonEquatorialCalls { get; private set; }

        public double SunLongitude { get; init; } = 279.0;
        public double MoonLongitude { get; init; } = 118.0;
        public bool Refuse { get; init; }

        public double GetSunLongitude(double julianDayUt)
        {
            SunCalls++;
            if (Refuse) throw new AstronomyNotSupportedException("no toolkit on this host");
            return SunLongitude;
        }

        public double GetMoonLongitude(double julianDayUt)
        {
            MoonCalls++;
            if (Refuse) throw new AstronomyNotSupportedException("no toolkit on this host");
            return MoonLongitude;
        }

        public EquatorialCoordinates GetSunEquatorial(double julianDayUt)
        {
            SunEquatorialCalls++;
            return new EquatorialCoordinates(279.0, 0.0);
        }

        public LunarEquatorialCoordinates GetMoonEquatorial(double julianDayUt)
        {
            MoonEquatorialCalls++;
            return new LunarEquatorialCoordinates(118.0, 0.0, 0.95);
        }
    }
}