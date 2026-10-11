using System.Net;

namespace MyVedicStar.Authorize.Data.Persistence.Entities;

/// <summary>Audit record of one sign-in by a user.</summary>
internal sealed class UserLoginEntity
{
    public long Id { get; set; }

    public Guid UserId { get; set; }
    public int Type { get; set; } = 1;
    public string? SessionKey { get; set; }

    /// <summary>Set by the database server (UTC) when the row is inserted.</summary>
    public DateTime LoginTimeUtc { get; set; }

    public IPAddress? IpAddress { get; set; }

    public UserEntity User { get; set; } = null!;
}
