using System.Net;
using System.Security.Claims;
using MyVedicStar.Authorize.Business.Users;

namespace MyVedicStar.Authorize.Business;

internal sealed class AuthorizeFacade : IAuthorizeFacade
{
    private readonly ExternalUserIdResolver _externalUserIdResolver;
    private readonly UserSignInService _userSignInService;

    public AuthorizeFacade(ExternalUserIdResolver externalUserIdResolver, UserSignInService userSignInService)
    {
        _externalUserIdResolver = externalUserIdResolver;
        _userSignInService = userSignInService;
    }

    public Task<UserInfo> SignInUserAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, CancellationToken cancellationToken)
    {
        var externalUserId = _externalUserIdResolver.GetExternalUserId(user);
        return _userSignInService.SignInAsync(externalUserId, clientIpAddress, cancellationToken);
    }
}
