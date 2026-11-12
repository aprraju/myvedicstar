using System.Net.Mail;

namespace MyVedicStar.Email.Shared;

/// <summary>Address validation shared by submission and SMTP configuration boundaries.</summary>
public static class EmailAddressValidation
{
    public static bool IsAddress(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 254 && !value.Any(char.IsControl) && value.Contains('@')
        && MailAddress.TryCreate(value, out var address) && address.Address == value;
}
