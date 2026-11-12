using System.Security.Claims;

namespace MyVedicStar.Email.Business;

/// <summary>Public business entry point for user communication submissions.</summary>
public interface IEmailFacade
{
    /// <summary>Validates and delivers a submission using the existing caller and deduplication policies.</summary>
    Task SendAsync(CommunicationRequest request, ClaimsPrincipal user, string anonymousKey, CancellationToken token);
}
