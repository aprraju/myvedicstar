using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.VedicProfiles.Business;
using MyVedicStar.VedicProfiles.Data;
using MyVedicStar.VedicProfiles.Integration;

namespace MyVedicStar.Infrastructure.DependencyInjection;

public static class VedicProfilesModuleRegistration
{
    /// <summary>Composes the Vedic Profiles module: business, persistence and the read-only account adapter.
    /// Intentionally registers no migration hosted service — the schema is rolled out DB-first by an
    /// authorized operator, so the host fails safely (503) rather than mutating production on startup.</summary>
    public static IServiceCollection AddVedicProfilesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddVedicProfilesBusiness(); services.AddVedicProfilesData(); services.AddVedicProfilesIntegration();
        return services;
    }
}
