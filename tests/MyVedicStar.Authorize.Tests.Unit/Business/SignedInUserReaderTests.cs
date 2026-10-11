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

        var result = _reader.GetSignedInUser(user);
        Assert.Equal("object-id", result.ExternalUserId);
        Assert.Equal("John Smith", result.Name);
        Assert.Equal("john.smith@example.com", result.Email);
        Assert.Equal("https://test.ciamlogin.com/test/v2.0", result.Issuer);
    }

    [Fact]
    public void GetSignedInUser_NameAndEmailMissing_ReturnsNulls()
    {
        var user = CreateUser(("oid", "object-id"));

        var result = _reader.GetSignedInUser(user);
        Assert.Equal("object-id", result.ExternalUserId);
        Assert.Null(result.Name);
        Assert.Null(result.Email);
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

    [Theory]
    [InlineData("unknown")]
    [InlineData("aprraju@gmail.com")]
    public void PlaceholderOrEmailName_WithPreferredUsername_ReturnsEmailWithoutInventingName(string name)
    {
        var result = _reader.GetSignedInUser(CreateUser(("oid", "object-id"), ("name", name), ("preferred_username", "aprraju@gmail.com")));
        Assert.Equal("aprraju@gmail.com", result.Email);
        Assert.Null(result.Name);
    }

    [Fact]
    public void PlaceholderName_UsesGivenAndFamilyNameClaims()
    {
        var result = _reader.GetSignedInUser(CreateUser(("oid", "object-id"), ("name", "unknown"), ("given_name", " Jane "), ("family_name", " Doe ")));
        Assert.Equal("Jane Doe", result.Name);
    }

    [Theory]
    [InlineData("login-handle")]
    [InlineData("Name <user@example.com>")]
    public void PreferredUsername_ThatIsNotAnEmail_ReturnsNullEmail(string username) =>
        Assert.Null(_reader.GetSignedInUser(CreateUser(("oid", "object-id"), ("preferred_username", username))).Email);

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
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value))
            .Concat([new Claim("iss", "https://test.ciamlogin.com/test/v2.0"), new Claim("iat", "123456")]), authenticationType: "Test"));

    [Theory]
    [InlineData("https://accounts.google.com")]
    [InlineData("https://appleid.apple.com")]
    [InlineData("https://www.facebook.com")]
    public void OtherProviders_UseStableSubjectAndValidatedIssuer(string issuer)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", issuer), new Claim("sub", "provider-id"), new Claim("sid", "session")], "Test"));
        var result = _reader.GetSignedInUser(principal);
        Assert.Equal("provider-id", result.ExternalUserId);
        Assert.Equal(issuer, result.Issuer);
    }

    [Theory]
    [InlineData("sid")]
    [InlineData("auth_time")]
    public void StableSessionClaim_DeduplicatesRefreshedTokens(string claim)
    {
        var first = CreateUser(("oid", "object-id"), (claim, "stable-session"), ("iat", "1"));
        var refreshed = CreateUser(("oid", "object-id"), (claim, "stable-session"), ("iat", "2"));
        Assert.Equal(_reader.GetSignedInUser(first).SessionKey, _reader.GetSignedInUser(refreshed).SessionKey);
        Assert.NotEqual(_reader.GetSignedInUser(first).SessionKey,
            _reader.GetSignedInUser(CreateUser(("oid", "object-id"), (claim, "new-session"))).SessionKey);
    }

    [Fact]
    public void ClientSessionKey_IsValidatedAndIdentityRemainsFromToken()
    {
        var user = CreateUser(("oid", "object-id"));
        Assert.Throws<ValidationException>(() => _reader.GetSignedInUser(user, "invalid"));
        var result = _reader.GetSignedInUser(user, Guid.NewGuid().ToString());
        Assert.Equal("object-id", result.ExternalUserId);
        Assert.Equal(64, result.SessionKey!.Length);
    }

    [Fact]
    public void UnauthenticatedIdentity_IsRejected() =>
        Assert.Throws<AuthenticationFailedException>(() => _reader.GetSignedInUser(new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "attacker")]))));
}
