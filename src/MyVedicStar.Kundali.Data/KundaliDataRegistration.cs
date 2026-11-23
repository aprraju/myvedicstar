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
        services.AddScoped<IKundaliAlertRepository, KundaliAlertRepository>();
        // KUN-17 (api#57): report persistence. Registered here, next to the repository it shares a DbContext with, so a
        // host that composes the Data layer gets a real store rather than the business layer's refusing fallback.
        services.AddScoped<IKundaliReportStore, KundaliReportStore>();
        return services;
    }
}
