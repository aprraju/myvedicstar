namespace MyVedicStar.Authorize.Business.Options;

/// <summary>
/// Feature switches for the Authorize domain. Read once at startup, because they decide which services are registered.
/// </summary>
public sealed class FeatureFlagOptions
{
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// When <see langword="true"/>, signed-in users are stored in PostgreSQL and each sign-in is audited.
    /// When <see langword="false"/>, no database services are registered and nothing is persisted.
    /// </summary>
    public bool UserPersistence { get; set; }
}
