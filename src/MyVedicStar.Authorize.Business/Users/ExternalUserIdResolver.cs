using System.Security.Claims;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Business.Options;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>Reads the user's directory object ID from validated token claims.</summary>
internal sealed class ExternalUserIdResolver
{
    private readonly UserIdentityClaimOptions _options;

    public ExternalUserIdResolver(IOptions<UserIdentityClaimOptions> options)
    {
        _options = options.Value;
    }

    /// <exception cref="AuthenticationFailedException">None of the configured claims are present.</exception>
    public string GetExternalUserId(ClaimsPrincipal user) =>
        _options.ExternalUserId
            .Select(claimType => user.FindFirst(claimType)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? throw new AuthenticationFailedException(
            "The token does not identify a user.",
            $"None of the configured user ID claims ({string.Join(", ", _options.ExternalUserId)}) were present.");
}
