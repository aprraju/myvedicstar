using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Integration.DependencyInjection;
using MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph;

namespace MyVedicStar.Authorize.Tests.Unit.Integration;

public sealed class MicrosoftGraphCredentialTests
{
    [Fact]
    public void Create_ClientSecret_ReturnsClientSecretCredential()
    {
        var credential = MicrosoftGraphCredentialFactory.Create(Options(MicrosoftGraphCredentialType.ClientSecret));

        Assert.IsType<ClientSecretCredential>(credential);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public void Create_ManagedIdentityFederation_ReturnsClientAssertionCredential(string managedIdentityClientId)
    {
        var options = Options(MicrosoftGraphCredentialType.ManagedIdentityFederation);
        options.ManagedIdentityClientId = managedIdentityClientId;

        var credential = MicrosoftGraphCredentialFactory.Create(options);

        Assert.IsType<ClientAssertionCredential>(credential);
    }

    [Fact]
    public void Registration_ClientSecretTypeWithoutSecret_FailsValidation()
    {
        using var services = BuildServices(new() { ["CredentialType"] = "ClientSecret", ["ClientSecret"] = "" });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value);
        Assert.Contains(nameof(MicrosoftGraphOptions.ClientSecret), exception.Message);
    }

    [Fact]
    public void Registration_ManagedIdentityFederationWithoutSecret_IsValidAndUsesAssertionCredential()
    {
        using var services = BuildServices(new()
        {
            ["CredentialType"] = "ManagedIdentityFederation",
            ["ClientSecret"] = "",
            ["FederatedTokenAudience"] = "api://AzureADTokenExchange",
        });

        Assert.Equal(MicrosoftGraphCredentialType.ManagedIdentityFederation, services.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value.CredentialType);
        Assert.IsType<ClientAssertionCredential>(services.GetRequiredService<TokenCredential>());
    }

    [Fact]
    public void Registration_ManagedIdentityFederationWithoutAudience_FailsValidation()
    {
        using var services = BuildServices(new() { ["CredentialType"] = "ManagedIdentityFederation", ["FederatedTokenAudience"] = "" });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value);
        Assert.Contains(nameof(MicrosoftGraphOptions.FederatedTokenAudience), exception.Message);
    }

    private static MicrosoftGraphOptions Options(MicrosoftGraphCredentialType credentialType) => new()
    {
        AuthorityHost = "https://login.microsoftonline.com/",
        TenantId = "tenant-id",
        ClientId = "client-id",
        ClientSecret = "client-secret",
        CredentialType = credentialType,
        FederatedTokenAudience = "api://AzureADTokenExchange",
    };

    private static ServiceProvider BuildServices(Dictionary<string, string?> overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["BaseUrl"] = "https://graph.microsoft.com/v1.0/",
            ["Scopes:0"] = "https://graph.microsoft.com/.default",
            ["AuthorityHost"] = "https://login.microsoftonline.com/",
            ["TenantId"] = "tenant-id",
            ["ClientId"] = "client-id",
            ["ClientSecret"] = "client-secret",
            ["Resilience:Retry:MaxRetryAttempts"] = "1",
        };

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => $"{MicrosoftGraphOptions.SectionName}:{setting.Key}", setting => setting.Value))
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddAuthorizeIntegrations(configuration)
            .BuildServiceProvider();
    }
}
