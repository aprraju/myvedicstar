using System.Net;
using System.Security.Claims;
using MyVedicStar.Authorize.Business.Users;

namespace MyVedicStar.Authorize.Business;

internal sealed class AuthorizeFacade : IAuthorizeFacade
{
    private readonly SignedInUserReader _signedInUserReader;
    private readonly UserSignInService _userSignInService;
    private readonly IProfileService _profiles;

    public AuthorizeFacade(SignedInUserReader signedInUserReader, UserSignInService userSignInService, IProfileService profiles)
    {
        _signedInUserReader = signedInUserReader;
        _userSignInService = userSignInService;
        _profiles = profiles;
    }

    public Task<UserInfo> SignInUserAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, CancellationToken cancellationToken)
        => LoginAsync(user, clientIpAddress, null, cancellationToken);

    public Task<UserInfo> LoginAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, string? sessionId, CancellationToken cancellationToken)
    {
        var signedInUser = _signedInUserReader.GetSignedInUser(user, sessionId);
        return _userSignInService.SignInAsync(signedInUser, clientIpAddress, cancellationToken);
    }

    public async Task<ProfileResult> GetProfileAsync(ClaimsPrincipal user, CancellationToken token) =>
        ProfileResult.From(await _profiles.ReadAsync(user, token));
    public async Task<ProfileResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileUpdateRequest request, CancellationToken token) =>
        ProfileResult.From(await _profiles.UpdateAsync(user, request, token));
    public async Task<ProfileResult> RetryProfileSyncAsync(ClaimsPrincipal user, CancellationToken token) =>
        ProfileResult.From(await _profiles.RetrySyncAsync(user, token));
    public async Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token) =>
        (await _profiles.ReadAsync(user, token)).Id;

    public Task LogoutAsync(ClaimsPrincipal user, IPAddress? clientIpAddress, string? sessionId, CancellationToken cancellationToken) =>
        _userSignInService.SignOutAsync(_signedInUserReader.GetSignedInUser(user, sessionId), clientIpAddress, cancellationToken);
}
