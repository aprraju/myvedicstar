using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Opts the astronomy port into caching (api#72, AST-07).
/// </summary>
/// <remarks>
/// <para><b>The default is OFF, and this type says why</b> rather than switching a cache on optimistically.
/// <see cref="CachingEphemeris"/> is implemented and tested, but wiring it into the host is only correct while the key
/// and the engine call describe the same request. The key carries <see cref="AstronomySettings"/> (zodiac, ayanamsa,
/// node convention, observer, body set); the port adapter calls the engine with a default
/// <see cref="AstronomySettings"/>. Those coincide today. The moment a provider lets its port settings diverge from that
/// default, a cache hit would be the same stored value answering a different question - silently, because it would only
/// show up as speed. Enabling it is therefore a deliberate composition call, made after the adapter takes its settings
/// as a parameter, with a test: not a default.</para>
///
/// <para><b>Nothing here resolves the container from inside the container.</b> The decorator takes the port instance and
/// the cache as explicit arguments, so a caller owns the construction and the container never builds a graph that
/// refers back to itself. That is what makes the composition testable with two plain objects.</para>
/// </remarks>
public static class AstronomyCachingComposition
{
    /// <summary>Configuration key that opts the port into caching. Absent or false leaves the port unwrapped.</summary>
    public const string ConfigurationKey = "Astronomy:PortCache";

    /// <summary>
    /// Wraps <paramref name="inner"/> in a cache and registers the result as <see cref="IEphemeris"/>, replacing any
    /// registration already present. The cache is a singleton, so every caller shares one bounded entry set and one set
    /// of counters. Returns the services, so this can be chained from composition.
    /// </summary>
    public static IServiceCollection AddAstronomyPortCache(this IServiceCollection services, IEphemeris inner)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(inner);

        var cache = new AstronomyResultCache(CachingEphemeris.DefaultCapacity);

        services.RemoveAll<IEphemeris>();
        services.AddSingleton(cache);
        services.AddSingleton<IEphemeris>(new CachingEphemeris(inner, cache));

        return services;
    }
}
