using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;
using MyVedicStar.Shared.Caching;

namespace MyVedicStar.Shared.Tests.Unit.Caching;

public sealed class MemoryCacheStoreTests : IDisposable
{
    private const string Key = "panchang:test";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly TestMeterFactory _meterFactory = new();
    private readonly MemoryCacheStore _store;

    public MemoryCacheStoreTests()
    {
        _store = new MemoryCacheStore(
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            _time,
            new CacheMetrics(_meterFactory));
    }

    private DateTimeOffset InOneHour => _time.GetUtcNow().AddHours(1);

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        Assert.Null(await _store.GetAsync<Day>(Key, CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsValue()
    {
        var day = new Day("Ekadashi");

        await _store.SetAsync(Key, day, InOneHour, CancellationToken.None);

        Assert.Same(day, await _store.GetAsync<Day>(Key, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_AfterAbsoluteExpiration_ReturnsNull()
    {
        await _store.SetAsync(Key, new Day("Ekadashi"), InOneHour, CancellationToken.None);

        _time.Advance(TimeSpan.FromHours(1));

        Assert.Null(await _store.GetAsync<Day>(Key, CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_ExpirationAlreadyPassed_RemovesExistingEntry()
    {
        await _store.SetAsync(Key, new Day("Ekadashi"), InOneHour, CancellationToken.None);

        await _store.SetAsync(Key, new Day("Dwadashi"), _time.GetUtcNow(), CancellationToken.None);

        Assert.Null(await _store.GetAsync<Day>(Key, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        await _store.SetAsync(Key, new Day("Ekadashi"), InOneHour, CancellationToken.None);

        await _store.RemoveAsync(Key, CancellationToken.None);

        Assert.Null(await _store.GetAsync<Day>(Key, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_EntryOfAnotherType_ReturnsNull()
    {
        await _store.SetAsync(Key, "not a day", InOneHour, CancellationToken.None);

        Assert.Null(await _store.GetAsync<Day>(Key, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Operations_BlankKey_Throw(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.GetAsync<Day>(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetAsync(key, new Day("x"), InOneHour, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrCreateAsync_Miss_RunsFactoryOnceAndCachesResult()
    {
        var calls = 0;
        Task<Day> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult(new Day("Ekadashi"));
        }

        var first = await _store.GetOrCreateAsync(Key, Factory, InOneHour, CancellationToken.None);
        var second = await _store.GetOrCreateAsync(Key, Factory, InOneHour, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentMisses_ShareOneFactoryCall()
    {
        var calls = 0;
        var release = new TaskCompletionSource<Day>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Day> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return release.Task;
        }

        var callers = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => _store.GetOrCreateAsync(Key, Factory, InOneHour, CancellationToken.None)))
            .ToArray();
        await WaitUntilAsync(() => Volatile.Read(ref calls) > 0);
        release.SetResult(new Day("Ekadashi"));
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public async Task GetOrCreateAsync_FactoryThrows_PropagatesAndCachesNothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.GetOrCreateAsync<Day>(
            Key, _ => throw new InvalidOperationException("calculation failed"), InOneHour, CancellationToken.None));

        var retried = await _store.GetOrCreateAsync(Key, _ => Task.FromResult(new Day("Ekadashi")), InOneHour, CancellationToken.None);

        Assert.Equal("Ekadashi", retried.Tithi);
    }

    [Fact]
    public async Task GetOrCreateAsync_CallerCancels_OtherCallersStillGetResult()
    {
        var release = new TaskCompletionSource<Day>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        var cancelledCaller = _store.GetOrCreateAsync(Key, _ => release.Task, InOneHour, cancellation.Token);
        var patientCaller = _store.GetOrCreateAsync(Key, _ => release.Task, InOneHour, CancellationToken.None);
        await cancellation.CancelAsync();
        release.SetResult(new Day("Ekadashi"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledCaller);
        Assert.Equal("Ekadashi", (await patientCaller).Tithi);
        Assert.NotNull(await _store.GetAsync<Day>(Key, CancellationToken.None));
    }

    [Fact]
    public async Task Lookups_RecordHitsAndMisses()
    {
        using var hits = new MetricCollector<long>(_meterFactory, CacheMetrics.MeterName, "myvedicstar.cache.hits");
        using var misses = new MetricCollector<long>(_meterFactory, CacheMetrics.MeterName, "myvedicstar.cache.misses");

        await _store.GetAsync<Day>(Key, CancellationToken.None);
        await _store.SetAsync(Key, new Day("Ekadashi"), InOneHour, CancellationToken.None);
        await _store.GetAsync<Day>(Key, CancellationToken.None);
        await _store.GetAsync<Day>(Key, CancellationToken.None);

        Assert.Equal(2, hits.GetMeasurementSnapshot().Sum(measurement => measurement.Value));
        Assert.Equal(1, misses.GetMeasurementSnapshot().Sum(measurement => measurement.Value));
    }

    public void Dispose()
    {
        _store.Dispose();
        _meterFactory.Dispose();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the factory to start.");
            await Task.Delay(10);
        }
    }

    private sealed record Day(string Tithi);
}
