using MyVedicStar.Kundali.Business;
using MyVedicStar.VedicProfiles.Business;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Removes this module's Kundali data when a profile is deleted. Registered as a deletion participant so VedicProfiles
/// never references Kundali - the dependency runs one way only.
/// </summary>
/// <remarks>
/// Public because it is registered from the composition project, which references both modules but must not be
/// bypassed by a module-level reference. Idempotent by construction: the underlying delete is account-scoped and removes whatever exists, so a second run
/// removes nothing and succeeds. That matters because deletion can be retried after a transient failure.
/// </remarks>
public sealed class KundaliProfileDeletionParticipant(IKundaliRepository repository)
    : IVedicProfileDeletionParticipant
{
    public async Task RemoveAsync(Guid accountId, Guid profileId, CancellationToken token)
    {
        // Nothing is asserted about the count. A profile with no Kundali data is not an error - it is the common case
        // for a profile that was created and never generated.
        _ = await repository.DeleteForProfileAsync(accountId, profileId, token);
    }
}

