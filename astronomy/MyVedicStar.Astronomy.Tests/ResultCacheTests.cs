namespace MyVedicStar.Astronomy.Tests;

using MyVedicStar.Astronomy.Core;

/// <summary>
/// api#72 (AST-07): the cache must be bounded, must report what it did, and must never contain an invalidation that
/// silently no-ops — the shape of the <c>TtOffsets.Install</c> latch (issue #159).
/// </summary>
public sealed class ResultCacheTests
{
    private static readonly AstronomySettings Settings = new();

    private static AstronomyCacheKey KeyAt(double jd, IReadOnlyCollection<CelestialBody>? bodies = null) =>
        AstronomyCacheKey.For(new JulianDayUt(jd), Settings, bodies, TimeSpan.FromDays(1));

    private static AstronomyResult Result(double jd) =>
        new(new JulianDayUt(jd), Settings, Array.Empty<BodyPosition>(),
            new AstronomyProvenance("test", "test", "test", 0d, NodeConvention.Mean, AyanamsaKind.Lahiri,
                ZodiacConvention.Tropical));

    [Fact]
    public void Key_quantises_the_instant_to_the_resolution()
    {
        var morning = KeyAt(2451545.10);
        var evening = KeyAt(2451545.90);

        Assert.Equal(morning, evening);
        Assert.NotEqual(KeyAt(2451546.10), morning);
    }

    [Fact]
    public void Key_body_set_is_order_insensitive_and_distinct_from_all_bodies()
    {
        var forward = KeyAt(2451545.0, new[] { CelestialBody.Sun, CelestialBody.Moon });
        var reverse = KeyAt(2451545.0, new[] { CelestialBody.Moon, CelestialBody.Sun });
        var all = KeyAt(2451545.0);

        Assert.Equal(forward, reverse);
        Assert.NotEqual(forward, all);
    }

    [Fact]
    public void Second_call_for_the_same_key_is_a_hit_and_computes_once()
    {
        var cache = new AstronomyResultCache();
        var computations = 0;
        var key = KeyAt(2451545.0);

        cache.GetOrAdd(key, () => { computations++; return Result(2451545.0); });
        cache.GetOrAdd(key, () => { computations++; return Result(2451545.0); });

        var stats = cache.Statistics();
        Assert.Equal(1, computations);
        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Equal(0.5d, stats.HitRate, 3);
    }

    [Fact]
    public void Cache_is_bounded_and_evicts_the_least_recently_used_entry()
    {
        var cache = new AstronomyResultCache(capacity: 2);
        var first = KeyAt(2451545.0);
        var second = KeyAt(2451546.0);
        var third = KeyAt(2451547.0);

        cache.GetOrAdd(first, () => Result(2451545.0));
        cache.GetOrAdd(second, () => Result(2451546.0));
        cache.GetOrAdd(first, () => Result(2451545.0));   // first becomes most recent
        cache.GetOrAdd(third, () => Result(2451547.0));   // evicts second

        Assert.Equal(2, cache.Statistics().Count);
        Assert.True(cache.Contains(first));
        Assert.False(cache.Contains(second));
        Assert.True(cache.Contains(third));
        Assert.Equal(1, cache.Statistics().Evictions);
    }

    [Fact]
    public void InvalidateBefore_removes_and_reports_what_it_removed()
    {
        var cache = new AstronomyResultCache();
        cache.GetOrAdd(KeyAt(2451540.0), () => Result(2451540.0));
        cache.GetOrAdd(KeyAt(2451545.0), () => Result(2451545.0));
        var versionBefore = cache.Version;

        var removed = cache.InvalidateBefore(2451543.0);

        Assert.Equal(1, removed);
        Assert.True(cache.Version > versionBefore);
        Assert.False(cache.Contains(KeyAt(2451540.0)));
        Assert.True(cache.Contains(KeyAt(2451545.0)));
    }

    [Fact]
    public void Second_invalidation_is_as_effective_as_the_first_no_silent_no_op()
    {
        // The #159 shape: an invalidation that works once and then silently stops working would leave a caller
        // believing fresh values are served. Here each call must still remove what matches.
        var cache = new AstronomyResultCache();
        cache.GetOrAdd(KeyAt(2451540.0), () => Result(2451540.0));
        Assert.Equal(1, cache.InvalidateBefore(2451543.0));

        cache.GetOrAdd(KeyAt(2451541.0), () => Result(2451541.0));
        Assert.Equal(1, cache.InvalidateBefore(2451543.0));

        cache.GetOrAdd(KeyAt(2451542.0), () => Result(2451542.0));
        Assert.Equal(1, cache.InvalidateBefore(2451543.0));
        Assert.Equal(0, cache.Statistics().Count);
    }

    [Fact]
    public void InvalidateBetween_drops_keys_outside_the_served_window()
    {
        var cache = new AstronomyResultCache();
        foreach (var jd in new[] { 2451540.0, 2451545.0, 2451550.0 })
            cache.GetOrAdd(KeyAt(jd), () => Result(jd));

        var removed = cache.InvalidateBetween(2451541.0, 2451549.0);

        Assert.Equal(2, removed);
        Assert.True(cache.Contains(KeyAt(2451545.0)));
        Assert.Equal(1, cache.Statistics().Count);
    }

    [Fact]
    public void InvalidateBodySet_drops_entries_that_include_the_body()
    {
        var cache = new AstronomyResultCache();
        cache.GetOrAdd(KeyAt(2451545.0, new[] { CelestialBody.Sun }), () => Result(2451545.0));
        cache.GetOrAdd(KeyAt(2451545.0, new[] { CelestialBody.Sun, CelestialBody.Moon }), () => Result(2451545.0));
        cache.GetOrAdd(KeyAt(2451545.0), () => Result(2451545.0));   // all bodies

        var removed = cache.InvalidateBodySet(CelestialBody.Moon);

        Assert.Equal(2, removed);
        Assert.True(cache.Contains(KeyAt(2451545.0, new[] { CelestialBody.Sun })));
    }

    [Fact]
    public void A_changed_observer_is_a_different_key()
    {
        var geocentric = AstronomyCacheKey.For(new JulianDayUt(2451545.0), Settings, null, TimeSpan.FromDays(1));
        var topocentric = AstronomyCacheKey.For(
            new JulianDayUt(2451545.0),
            Settings with { Observer = new ObserverLocation(28.6139, 77.2090, 216) },
            null,
            TimeSpan.FromDays(1));

        Assert.NotEqual(geocentric, topocentric);
    }
}
