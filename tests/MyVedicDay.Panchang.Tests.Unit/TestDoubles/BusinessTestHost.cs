using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Caching;
using MyVedicDay.Panchang.Business;
using MyVedicDay.Panchang.Calculation;
using NodaTime;
using NodaTime.Testing;

namespace MyVedicDay.Panchang.Tests.Unit.TestDoubles;

/// <summary>
/// Builds the Business layer exactly as the API does, with a controllable clock and a call-counting calculator.
/// </summary>
internal sealed class BusinessTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public BusinessTestHost(Instant now, Action<IServiceCollection>? configure = null, IDictionary<string, string?>? settings = null)
    {
        Clock = new FakeClock(now);
        Calculator = new CountingPanchangCalculator(CreateRealCalculator());
        Cache = new InMemoryPanchangCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<IClock>(Clock);
        services.AddPanchangBusiness(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        services.AddSingleton<IPanchangCalculator>(Calculator);
        services.AddSingleton<IDistributedObjectCache>(Cache);
        configure?.Invoke(services);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Scope = _provider.CreateScope();
    }

    public FakeClock Clock { get; }

    public CountingPanchangCalculator Calculator { get; }

    public InMemoryPanchangCache Cache { get; }

    public IServiceScope Scope { get; }

    public IPanchangFacade Facade => Scope.ServiceProvider.GetRequiredService<IPanchangFacade>();

    public T GetRequiredService<T>()
        where T : notnull => Scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        Scope.Dispose();
        _provider.Dispose();
    }

    private static IPanchangCalculator CreateRealCalculator() =>
        new ServiceCollection().AddPanchangCalculation().BuildServiceProvider().GetRequiredService<IPanchangCalculator>();
}
