using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Integration;

/// <summary>Updates only explicitly changed name attributes on the verified Entra object.</summary>
public interface IGraphProfileClient
{
    Task UpdateNamesAsync(string issuer, string objectId, IReadOnlyDictionary<string, string?> changes, CancellationToken token);
}
internal sealed partial class GraphProfileClient(HttpClient http, IOptions<GraphProfileOptions> settings,
    TokenCredential credential, ILogger<GraphProfileClient> logger) : IGraphProfileClient
{
    public async Task UpdateNamesAsync(string issuer, string objectId, IReadOnlyDictionary<string, string?> changes, CancellationToken token)
    {
        var options = settings.Value;
        if (!options.Enabled || !Guid.TryParse(options.TenantId, out var tenantId) || !Guid.TryParse(objectId, out _) ||
            !Uri.TryCreate(issuer, UriKind.Absolute, out var authority) || authority.Scheme != "https" ||
            !(authority.Host.EndsWith(".ciamlogin.com", StringComparison.OrdinalIgnoreCase) || authority.Host == "login.microsoftonline.com") ||
            !authority.AbsolutePath.Split('/').Contains(tenantId.ToString("D"), StringComparer.OrdinalIgnoreCase))
            throw new IntegrationException("Name synchronization is unavailable for this account.");
        if (changes.Keys.Any(key => key is not ("givenName" or "surname" or "displayName"))) throw new IntegrationException("Unsupported name attribute.");
        if (changes.TryGetValue("displayName", out var displayName) && string.IsNullOrWhiteSpace(displayName))
            throw new IntegrationException("A display name cannot be cleared.");
        if (changes.Count == 0) return;
        var started = Stopwatch.GetTimestamp();
        int? status = null;
        try
        {
            var access = await credential.GetTokenAsync(new TokenRequestContext(["https://graph.microsoft.com/.default"]), token);
            using var request = new HttpRequestMessage(HttpMethod.Patch, "users/" + Uri.EscapeDataString(objectId)) { Content = JsonContent.Create(changes) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.Token);
            using var response = await http.SendAsync(request, token);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) throw new IntegrationException("Name synchronization could not be completed. Retry later.");
        }
        finally
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                var duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                LogCompleted(logger, status, duration);
            }
        }
    }
    [LoggerMessage(Level = LogLevel.Information, Message = "Microsoft Graph update names completed: status {Status}, duration {DurationMs}ms")]
    private static partial void LogCompleted(ILogger logger, int? status, double durationMs);
}
public static class GraphProfileRegistration
{
    public static IServiceCollection AddGraphProfileIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GraphProfileOptions>().Bind(configuration.GetSection(GraphProfileOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddHttpClient<IGraphProfileClient, GraphProfileClient>((provider, http) =>
        {
            http.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            http.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<GraphProfileOptions>>().Value.TimeoutSeconds);
        });
        services.AddSingleton<TokenCredential>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<GraphProfileOptions>>().Value;
            return options.Enabled && options.CredentialType == "ClientSecret"
                ? new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret)
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = options.ClientId });
        });
        return services;
    }
}
