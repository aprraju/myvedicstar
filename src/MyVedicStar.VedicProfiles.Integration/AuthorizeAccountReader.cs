using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Business;
using MyVedicStar.VedicProfiles.Business;

namespace MyVedicStar.VedicProfiles.Integration;

/// <summary>Read-only adapter over the Authorize public facade. Depends on the interface only:
/// it must never reference an Authorize DbContext or repository, and never writes identity data.</summary>
internal sealed class AuthorizeAccountReader(IAuthorizeFacade authorize) : IVedicProfileAccountReader
{
    public Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token) =>
        authorize.GetRegisteredAccountIdAsync(user, token);

    public async Task<string?> GetDefaultNameAsync(ClaimsPrincipal user, CancellationToken token)
    {
        var profile = await authorize.GetProfileAsync(user, token);
        var name = string.Join(' ', new[] { profile.FirstName, profile.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        return name.Length == 0 ? null : name;
    }
}

public static class VedicProfilesIntegrationRegistration
{
    public static IServiceCollection AddVedicProfilesIntegration(this IServiceCollection services)
    {
        services.AddScoped<IVedicProfileAccountReader, AuthorizeAccountReader>();
        return services;
    }
}
