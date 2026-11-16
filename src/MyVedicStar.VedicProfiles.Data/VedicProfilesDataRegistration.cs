using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Infrastructure.Data;
using MyVedicStar.VedicProfiles.Business;

namespace MyVedicStar.VedicProfiles.Data;

public static class VedicProfilesDataRegistration
{
    /// <summary>Registers the module DbContext and repository. Deliberately registers NO migration
    /// hosted service: the Vedic schema is rolled out by a separately authorized operator (DB-first),
    /// so a missing schema fails safely instead of mutating production on startup.</summary>
    public static IServiceCollection AddVedicProfilesData(this IServiceCollection services)
    {
        services.AddDbContext<VedicProfilesDbContext>((provider, builder) =>
            DatabaseConfiguration.ConfigureDomainDatabase(builder, provider, VedicProfilesDbContext.Schema));
        services.AddScoped<IVedicProfileRepository, VedicProfileRepository>();
        return services;
    }
}
