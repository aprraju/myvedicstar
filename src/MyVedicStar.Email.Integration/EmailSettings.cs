using System.ComponentModel.DataAnnotations;
using MyVedicStar.Email.Shared;

namespace MyVedicStar.Email.Integration;

public sealed class EmailSettings : IValidatableObject
{
    public const string SectionName = "EmailSettings";
    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SecurityMode { get; set; } = "StartTls";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "MyVedicStar";
    public string ToEmail { get; set; } = "contact@myvedicstar.com";
    public int TimeoutSeconds { get; set; } = 30;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled) yield break;
        if (string.IsNullOrWhiteSpace(SmtpHost) || SmtpHost.Any(char.IsControl))
            yield return new("EmailSettings:SmtpHost is required.");
        if (SmtpPort is < 1 or > 65535 || TimeoutSeconds is < 1 or > 120)
            yield return new("EmailSettings port or timeout is invalid.");
        if (SecurityMode is not ("StartTls" or "SslOnConnect"))
            yield return new("EmailSettings:SecurityMode must be StartTls or SslOnConnect.");
        if (!IsAddress(FromEmail) || FromName.Any(char.IsControl))
            yield return new("EmailSettings sender is invalid.");
        if (!string.Equals(ToEmail, "contact@myvedicstar.com", StringComparison.OrdinalIgnoreCase))
            yield return new("EmailSettings:ToEmail must be contact@myvedicstar.com.");
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            yield return new("EmailSettings SMTP credentials are required.");
    }

    public static bool IsAddress(string? value) => EmailAddressValidation.IsAddress(value);
}
