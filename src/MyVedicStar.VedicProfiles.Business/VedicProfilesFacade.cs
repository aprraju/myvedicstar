using System.Security.Claims;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.VedicProfiles.Business;

internal sealed class VedicProfilesFacade(IVedicProfileAccountReader accounts, IVedicProfileRepository repository,
    VedicProfileValidator validator, IEnumerable<IVedicProfileDeletionParticipant> deletionParticipants)
    : IVedicProfilesFacade
{
    public async Task<IReadOnlyList<VedicProfileSummary>> ListAsync(ClaimsPrincipal user, CancellationToken token) =>
        await repository.ListAsync(await accounts.GetRegisteredAccountIdAsync(user, token), token);

    public async Task<VedicProfile> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken token) =>
        await ReadOwnedAsync(await accounts.GetRegisteredAccountIdAsync(user, token), id, token);

    public async Task<VedicProfile> CreateAsync(ClaimsPrincipal user, VedicProfileDetails details, CancellationToken token)
    {
        var accountId = await accounts.GetRegisteredAccountIdAsync(user, token);
        ArgumentNullException.ThrowIfNull(details);
        // Only Self may inherit the name; every other relationship must supply one.
        // When the account has no usable name, leave it blank so the validator returns
        // a 400 rather than inventing a value.
        if (details.Relationship == Relationship.Self && string.IsNullOrWhiteSpace(details.Name))
            details = details with { Name = await accounts.GetDefaultNameAsync(user, token) };
        validator.Validate(details);
        return await repository.CreateAsync(accountId, details, token);
    }

    public async Task<VedicProfile> ReplaceAsync(ClaimsPrincipal user, Guid id, VedicProfileDetails details,
        long revision, CancellationToken token)
    {
        var accountId = await accounts.GetRegisteredAccountIdAsync(user, token);
        var current = await ReadOwnedAsync(accountId, id, token);
        if (revision <= 0) throw new ValidationException(VedicProfileErrors.InvalidRevision);
        if (revision != current.Revision || revision == long.MaxValue)
            throw new VedicProfileConflictException(VedicProfileErrors.StaleRevision);
        validator.Validate(details);
        return await repository.ReplaceAsync(accountId, id, details, revision, token);
    }

    public async Task DeleteAsync(ClaimsPrincipal user, Guid id, CancellationToken token)
    {
        var accountId = await accounts.GetRegisteredAccountIdAsync(user, token);

        // Participants run BEFORE the profile row is removed. If one throws, the profile survives intact - an
        // orphaned profile is recoverable, whereas rows left behind for a profile that no longer exists are not
        // reachable by any endpoint and can never be cleaned up by the owner.
        foreach (var participant in deletionParticipants)
            await participant.RemoveAsync(accountId, id, token);

        if (!await repository.DeleteAsync(accountId, id, token))
            throw new ResourceNotFoundException(VedicProfileErrors.NotFound);
    }

    private async Task<VedicProfile> ReadOwnedAsync(Guid accountId, Guid id, CancellationToken token) =>
        await repository.GetAsync(accountId, id, token) ?? throw new ResourceNotFoundException(VedicProfileErrors.NotFound);
}
