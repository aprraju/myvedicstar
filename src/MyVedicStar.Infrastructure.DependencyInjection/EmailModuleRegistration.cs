using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Email.Business;
using MyVedicStar.Email.Integration;

namespace MyVedicStar.Infrastructure.DependencyInjection;

/// <summary>Composes email provider implementations separately from business operations.</summary>
public static class EmailModuleRegistration
{
    public static IServiceCollection AddEmailModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEmailIntegration(configuration);
        services.AddEmailBusiness(configuration);
        return services;
    }
}
