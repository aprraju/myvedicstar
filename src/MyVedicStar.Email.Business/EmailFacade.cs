using System.Security.Claims;

namespace MyVedicStar.Email.Business;

internal sealed class EmailFacade(EmailCommunicationService communication) : IEmailFacade
{
    public Task SendAsync(CommunicationRequest request, ClaimsPrincipal user, string anonymousKey, CancellationToken token) =>
        communication.SendAsync(request, user, anonymousKey, token);
}
