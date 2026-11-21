namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// The cache key for an astronomy result (api#72, AST-07): every input that can change a number is named here, so a
/// hit can never be a different question answered with the same value.
/// </summary>
/// <remarks>
/// The instant is quantised to the request's <see cref="Resolution"/> because that is the granularity callers ask at
/// (Panchang/Kundali work to a civil day). A key on the raw Julian Day would miss every time: successive calls carry
/// seconds of drift. The quantisation is an approximation of the <b>cache</b>, never of the engine: a caller that
/// needs finer resolution asks with a finer <see cref="Resolution"/> and gets its own entries.
/// </remarks>
public sealed record AstronomyCacheKey(
    double JulianDay,
    TimeSpan Resolution,
    ZodiacConvention Zodiac,
    AyanamsaKind Ayanamsa,
    NodeConvention NodeConvention,
    double? ObserverLatitudeDegrees,
    double? ObserverLongitudeDegrees,
    double? ObserverElevationMetres,
    string BodySet)
{
    /// <summary>Builds the key for one request, folding bodies into a stable order-insensitive token.</summary>
    public static AstronomyCacheKey For(JulianDayUt time, AstronomySettings settings,
        IReadOnlyCollection<CelestialBody>? bodies, TimeSpan resolution)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (resolution <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be positive.");

        var step = resolution.TotalDays;
        if (!time.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(time), "Julian Day must be finite.");

        var bucket = Math.Floor(time.Value / step) * step;

        return new AstronomyCacheKey(
            JulianDay: bucket,
            Resolution: resolution,
            Zodiac: settings.Zodiac,
            Ayanamsa: settings.Ayanamsa,
            NodeConvention: settings.NodeConvention,
            ObserverLatitudeDegrees: settings.Observer?.LatitudeDegrees,
            ObserverLongitudeDegrees: settings.Observer?.LongitudeDegrees,
            ObserverElevationMetres: settings.Observer?.ElevationMetres,
            BodySet: BodySetToken(bodies));
    }

    /// <summary>True when this key's body set includes <paramref name="body"/> (the all-bodies key includes every body).</summary>
    public bool Includes(CelestialBody body) =>
        BodySet == "*" || BodySet.Split(',').Contains(body.ToString(), StringComparer.Ordinal);

    private static string BodySetToken(IReadOnlyCollection<CelestialBody>? bodies) =>
        bodies is null
            ? "*"
            : string.Join(',', bodies.Select(b => b.ToString()).Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal));
}

/// <summary>Point-in-time cache counters. Cumulative since construction or the last <see cref="AstronomyResultCache.ResetStatistics"/>.</summary>
public sealed record AstronomyCacheStatistics(long Hits, long Misses, long Evictions, int Count, int Capacity)
{
    public double HitRate => Hits + Misses == 0 ? 0d : (double)Hits / (Hits + Misses);
}

/// <summary>
/// A bounded, engine-agnostic result cache: at most <see cref="Capacity"/> entries, least-recently-used evicted, safe
/// for concurrent callers.
/// </summary>
/// <remarks>
/// <para>
/// <b>What invalidates it.</b> Nothing ambient. <see cref="Clear"/> drops everything; <see cref="InvalidateBefore"/>
/// and <see cref="InvalidateBetween"/> drop keys whose quantised instant left the served window (ephemeris-kernel
/// coverage change); <see cref="InvalidateBodySet"/> drops only entries that include a body whose calculation changed.
/// The engine version is <b>not</b> a cache key on purpose: a new engine is a new process, and pretending an in-process
/// key can detect it would be a claim this type cannot test.
/// </para>
/// <para>
/// <b>The shape this type is built to avoid</b> (the <c>TtOffsets.Install</c> latch, issue #159): an invalidation that
/// silently no-ops because something else set the state first is worse than none, because the caller believes fresh
/// values are served. Every invalidation here is unconditional and <b>observable</b>: it removes what matches and
/// returns how many entries it removed, and every caller-visible mutation bumps <see cref="Version"/>, which a test can
/// assert. There is no latch and no first-writer-wins rule, so a second invalidation is as effective as the first.
/// </para>
/// </remarks>
public sealed class AstronomyResultCache
{
    private readonly object _gate = new();
    private readonly Dictionary<AstronomyCacheKey, LinkedListNode<(AstronomyCacheKey Key, AstronomyResult Result)>> _entries = new();
    private readonly LinkedList<(AstronomyCacheKey Key, AstronomyResult Result)> _recency = new();
    private long _hits;
    private long _misses;
    private long _evictions;
    private long _version;

