using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph.Models;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Integration.Identity.MicrosoftGraph;

/// <summary>
/// Adapter that reads Entra External ID user profiles from Microsoft Graph using the application's own identity.
/// </summary>
internal sealed class MicrosoftGraphExternalIdentityService : IExternalIdentityService
{
    private const string ProviderName = "MicrosoftGraph";
    private const string OperationName = "GetUser";

    // Tied to the fields mapped below, so it lives with the code rather than in configuration.
    internal const string UserSelectQuery =
        "id,mail,otherMails,identities,givenName,surname";

    private const string EmailAddressSignInType = "emailAddress";

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly MicrosoftGraphOptions _options;
    private readonly ILogger<MicrosoftGraphExternalIdentityService> _logger;

    public MicrosoftGraphExternalIdentityService(
        HttpClient httpClient,
        TokenCredential credential,
        IOptions<MicrosoftGraphOptions> options,
        ILogger<MicrosoftGraphExternalIdentityService> logger)
    {
        _httpClient = httpClient;
        _credential = credential;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExternalUserProfile> GetUserProfileAsync(string externalUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalUserId))
        {
            throw new ValidationException("A user identifier is required.");
        }

        var stopwatch = Stopwatch.StartNew();
        HttpStatusCode? statusCode = null;

        try
        {
            using var request = await CreateGetUserRequestAsync(externalUserId, cancellationToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            statusCode = response.StatusCode;

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ResourceNotFoundException(
                    "The user profile could not be found.",
                    $"{ProviderName} returned 404 for the requested user.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new IntegrationException(
                    "The identity provider is currently unavailable. Please try again later.",
                    $"{ProviderName} {OperationName} returned {(int)response.StatusCode}.");
            }

            var graphUser = await response.Content.ReadFromJsonAsync<GraphUserResponse>(cancellationToken)
                ?? throw new IntegrationException(
                    "The identity provider returned an unexpected response.",
                    $"{ProviderName} {OperationName} returned an empty body.");

            LogCompleted(stopwatch, statusCode, success: true);
            return MapProfile(externalUserId, graphUser);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MyVedicStarException)
        {
            LogCompleted(stopwatch, statusCode, success: false);
            throw;
        }
        catch (Exception ex)
        {
            // Token acquisition failures, network errors, resilience timeouts / open circuit, malformed JSON.
            LogCompleted(stopwatch, statusCode, success: false, ex);
            throw new IntegrationException(
                "The identity provider is currently unavailable. Please try again later.",
                $"{ProviderName} {OperationName} failed: {ex.GetType().Name}.",
                ex);
        }
    }

    private async Task<HttpRequestMessage> CreateGetUserRequestAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var accessToken = await _credential.GetTokenAsync(new TokenRequestContext(_options.Scopes), cancellationToken);

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"users/{Uri.EscapeDataString(externalUserId)}?$select={UserSelectQuery}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
        return request;
    }

    private static ExternalUserProfile MapProfile(string externalUserId, GraphUserResponse graphUser) => new(
        ExternalUserId: graphUser.Id ?? externalUserId,
        Email: ResolveEmail(graphUser),
        GivenName: graphUser.GivenName,
        Surname: graphUser.Surname);

    /// <summary>
    /// Entra External ID often leaves <c>mail</c> empty for local accounts; the email sign-in name is then
    /// held in <c>identities</c>.
    /// </summary>
    private static string? ResolveEmail(GraphUserResponse graphUser) =>
        new[]
        {
            graphUser.Mail,
            graphUser.Identities?
                .FirstOrDefault(identity => string.Equals(identity.SignInType, EmailAddressSignInType, StringComparison.OrdinalIgnoreCase))?
                .IssuerAssignedId,
            graphUser.OtherMails?.FirstOrDefault(),
        }
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private void LogCompleted(Stopwatch stopwatch, HttpStatusCode? statusCode, bool success, Exception? exception = null)
    {
        stopwatch.Stop();
        var level = success ? LogLevel.Information : LogLevel.Warning;
        _logger.Log(
            level,
            exception,
            "{Provider} {Operation} completed in {DurationMs} ms. HttpStatus={HttpStatus} Success={Success}",
            ProviderName,
            OperationName,
            stopwatch.ElapsedMilliseconds,
            statusCode is null ? null : (int)statusCode,
            success);
    }
}
