using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    public SignedInUser GetSignedInUser(ClaimsPrincipal user, string? sessionId = null)
    {
        var identity = user.Identities.FirstOrDefault(identity => identity.IsAuthenticated)
            ?? throw new AuthenticationFailedException("The token does not identify a user.");
        user = new ClaimsPrincipal(identity);
        var issuer = FindFirstValue(user, ["iss"])
            ?? throw new AuthenticationFailedException("The token does not identify an issuer.");
        var externalUserId = (issuer.Contains(".ciamlogin.com/", StringComparison.OrdinalIgnoreCase)
            || issuer.Contains("login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase)
            ? FindFirstValue(user, _options.ExternalUserId)
            : FindFirstValue(user, ["sub"]) ?? FindFirstValue(user, _options.ExternalUserId))
            ?? throw new AuthenticationFailedException(
                "The token does not identify a user.",
                $"None of the configured user ID claims ({string.Join(", ", _options.ExternalUserId)}) were present.");

        if (sessionId is not null && !Guid.TryParse(sessionId, out _))
            throw new ValidationException("X-Auth-Session-Id must be a UUID generated once per login session.");
        // A client session ID is only an idempotency key; identity always comes from the validated token.
        var session = sessionId is not null ? "client:" + Guid.Parse(sessionId).ToString("D")
            : FindFirstValue(user, ["sid"]) is { } sid ? "sid:" + sid
            : FindFirstValue(user, ["auth_time"]) is { } time ? "auth_time:" + time
            : FindFirstValue(user, ["iat"]) is { } issued ? "iat:" + issued
            : throw new AuthenticationFailedException("The token does not identify a session. Supply X-Auth-Session-Id.");
        var sessionKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session)));
        return new SignedInUser(externalUserId, FindFirstValue(user, _options.Name), FindFirstValue(user, _options.Email), issuer, sessionKey);
    }

    private static string? FindFirstValue(ClaimsPrincipal user, IEnumerable<string> claimTypes) =>
        claimTypes
            .Select(claimType => user.FindFirst(claimType)?.Value.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));
}
