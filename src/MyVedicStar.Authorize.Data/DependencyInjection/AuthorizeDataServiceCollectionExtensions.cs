using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Data.Options;
using MyVedicStar.Authorize.Data.Persistence;
using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Shared.Health;

namespace MyVedicStar.Authorize.Data.DependencyInjection;

public static class AuthorizeDataServiceCollectionExtensions
{
    private const string DatabaseHealthCheckName = "authorize-database";

    /// <summary>
    /// Registers the Authorize PostgreSQL context, repositories, unit of work and database readiness check.
    /// </summary>
    public static IServiceCollection AddAuthorizeData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<AuthorizeDbContext>((serviceProvider, builder) =>
            AuthorizeDbContextOptionsConfigurator.Configure(
                builder,
                serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserLoginRepository, UserLoginRepository>();
        services.AddScoped<IAuthorizeUnitOfWork, AuthorizeUnitOfWork>();

        services.AddHostedService<DatabaseMigrationHostedService>();

        services.AddHealthChecks()
            .AddDbContextCheck<AuthorizeDbContext>(DatabaseHealthCheckName, tags: [HealthCheckTags.Readiness]);

        return services;
    }
}
