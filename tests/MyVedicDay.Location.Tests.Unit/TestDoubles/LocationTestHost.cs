using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyVedicDay.Location.Tests.Unit.TestDoubles;

/// <summary>
/// Builds the Location module on its own, exactly as a feature module registers it.
/// </summary>
internal sealed class LocationTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public LocationTestHost(Action<IServiceCollection>? configure = null, IDictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocationBusiness(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        configure?.Invoke(services);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Scope = _provider.CreateScope();
    }

    public IServiceScope Scope { get; }

    public T GetRequiredService<T>()
        where T : notnull => Scope.ServiceProvider.GetRequiredService<T>();

    public void Dispose()
    {
        Scope.Dispose();
        _provider.Dispose();
    }
}
