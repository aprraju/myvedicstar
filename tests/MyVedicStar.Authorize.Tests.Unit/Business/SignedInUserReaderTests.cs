using System.Security.Claims;
using MyVedicStar.Authorize.Business.Options;
using MyVedicStar.Authorize.Business.Users;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Tests.Unit.Business;

public sealed class SignedInUserReaderTests
{
    private readonly SignedInUserReader _reader = new(
        Microsoft.Extensions.Options.Options.Create(new UserIdentityClaimOptions
        {
            ExternalUserId = ["oid"],
            Name = ["name"],
            Email = ["email", "preferred_username"],
        }));

    [Fact]
    public void GetSignedInUser_AllClaimsPresent_ReturnsObjectIdNameAndEmail()
    {
        var user = CreateUser(("oid", "object-id"), ("sub", "pairwise-subject"), ("name", "John Smith"), ("email", "john.smith@example.com"));

        Assert.Equal(new SignedInUser("object-id", "John Smith", "john.smith@example.com"), _reader.GetSignedInUser(user));
    }

    [Fact]
    public void GetSignedInUser_NameAndEmailMissing_ReturnsNulls()
    {
        var user = CreateUser(("oid", "object-id"));

        Assert.Equal(new SignedInUser("object-id", null, null), _reader.GetSignedInUser(user));
    }

    [Fact]
    public void GetSignedInUser_FirstClaimBlank_UsesNextConfiguredClaim()
    {
        var user = CreateUser(("oid", "object-id"), ("email", " "), ("preferred_username", "john@example.com"));

        Assert.Equal("john@example.com", _reader.GetSignedInUser(user).Email);
    }

    [Fact]
    public void GetSignedInUser_TrimsValues()
    {
        var user = CreateUser(("oid", "object-id"), ("name", "  John Smith "));

        Assert.Equal("John Smith", _reader.GetSignedInUser(user).Name);
    }

    [Fact]
    public void GetSignedInUser_OnlySubjectPresent_ThrowsBecauseSubjectIsNotTheDirectoryId()
    {
        var user = CreateUser(("sub", "pairwise-subject"), ("name", "John Smith"));

        Assert.Throws<AuthenticationFailedException>(() => _reader.GetSignedInUser(user));
    }

    [Fact]
    public void GetSignedInUser_BlankObjectId_Throws()
    {
        var user = CreateUser(("oid", "  "));

        Assert.Throws<AuthenticationFailedException>(() => _reader.GetSignedInUser(user));
    }

    private static ClaimsPrincipal CreateUser(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Test"));
}
