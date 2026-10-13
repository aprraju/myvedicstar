using MyVedicStar.Authorize.Business;
namespace MyVedicStar.API.Contracts;

public sealed record UpdateProfileRequest(long Revision, Dictionary<string, string?> Changes);
public sealed record ProfileResponse(Guid Id, string? FirstName, string? LastName, string? StreetAddress,
    string? StreetAddress2, string? Country, string? State, string? City, string? PostalCode,
    string? Email, string? PhoneNumber, long Revision, bool NameSyncPending)
{
    public static ProfileResponse From(ProfileResult p) => new(p.Id, p.FirstName, p.LastName,
        p.StreetAddress, p.StreetAddress2, p.Country, p.State, p.City, p.PostalCode, p.Email,
        p.PhoneNumber, p.Revision, p.NameSyncPending);
}
