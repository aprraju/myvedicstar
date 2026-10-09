using System.Security.Claims;
using MyVedicStar.Authorize.Business.Options;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class ExternalUserIdResolverTests
{
    private readonly ExternalUserIdResolver _resolver = new(
        Microsoft.Extensions.Options.Options.Create(new UserIdentityClaimOptions { ExternalUserId = ["oid"] }));

    [Fact]
    public void GetExternalUserId_ObjectIdClaimPresent_ReturnsIt()
    {
        var user = CreateUser(("oid", "object-id"), ("sub", "pairwise-subject"));

        Assert.Equal("object-id", _resolver.GetExternalUserId(user));
    }

    [Fact]
    public void GetExternalUserId_OnlySubjectPresent_ThrowsBecauseSubjectIsNotTheDirectoryId()
    {
        var user = CreateUser(("sub", "pairwise-subject"));

        Assert.Throws<AuthenticationFailedException>(() => _resolver.GetExternalUserId(user));
    }

    [Fact]
    public void GetExternalUserId_BlankClaim_Throws()
    {
        var user = CreateUser(("oid", "  "));

        Assert.Throws<AuthenticationFailedException>(() => _resolver.GetExternalUserId(user));
    }

    private static ClaimsPrincipal CreateUser(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Test"));
}
