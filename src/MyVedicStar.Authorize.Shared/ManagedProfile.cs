namespace MyVedicStar.Authorize.Data.Users;

public sealed record ManagedProfile(Guid Id, string? FirstName, string? LastName, string? DisplayName, string? StreetAddress,
    string? StreetAddress2, string? Country, string? State, string? City, string? PostalCode,
    string? Email, string? PhoneNumber, long Revision, bool NameSyncPending);

/// <summary>A profile and which name attributes still have to be written to the identity provider.</summary>
public sealed record ProfileSyncWork(ManagedProfile Profile, bool FirstNamePending, bool LastNamePending, bool DisplayNamePending = false);

public interface IProfileRepository
{
    Task<ProfileSyncWork> ReadAsync(string issuer, string externalId, CancellationToken token);
    Task<ProfileSyncWork> UpdateAsync(string issuer, string externalId, long revision,
        IReadOnlyDictionary<string, string?> changes, CancellationToken token);
    Task<ManagedProfile> SynchronizeAsync(string issuer, string externalId,
        Func<ProfileSyncWork, CancellationToken, Task> synchronize, CancellationToken token);
    Task<bool> MarkSynchronizedAsync(string issuer, string externalId, long revision, CancellationToken token);
}
