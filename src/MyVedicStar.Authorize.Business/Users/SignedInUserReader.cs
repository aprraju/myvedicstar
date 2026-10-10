using System.Security.Claims;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Business.Options;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Business.Users;

/// <summary>Reads the user's object ID, name and email from validated token claims.</summary>
internal sealed class SignedInUserReader
{
    private readonly UserIdentityClaimOptions _options;

    public SignedInUserReader(IOptions<UserIdentityClaimOptions> options)
    {
        _options = options.Value;
    }

    /// <exception cref="AuthenticationFailedException">None of the configured user ID claims are present.</exception>
    public SignedInUser GetSignedInUser(ClaimsPrincipal user)
    {
        var externalUserId = FindFirstValue(user, _options.ExternalUserId)
            ?? throw new AuthenticationFailedException(
                "The token does not identify a user.",
                $"None of the configured user ID claims ({string.Join(", ", _options.ExternalUserId)}) were present.");

        return new SignedInUser(externalUserId, FindFirstValue(user, _options.Name), FindFirstValue(user, _options.Email));
    }

    private static string? FindFirstValue(ClaimsPrincipal user, IEnumerable<string> claimTypes) =>
        claimTypes
            .Select(claimType => user.FindFirst(claimType)?.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));
}
