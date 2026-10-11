using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Caching;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Muhurta.Calculation;
using MyVedicDay.Muhurta.Calculation.Models;
using NodaTime;
using NodaTime.Testing;

namespace MyVedicDay.Muhurta.Tests.Unit;

/// <summary>
/// Builds the Muhurta business layer exactly as the API does, with a controllable clock, a call-counting calculator
/// and a non-evicting cache.
/// </summary>
internal sealed class MuhurtaTestHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public MuhurtaTestHost(Instant now, Action<IServiceCollection>? configure = null)
    {
        Clock = new FakeClock(now);
        Calculator = new CountingMuhurtaCalculator(
            new ServiceCollection().AddMuhurtaCalculation().BuildServiceProvider().GetRequiredService<IMuhurtaCalculator>());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<IMuhurtaCalculator>(Calculator);
        services.AddSingleton<IDistributedObjectCache>(Cache);
        configure?.Invoke(services);
        services.AddMuhurtaBusiness(new ConfigurationBuilder().Build());

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _scope = _provider.CreateScope();
    }

    public FakeClock Clock { get; }

    public CountingMuhurtaCalculator Calculator { get; }

    public InMemoryObjectCache Cache { get; } = new();

    public IMuhurtaFacade Facade => _scope.ServiceProvider.GetRequiredService<IMuhurtaFacade>();

    public T GetRequiredService<T>()
        where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

/// <summary>Dictionary-backed cache that never evicts, so tests using a fake clock are deterministic.</summary>
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

/// <summary>Decorates the real calculator to count evaluations.</summary>
internal sealed class CountingMuhurtaCalculator(IMuhurtaCalculator inner) : IMuhurtaCalculator
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public MuhurtaEvaluation Evaluate(MuhurtaCalculationRequest request)
    {
        Interlocked.Increment(ref _calls);
        return inner.Evaluate(request);
    }
}
