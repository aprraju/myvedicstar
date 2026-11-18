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

        // Profile deletion is extended by registration, not by reference: VedicProfiles must not know Kundali, and
        // Kundali.Integration must not know VedicProfiles. This project references both, so it is the only place the
        // two can be joined without inverting a dependency.
        services.AddScoped<MyVedicStar.VedicProfiles.Business.IVedicProfileDeletionParticipant,
            MyVedicStar.Kundali.Integration.KundaliProfileDeletionParticipant>();
        return services;
    }
}
// CI path-filter proof for #86 (third gap): this DI-only edit must trigger MyVedicDay API CI.
