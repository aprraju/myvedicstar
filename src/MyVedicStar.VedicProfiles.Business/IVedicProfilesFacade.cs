using System.Security.Claims;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.VedicProfiles.Business;

/// <summary>Sole business entry point. The account is always resolved from the authenticated principal.</summary>
public interface IVedicProfilesFacade
{
    Task<IReadOnlyList<VedicProfileSummary>> ListAsync(ClaimsPrincipal user, CancellationToken token);
    Task<VedicProfile> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken token);
    Task<VedicProfile> CreateAsync(ClaimsPrincipal user, VedicProfileDetails details, CancellationToken token);
    Task<VedicProfile> ReplaceAsync(ClaimsPrincipal user, Guid id, VedicProfileDetails details, long revision, CancellationToken token);
    Task DeleteAsync(ClaimsPrincipal user, Guid id, CancellationToken token);
}
