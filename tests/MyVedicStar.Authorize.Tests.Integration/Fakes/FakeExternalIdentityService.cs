using System.Collections.Concurrent;
using MyVedicStar.Authorize.Integration.Identity;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Authorize.Tests.Integration.Fakes;

/// <summary>Stands in for Microsoft Graph.</summary>
public sealed class FakeExternalIdentityService : IExternalIdentityService
{
    private readonly ConcurrentDictionary<string, ExternalUserProfile> _profiles = new();

    public bool IsUnavailable { get; set; }

    public void AddProfile(ExternalUserProfile profile) => _profiles[profile.ExternalUserId] = profile;

    public void Reset()
    {
        _profiles.Clear();
        IsUnavailable = false;
    }

    public Task<ExternalUserProfile> GetUserProfileAsync(string externalUserId, CancellationToken cancellationToken)
    {
        if (IsUnavailable)
        {
            throw new IntegrationException(
                "The identity provider is currently unavailable. Please try again later.",
                "MicrosoftGraph GetUser returned 503.");
        }

        return _profiles.TryGetValue(externalUserId, out var profile)
            ? Task.FromResult(profile)
            : throw new ResourceNotFoundException("The user profile could not be found.", "MicrosoftGraph returned 404.");
    }
}
