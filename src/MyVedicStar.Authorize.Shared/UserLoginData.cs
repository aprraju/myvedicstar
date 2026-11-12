using System.Net;

namespace MyVedicStar.Authorize.Data.Users;

/// <summary>A sign-in to record. The login time is assigned by the database server.</summary>
/// <param name="UserId">Internal MyVedicStar user ID.</param>
/// <param name="IpAddress">Client IP address, if known.</param>
/// <param name="SessionKey">Session hash for idempotency; null only for legacy callers.</param>
/// <param name="Type">1 for login, 99 for explicit logout.</param>
public sealed record UserLoginData(Guid UserId, IPAddress? IpAddress, string? SessionKey = null, int Type = 1);
