using System.ComponentModel.DataAnnotations;
namespace MyVedicStar.Authorize.Business.Options;

/// <summary>Bounds post-commit lifecycle work; failures are repaired on the next sign-in.</summary>
public sealed class AccountInitializationOptions
{
    public const string SectionName = "AccountInitialization";
    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 3;
}
