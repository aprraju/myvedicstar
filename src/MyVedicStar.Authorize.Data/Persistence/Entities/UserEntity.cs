namespace MyVedicStar.Authorize.Data.Persistence.Entities;

/// <summary>A MyVedicStar user, keyed internally by <see cref="Id"/> and linked to the identity provider by <see cref="ExternalId"/>.</summary>
internal sealed class UserEntity
{
    public Guid Id { get; set; }

    /// <summary>Stable provider ID, unique together with Issuer.</summary>
    public string ExternalId { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public string? Email { get; set; }

    public string? GivenName { get; set; }

    public string? FamilyName { get; set; }

    public string? DisplayName { get; set; }

    public string? StreetAddress { get; set; }
    public string? StreetAddress2 { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? PhoneNumber { get; set; }
    public long ProfileRevision { get; set; }
    public bool ProfileManaged { get; set; }
    public bool FirstNameSyncPending { get; set; }
    public bool LastNameSyncPending { get; set; }
    public bool DisplayNameSyncPending { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public ICollection<UserLoginEntity> Logins { get; } = [];
}