    public AstronomyResultCache(int capacity = 512)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        Capacity = capacity;
    }

    /// <summary>Maximum number of entries retained.</summary>
    public int Capacity { get; }

    /// <summary>
    /// Count of successful mutations. A test asserts this changed after an invalidation, which is what turns a silent
    /// no-op into a failing test instead of a quiet defect.
    /// </summary>
    public long Version
    {
        get { lock (_gate) return _version; }
    }

    /// <summary>Returns the cached result, or computes and stores it when absent.</summary>
    public AstronomyResult GetOrAdd(AstronomyCacheKey key, Func<AstronomyResult> compute)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(compute);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var hit))
            {
                _hits++;
                _recency.Remove(hit);
                _recency.AddFirst(hit);
                return hit.Value.Result;
            }
        }

        // Compute outside the lock: an engine call must never serialise behind unrelated cache traffic.
        var result = compute();

        lock (_gate)
        {
            // A concurrent caller may have stored the same key while we computed. Prefer the stored value so every
            // caller sees one instance for one key (determinism: identical input must yield identical output).
            if (_entries.TryGetValue(key, out var raced))
            {
                _hits++;
                _recency.Remove(raced);
                _recency.AddFirst(raced);
                return raced.Value.Result;
            }

            _misses++;
            var node = _recency.AddFirst((key, result));
            _entries[key] = node;
            _version++;
            Trim();
            return result;
        }
    }

    /// <summary>True when the key is present. Does not change recency or counters.</summary>
    public bool Contains(AstronomyCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate) return _entries.ContainsKey(key);
    }

    /// <summary>Drops every entry. Returns how many were removed.</summary>
    public int Clear()
    {
        lock (_gate)
        {
            var removed = _entries.Count;
            _entries.Clear();
            _recency.Clear();
            if (removed > 0) _version++;
            return removed;
        }
    }

    /// <summary>
    /// Drops every entry whose quantised instant is earlier than <paramref name="earliestServed"/>. The served window
    /// moved forward, so the old floor is unservable. Returns how many were removed.
    /// </summary>
    public int InvalidateBefore(double earliestServed) =>
        InvalidateWhere(key => key.JulianDay < earliestServed);

    /// <summary>Drops every entry outside the inclusive served window. Returns how many were removed.</summary>
    public int InvalidateBetween(double earliestServed, double latestServed) =>
        InvalidateWhere(key => key.JulianDay < earliestServed || key.JulianDay > latestServed);

    /// <summary>Drops every entry whose request includes <paramref name="body"/>. Returns how many were removed.</summary>
    public int InvalidateBodySet(CelestialBody body) => InvalidateWhere(key => key.Includes(body));

    /// <summary>Snapshot of the counters. Reading it resets nothing.</summary>
    public AstronomyCacheStatistics Statistics()
    {
        lock (_gate) return new AstronomyCacheStatistics(_hits, _misses, _evictions, _entries.Count, Capacity);
    }

    /// <summary>Resets hit/miss/eviction counters, leaving stored entries in place.</summary>
    public void ResetStatistics()
    {
        lock (_gate)
        {
            _hits = 0;
            _misses = 0;
            _evictions = 0;
        }
    }

    private int InvalidateWhere(Func<AstronomyCacheKey, bool> predicate)
    {
        lock (_gate)
        {
            var doomed = _entries.Keys.Where(predicate).ToArray();
            foreach (var key in doomed)
            {
                var node = _entries[key];
                _entries.Remove(key);
                _recency.Remove(node);
            }

            if (doomed.Length > 0) _version++;
            return doomed.Length;
        }
    }

    private void Trim()
    {
        while (_entries.Count > Capacity)
        {
            var last = _recency.Last!;
            _recency.RemoveLast();
            _entries.Remove(last.Value.Key);
            _evictions++;
            _version++;
        }
    }
}
