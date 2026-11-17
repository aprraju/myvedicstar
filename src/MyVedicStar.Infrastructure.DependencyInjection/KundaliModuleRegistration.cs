using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Data;
using MyVedicStar.Kundali.Integration;

namespace MyVedicStar.Infrastructure.DependencyInjection;

public static class KundaliModuleRegistration
{
    /// <summary>Composes the Kundali module: business, persistence and the read-only profile/settings adapters.
    /// Intentionally registers no migration hosted service - the schema is rolled out DB-first by an authorized
    /// operator, so the host fails safely (503) rather than mutating production on startup.</summary>
    public static IServiceCollection AddKundaliModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddKundaliBusiness(); services.AddKundaliData(); services.AddKundaliIntegration();
        return services;
    }
}
