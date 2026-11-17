using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Infrastructure.Data;
using MyVedicStar.Kundali.Business;

namespace MyVedicStar.Kundali.Data;

public static class KundaliDataRegistration
{
    /// <summary>Registers the module DbContext and repository. Deliberately registers NO migration hosted service:
    /// the Kundali schema is rolled out DB-first by a separately authorized operator, so a missing schema fails
    /// safely instead of mutating production on startup (same policy as Vedic Profiles).</summary>
    public static IServiceCollection AddKundaliData(this IServiceCollection services)
    {
        services.AddDbContext<KundaliDbContext>((provider, builder) =>
            DatabaseConfiguration.ConfigureDomainDatabase(builder, provider, KundaliDbContext.Schema));
        services.AddScoped<IKundaliRepository, KundaliRepository>();
        return services;
    }
}
