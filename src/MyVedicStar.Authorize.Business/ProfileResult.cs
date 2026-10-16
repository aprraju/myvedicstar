using MyVedicStar.Authorize.Data.Users;
namespace MyVedicStar.Authorize.Business;

/// <summary>Business result for the authenticated account's editable profile.</summary>
public sealed record ProfileResult(Guid Id, string? FirstName, string? LastName, string? DisplayName, string? StreetAddress,
    string? StreetAddress2, string? Country, string? State, string? City, string? PostalCode,
    string? Email, string? PhoneNumber, long Revision, bool NameSyncPending)
{
    internal static ProfileResult From(ManagedProfile value) => new(value.Id, value.FirstName,
        value.LastName, value.DisplayName, value.StreetAddress, value.StreetAddress2, value.Country, value.State,
        value.City, value.PostalCode, value.Email, value.PhoneNumber, value.Revision, value.NameSyncPending);
}
