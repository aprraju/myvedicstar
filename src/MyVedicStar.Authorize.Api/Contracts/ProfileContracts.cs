using MyVedicStar.Authorize.Business;
namespace MyVedicStar.API.Contracts;

/// <summary>Changes to the signed-in user's profile.</summary>
/// <param name="Revision">The revision the client last read; a stale revision is rejected.</param>
/// <param name="Changes">
/// Only the fields to change, keyed by <c>firstName</c>, <c>lastName</c>, <c>displayName</c>, <c>streetAddress</c>,
/// <c>streetAddress2</c>, <c>country</c>, <c>state</c>, <c>city</c>, <c>postalCode</c>, <c>email</c> or <c>phoneNumber</c>.
/// A blank value clears the field, except <c>displayName</c>, which cannot be cleared.
/// </param>
public sealed record UpdateProfileRequest(long Revision, Dictionary<string, string?> Changes);

/// <summary>The signed-in user's editable profile.</summary>
/// <remarks>
/// <c>NameSyncPending</c> is true when a saved first, last or display name has not reached Microsoft Entra yet;
/// the database copy is kept and returned, and <c>POST profile/sync</c> retries the update.
/// </remarks>
public sealed record ProfileResponse(Guid Id, string? FirstName, string? LastName, string? DisplayName, string? StreetAddress,
    string? StreetAddress2, string? Country, string? State, string? City, string? PostalCode,
    string? Email, string? PhoneNumber, long Revision, bool NameSyncPending)
{
    public static ProfileResponse From(ProfileResult p) => new(p.Id, p.FirstName, p.LastName, p.DisplayName,
        p.StreetAddress, p.StreetAddress2, p.Country, p.State, p.City, p.PostalCode, p.Email,
        p.PhoneNumber, p.Revision, p.NameSyncPending);
}
