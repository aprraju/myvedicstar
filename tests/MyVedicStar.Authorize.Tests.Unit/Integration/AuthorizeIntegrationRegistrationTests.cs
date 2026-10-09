using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Integration.DependencyInjection;
using MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph;

namespace MyVedicStar.Authorize.Tests.Unit.Integration;

public sealed class AuthorizeIntegrationRegistrationTests
{
    [Fact]
    public void Registration_WithClientSecret_UsesClientSecretCredential()
    {
        using var services = BuildServices(clientSecret: "client-secret");

        Assert.IsType<ClientSecretCredential>(services.GetRequiredService<TokenCredential>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Registration_WithoutClientSecret_FailsValidation(string? clientSecret)
    {
        using var services = BuildServices(clientSecret);

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<MicrosoftGraphOptions>>().Value);
        Assert.Contains(nameof(MicrosoftGraphOptions.ClientSecret), exception.Message);
    }

    private static ServiceProvider BuildServices(string? clientSecret)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{MicrosoftGraphOptions.SectionName}:BaseUrl"] = "https://graph.microsoft.com/v1.0/",
                [$"{MicrosoftGraphOptions.SectionName}:Scopes:0"] = "https://graph.microsoft.com/.default",
                [$"{MicrosoftGraphOptions.SectionName}:AuthorityHost"] = "https://login.microsoftonline.com/",
                [$"{MicrosoftGraphOptions.SectionName}:TenantId"] = "tenant-id",
                [$"{MicrosoftGraphOptions.SectionName}:ClientId"] = "client-id",
                [$"{MicrosoftGraphOptions.SectionName}:ClientSecret"] = clientSecret,
                [$"{MicrosoftGraphOptions.SectionName}:Resilience:Retry:MaxRetryAttempts"] = "1",
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddAuthorizeIntegrations(configuration)
            .BuildServiceProvider();
    }
}
