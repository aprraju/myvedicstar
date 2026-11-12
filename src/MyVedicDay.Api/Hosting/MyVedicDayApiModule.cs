using GeoNames;
using GeoNames.Business;
using MyVedicDay.Api.Infrastructure;
using MyVedicDay.Festivals.Business;
using MyVedicDay.Muhurta.Business;
using MyVedicDay.Panchang.Business;

namespace MyVedicDay.Api.Hosting;

/// <summary>Composition entry points used by the standalone API and a deployment host.</summary>
public static class MyVedicDayApiModule
{
    /// <summary>Registers the API and its business modules using the supplied configuration.</summary>
    public static IServiceCollection AddMyVedicDayApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiInfrastructure(configuration);
        services.AddGeoNamesLocationSearch(configuration);
        services.AddLocationLookupBusiness();
        services.AddPanchangBusiness(configuration);
        services.AddFestivalBusiness(configuration);
        services.AddMuhurtaBusiness(configuration);
        return services;
    }

    /// <summary>Configures the existing middleware, authorization and endpoint pipeline.</summary>
    public static WebApplication UseMyVedicDayApi(this WebApplication app) => app.UseApiInfrastructure();
}
