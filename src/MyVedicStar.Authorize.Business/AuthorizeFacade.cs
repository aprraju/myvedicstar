using System.Net;
using System.Security.Claims;
using MyVedicStar.Authorize.Business.Users;

namespace MyVedicStar.Authorize.Business;

internal sealed class AuthorizeFacade : IAuthorizeFacade
{
    private readonly SignedInUserReader _signedInUserReader;
    private readonly UserSignInService _userSignInService;

    public AuthorizeFacade(SignedInUserReader signedInUserReader, UserSignInService userSignInService)
    {
        _signedInUserReader = signedInUserReader;
        _userSignInService = userSignInService;
    }

    public Task<UserInfo> SignInUserAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, CancellationToken cancellationToken)
        => LoginAsync(user, clientIpAddress, null, cancellationToken);

    public Task<UserInfo> LoginAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, string? sessionId, CancellationToken cancellationToken)
    {
        var signedInUser = _signedInUserReader.GetSignedInUser(user, sessionId);
        return _userSignInService.SignInAsync(signedInUser, clientIpAddress, cancellationToken);
    }

    public Task LogoutAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, string? sessionId, CancellationToken cancellationToken) =>
        _userSignInService.SignOutAsync(_signedInUserReader.GetSignedInUser(user, sessionId), clientIpAddress, cancellationToken);
}
