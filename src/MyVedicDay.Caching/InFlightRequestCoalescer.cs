using System.Collections.Concurrent;

namespace MyVedicDay.Caching;

/// <summary>
/// Collapses concurrent requests for the same key into one execution (cache-stampede protection within an instance).
/// </summary>
/// <remarks>
/// Across instances, duplicate work is bounded to one calculation per instance per key, which is acceptable because
/// a daily calculation takes about a millisecond and writes an identical value. A distributed lock would add a Redis
/// round trip to every miss for no practical gain.
/// </remarks>
public sealed class InFlightRequestCoalescer<TResult>
{
    private readonly ConcurrentDictionary<string, Lazy<Task<TResult>>> _inFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the in-flight task for <paramref name="key"/>, starting <paramref name="factory"/> if none is running.
    /// The shared work is not cancelled when one caller cancels; each caller should apply its own token with WaitAsync.
    /// </summary>
    public Task<TResult> GetOrStartAsync(string key, Func<Task<TResult>> factory)
    {
        var entry = _inFlight.GetOrAdd(key, _ => new Lazy<Task<TResult>>(() => RunAndReleaseAsync(key, factory)));
        return entry.Value;
    }

    private async Task<TResult> RunAndReleaseAsync(string key, Func<Task<TResult>> factory)
    {
        try
        {
            return await factory();
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }
}
