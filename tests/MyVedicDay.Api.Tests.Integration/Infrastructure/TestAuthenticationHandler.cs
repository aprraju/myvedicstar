using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicStar.ApiProtection.Auth.Claims;

namespace MyVedicDay.Api.Tests.Integration.Infrastructure;

/// <summary>
/// Lightweight authentication handler that succeeds by default, allowing integration tests to
/// exercise protected endpoints without a real identity provider or JWT tokens.
/// Requests with X-Test-Anonymous return no identity to exercise anonymous access and auth challenges.
/// </summary>
public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Test-Anonymous"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var ticket = new AuthenticationTicket(CreatePrincipal(), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    public static ClaimsPrincipal CreatePrincipal()
    {
        var claims = new[]
        {
            new Claim(StandardClaimNames.UserId, "test-user-id"),
            new Claim("oid", "test-user-id"),
            new Claim(StandardClaimNames.Provider, "entra"),
            new Claim("iss", "https://test.example"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        return new ClaimsPrincipal(identity);
    }
}
