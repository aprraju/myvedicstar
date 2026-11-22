using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Sits in front of the engine-backed <see cref="IEphemeris"/> and answers a repeated (body, instant) from a bounded
/// cache instead of calling the engine again (api#72, AST-07).
/// </summary>
/// <remarks>
/// <para><b>Why a decorator and not a cache inside the provider.</b> The port is the single seam every caller already
/// goes through, so the cache is visible in DI and replaceable in tests, and the provider keeps no hidden state that
/// could drift between two hosts in one process. There is still exactly one calculation path: every miss ends in the
/// same engine call it would have made without this type.</para>
///
/// <para><b>What this type must never do</b> is change a number or answer a question that was not asked. The key comes
/// from <see cref="AstronomyCacheKey"/>, which names every input that can move a value the engine port publishes: the
/// instant, the body, and the default <see cref="AstronomySettings"/> the adapter's callers use. The instant is
/// quantised to <c>resolution</c> - whole UT days by default, because that is the granularity Panchang and Kundali ask
/// at, and a key on the raw Julian Day would miss every time as successive calls carry seconds of drift. The
/// quantisation is an approximation of the <b>cache</b>, never of the engine.</para>
///
/// <para><b>Two port methods are not cached, deliberately.</b> <c>GetMoonEquatorial</c> carries the Moon's horizontal
/// parallax, and <c>GetSunEquatorial</c> is an apparent position: neither is a field of the cached per-body ecliptic
/// element list this key describes, and reconstructing them from a cached longitude would be computing a number the
/// engine did not produce. Those two calls are forwarded to the engine every time. Caching them under a key that
/// cannot name their inputs is exactly the plausible-wrong-answer defect this work exists to prevent, so the
/// decorator stays partial and says so instead of widening its key to cover inputs it cannot enumerate.</para>
///
/// <para><b>The two longitudes are cached</b>, because those are the hot path: a Panchang day asks for the Sun and Moon
/// tropical longitude repeatedly at the same civil day, and each is a pure function of (body, settings, quantised
/// instant).</para>
///
/// <para><b>Failures are not cached.</b> An engine refusal (<see cref="SpiceUnavailableException"/>,
/// <see cref="AstronomyNotSupportedException"/>) propagates exactly as it would without the decorator. No fallback, no
/// negative cache: a refusal is a fact about the deployment, not a computed result.</para>
///
/// <para><b>Invalidation is observable</b> - the deliberate opposite of the <c>TtOffsets.Install</c> latch (api#159).
/// Every invalidation returns the count it removed and bumps <see cref="AstronomyResultCache.Version"/>, so a silent
/// no-op fails a test. <see cref="InvalidateProviderChange"/> exists for the one thing an in-process key cannot detect:
/// the engine behind the port being replaced.</para>
/// </remarks>
public sealed class CachingEphemeris : IEphemeris
{
    /// <summary>Default capacity: about 512 distinct (day, body) entries for one settings set.</summary>
    public const int DefaultCapacity = 512;

    private static readonly TimeSpan DefaultResolution = TimeSpan.FromDays(1);

    private readonly IEphemeris _inner;
    private readonly AstronomyResultCache _cache;
    private readonly TimeSpan _resolution;
    private readonly AstronomySettings _settings;

    /// <summary>
    /// Wraps <paramref name="inner"/>, caching the two tropical longitudes. <paramref name="settings"/> must be the
    /// settings the inner port actually uses; a mismatch would cache an answer to a different question, so the default
    /// is the same explicit default the engine adapter constructs.
    /// </summary>
    public CachingEphemeris(IEphemeris inner, AstronomyResultCache cache, TimeSpan? resolution = null,
        AstronomySettings? settings = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _resolution = resolution ?? DefaultResolution;
        if (_resolution <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be positive.");
        _settings = settings ?? new AstronomySettings();
    }

    /// <summary>The cache this decorator answers from, for statistics and invalidation.</summary>
    public AstronomyResultCache Cache => _cache;

    /// <summary>Sun tropical longitude, from the cache when this (body, quantised instant) was already answered.</summary>
    public double GetSunLongitude(double julianDayUt) =>
        FromLongitudeCache(CelestialBody.Sun, julianDayUt, _inner.GetSunLongitude);

    /// <summary>Moon tropical longitude, from the cache when this (body, quantised instant) was already answered.</summary>
    public double GetMoonLongitude(double julianDayUt) =>
        FromLongitudeCache(CelestialBody.Moon, julianDayUt, _inner.GetMoonLongitude);

    /// <summary>Apparent solar equatorial position. Forwarded: see the type remarks for why it is not cached.</summary>
    public EquatorialCoordinates GetSunEquatorial(double julianDayUt) =>
        _inner.GetSunEquatorial(julianDayUt);

    /// <summary>Lunar equatorial position and parallax. Forwarded: see the type remarks for why it is not cached.</summary>
    public LunarEquatorialCoordinates GetMoonEquatorial(double julianDayUt) =>
        _inner.GetMoonEquatorial(julianDayUt);

    /// <summary>
    /// Drops every entry whose quantised instant falls outside the inclusive window: the kernel-coverage change.
    /// Returns how many entries were removed.
    /// </summary>
    public int InvalidateOutsideWindow(double earliestJulianDayUt, double latestJulianDayUt) =>
        _cache.InvalidateBetween(earliestJulianDayUt, latestJulianDayUt);

    /// <summary>
    /// Drops everything, for a provider or engine-version change behind the port. Returns how many entries were
    /// removed. An in-process key cannot detect that the engine itself changed, so invoking this at the composition
    /// seam is the caller's obligation - and it reports what it did rather than silently no-op-ing.
    /// </summary>
    public int InvalidateProviderChange() => _cache.Clear();

    /// <summary>
    /// One cached body longitude. The stored value is an <see cref="AstronomyResult"/> carrying exactly that one body,
    /// so the entry is as narrow as the question asked; the projection reads the longitude back out of it.
    /// </summary>
    private double FromLongitudeCache(CelestialBody body, double julianDayUt, Func<double, double> compute)
    {
        if (!double.IsFinite(julianDayUt))
            throw new ArgumentOutOfRangeException(nameof(julianDayUt), "Julian Day must be finite.");

        var key = AstronomyCacheKey.For(new JulianDayUt(julianDayUt), _settings, [body], _resolution);
        var result = _cache.GetOrAdd(key, () => new AstronomyResult(
            new JulianDayUt(key.JulianDay),
            _settings,
            [new BodyPosition(body,
                new EclipticPoint(compute(julianDayUt), double.NaN, null),
                new EquatorialPoint(double.NaN, double.NaN),
                double.NaN,
                false)],
            new AstronomyProvenance("caching-ephemeris", "cached-before-engine", "n/a", double.NaN,
                _settings.NodeConvention, _settings.Ayanamsa, _settings.Zodiac)));

        return result.Bodies[0].Ecliptic.LongitudeDegrees;
    }
}
