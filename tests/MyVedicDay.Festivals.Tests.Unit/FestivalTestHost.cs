using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Caching;
using MyVedicDay.Festivals.Business;
using MyVedicDay.Festivals.Calculation;
using MyVedicDay.Festivals.Calculation.Models;
using NodaTime;
using NodaTime.Testing;

namespace MyVedicDay.Festivals.Tests.Unit;

/// <summary>
/// Builds the festival business layer exactly as the API does, with a controllable clock and a call-counting calculator.
/// </summary>
internal sealed class FestivalTestHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public FestivalTestHost(Instant now)
    {
        Clock = new FakeClock(now);
        Calculator = new CountingFestivalCalculator(
            new ServiceCollection().AddFestivalCalculation().BuildServiceProvider().GetRequiredService<IFestivalCalculator>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IFestivalCalculator>(Calculator);
        services.AddSingleton<IDistributedObjectCache>(Cache);
        services.AddFestivalBusiness(new ConfigurationBuilder().Build());

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _scope = _provider.CreateScope();
    }

    public FakeClock Clock { get; }

    public CountingFestivalCalculator Calculator { get; }

    public InMemoryObjectCache Cache { get; } = new();

    public IFestivalFacade Facade => _scope.ServiceProvider.GetRequiredService<IFestivalFacade>();

    public T GetRequiredService<T>()
        where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

/// <summary>
/// Dictionary-backed cache that records expirations but never evicts, so tests using a fake clock are deterministic.
/// </summary>
internal sealed class InMemoryObjectCache : IDistributedObjectCache
{
    private readonly ConcurrentDictionary<string, object> _entries = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, DateTimeOffset> Expirations { get; } = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class =>
        Task.FromResult(_entries.TryGetValue(key, out var value) ? value as T : null);

    public Task SetAsync<T>(string key, T value, DateTimeOffset absoluteExpiration, CancellationToken cancellationToken)
        where T : class
    {
        _entries[key] = value;
        Expirations[key] = absoluteExpiration;
        return Task.CompletedTask;
    }
}

/// <summary>Decorates the real calculator to count calculations (each covers a range of days).</summary>
internal sealed class CountingFestivalCalculator(IFestivalCalculator inner) : IFestivalCalculator
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public IReadOnlyList<FestivalOccurrence> Calculate(FestivalCalculationRequest request)
    {
        Interlocked.Increment(ref _calls);
        return inner.Calculate(request);
    }
}
