namespace MyVedicStar.VedicProfiles.Business;

/// <summary>
/// A participant in profile deletion. VedicProfiles owns the profile lifecycle; other modules own their own rows and
/// must not be referenced by this one, so deletion is extended by registration rather than by a direct call.
/// </summary>
/// <remarks>
/// <para>Implementations must be idempotent: a retried deletion runs the participants again, and each must treat
/// "nothing left to remove" as success rather than as an error.</para>
/// <para>The account id is passed explicitly because a participant cannot resolve it - ownership was already decided
/// by the time this is called, and a second resolution could disagree.</para>
/// </remarks>
public interface IVedicProfileDeletionParticipant
{
    /// <summary>Removes this module's data for the profile. Called inside the deletion flow, before the profile row is
    /// removed, so a failure aborts the deletion rather than orphaning data.</summary>
    Task RemoveAsync(Guid accountId, Guid profileId, CancellationToken token);
}

