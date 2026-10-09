namespace MyVedicStar.Authorize.Business.Users;

/// <summary>Builds a display name from name parts.</summary>
internal static class DisplayNameFormatter
{
    /// <summary>
    /// Joins the trimmed, non-empty parts with a single space: "John Smith", "John", "Smith",
    /// or <see langword="null"/> when both are missing.
    /// </summary>
    public static string? Format(string? givenName, string? surname)
    {
        var parts = new[] { givenName?.Trim(), surname?.Trim() }
            .Where(part => !string.IsNullOrEmpty(part));

        var name = string.Join(' ', parts);
        return name.Length == 0 ? null : name;
    }
}
