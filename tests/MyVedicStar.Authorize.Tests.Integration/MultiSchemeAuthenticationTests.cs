using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace MyVedicStar.Authorize.Tests.Integration;

/// <summary>
/// Multi-scheme authentication from MyVedicStar.ApiProtection: tokens are routed by issuer, and only the
/// enabled provider (Entra External ID) can authenticate a caller.
/// </summary>
public sealed class MultiSchemeAuthenticationTests : IClassFixture<PersistenceDisabledApiFactory>
{
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";

    private readonly PersistenceDisabledApiFactory _factory;
    private readonly HttpClient _client;

    public MultiSchemeAuthenticationTests(PersistenceDisabledApiFactory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EntraToken_IsRoutedToEntraSchemeAndAccepted()
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("https://accounts.google.com")]   // Google is disabled in configuration
    [InlineData("https://appleid.apple.com")]     // Apple is disabled in configuration
    [InlineData("https://evil.example.com/tenant/v2.0")]
    [InlineData("https://other-tenant.ciamlogin.com/other-tenant/v2.0")]
    public async Task TokenFromAnyOtherIssuer_Returns401(string issuer)
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId)], issuer: issuer));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EntraTokenForAnotherAudience_Returns401()
    {
        var response = await PostUserInfoAsync(_factory.CreateToken([("oid", ObjectId)], audience: "another-api"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostUserInfoAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }
}

/// <summary>Host configured with the tenant's friendly subdomain instead of the tenant-ID host used in token issuers.</summary>
public sealed class FriendlyTenantDomainApiFactory : AuthorizeApiFactory
{
    protected override bool UserPersistenceEnabled => false;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Authentication:EntraExternalId:TenantDomain", "myvedicstarexternal.ciamlogin.com");
    }
}

public sealed class EntraSettingsValidationTests
{
    [Fact]
    public void Startup_FailsWhenTenantDomainIsNotTheTenantIdHost()
    {
        using var factory = new FriendlyTenantDomainApiFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(exception is AggregateException aggregate ? aggregate.InnerException : exception);
        Assert.Contains("TenantDomain must be", validation.Message);
    }
}
