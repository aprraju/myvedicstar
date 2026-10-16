namespace MyVedicStar.Authorize.Business;

/// <summary>Minimal profile of the signed-in user.</summary>
/// <param name="Id">The user's object ID in the identity provider (the token's user ID claim).</param>
/// <param name="Name">The stored display name (from the token's <c>name</c> claim or a profile edit); never derived from first and last name.</param>
/// <param name="Email">Email address, if the token carries one.</param>
/// <param name="FirstName">The stored first name.</param>
/// <param name="LastName">The stored last name.</param>
/// <remarks>With user persistence disabled, the names come straight from the token.</remarks>
public sealed record UserInfo(string Id, string? Name, string? Email, string? FirstName, string? LastName);
