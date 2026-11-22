using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// api#72 (AST-07): the cache is opt-in, and when it IS composed the interaction is observable - one shared cache, and
/// an invalidation that reports what it removed instead of possibly doing nothing.
/// </summary>
/// <remarks>
/// Deliberately keeps only the composition tests here. Anything that resolves the decorated port twice, or resolves it
/// while the container is being torn down, is avoided: the container is built, used and disposed inside each test, and
/// a disposal of a singleton graph this file does not own is not this file's subject.
/// </remarks>
public sealed class AstronomyCachingCompositionTests
{
    /// <summary>A port double so composition is tested without an engine or a kernel.</summary>
    private sealed class CountingEphemeris : IEphemeris
    {
        public int SunCalls { get; private set; }
        public double GetSunLongitude(double julianDayUt) { SunCalls++; return 279.0; }
        public double GetMoonLongitude(double julianDayUt) => 118.0;
        public EquatorialCoordinates GetSunEquatorial(double julianDayUt) => new(279.0, 0.0);
        public LunarEquatorialCoordinates GetMoonEquatorial(double julianDayUt) => new(118.0, 0.0, 0.95);
    }

    private static ServiceProvider Compose(bool withCache)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEphemeris>(new CountingEphemeris());
        if (withCache) services.AddAstronomyPortCache(new CountingEphemeris());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Without_the_explicit_call_the_port_is_not_wrapped()
    {
        // The default is off, and that is the point: a reader must not assume the cache is on because it exists.
        using var provider = Compose(withCache: false);
        Assert.IsType<CountingEphemeris>(provider.GetRequiredService<IEphemeris>());
    }

    [Fact]
    public void With_the_call_the_port_is_the_caching_decorator()
    {
        using var provider = Compose(withCache: true);
        Assert.IsType<CachingEphemeris>(provider.GetRequiredService<IEphemeris>());
    }

    [Fact]
    public void Repeated_reads_through_the_composed_port_reach_the_engine_once()
    {
        using var provider = Compose(withCache: true);
        var decorator = Assert.IsType<CachingEphemeris>(provider.GetRequiredService<IEphemeris>());

        // Two reads through the interface this container hands out; the count is the evidence, not the timing.
        var port = (IEphemeris)decorator;
        port.GetSunLongitude(2451545.10);
        port.GetSunLongitude(2451545.80);

        Assert.Equal(1, decorator.Cache.Statistics().Misses);
        Assert.Equal(1, decorator.Cache.Statistics().Hits);
    }

    [Fact]
    public void Invalidation_through_the_composed_port_is_observable()
    {
        using var provider = Compose(withCache: true);
        var decorator = Assert.IsType<CachingEphemeris>(provider.GetRequiredService<IEphemeris>());
        ((IEphemeris)decorator).GetSunLongitude(2451545.0);

        var versionBefore = decorator.Cache.Version;
        var removed = decorator.InvalidateOutsideWindow(2451546.0, 2451550.0);

        Assert.Equal(1, removed);
        Assert.True(decorator.Cache.Version > versionBefore,
            "A successful invalidation must bump Version; an unchanged version is the silent no-op shape (api#159).");
        Assert.Equal(0, decorator.Cache.Statistics().Count);
    }

    [Fact]
    public void A_provider_change_clears_the_shared_cache()
    {
        using var provider = Compose(withCache: true);
        var decorator = Assert.IsType<CachingEphemeris>(provider.GetRequiredService<IEphemeris>());
        ((IEphemeris)decorator).GetSunLongitude(2451545.0);

        // The engine behind the port was replaced: the in-process key cannot see that, so composition calls this.
        Assert.Equal(1, decorator.InvalidateProviderChange());
        Assert.Equal(0, decorator.Cache.Statistics().Count);
    }
}