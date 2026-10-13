using MyVedicStar.Authorize.Data.Users;
using MyVedicStar.Shared.Exceptions;
namespace MyVedicStar.Authorize.Business;

internal sealed class DisabledProfileRepository : IProfileRepository
{
    private static IntegrationException Disabled() => new("Profile management is unavailable.");
    public Task<ProfileSyncWork> ReadAsync(string issuer, string id, CancellationToken token) => throw Disabled();
    public Task<ProfileSyncWork> UpdateAsync(string issuer, string id, long revision, IReadOnlyDictionary<string, string?> changes, CancellationToken token) => throw Disabled();
    public Task<ManagedProfile> SynchronizeAsync(string issuer, string id, Func<ProfileSyncWork, CancellationToken, Task> synchronize, CancellationToken token) => throw Disabled();
    public Task<bool> MarkSynchronizedAsync(string issuer, string id, long revision, CancellationToken token) => throw Disabled();
}
