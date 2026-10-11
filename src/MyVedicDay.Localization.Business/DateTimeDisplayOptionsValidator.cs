using System.Globalization;
using Microsoft.Extensions.Options;
using NodaTime.Text;

namespace MyVedicDay.Localization;

/// <summary>
/// Rejects invalid date/time patterns and country codes at startup rather than at the first request.
/// </summary>
internal sealed class DateTimeDisplayOptionsValidator : IValidateOptions<DateTimeDisplayOptions>
{
    public ValidateOptionsResult Validate(string? name, DateTimeDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        CheckDatePattern(nameof(DateTimeDisplayOptions.DefaultDatePattern), options.DefaultDatePattern, failures);

        CheckTimePattern(nameof(DateTimeDisplayOptions.TimePattern), options.TimePattern, failures);
        CheckTimePattern(nameof(DateTimeDisplayOptions.ShortTimePattern), options.ShortTimePattern, failures);

        foreach (var (country, pattern) in options.CountryDatePatterns)
        {
            if (country.Length != 2 || !country.All(char.IsAsciiLetter))
            {
                failures.Add($"{nameof(DateTimeDisplayOptions.CountryDatePatterns)} key '{country}' is not an ISO 3166-1 alpha-2 code.");
            }

            CheckDatePattern($"{nameof(DateTimeDisplayOptions.CountryDatePatterns)}:{country}", pattern, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckTimePattern(string setting, string pattern, List<string> failures)
    {
        try
        {
            LocalTimePattern.Create(pattern, CultureInfo.InvariantCulture);
        }
        catch (InvalidPatternException exception)
        {
            failures.Add($"{setting} is invalid: {exception.Message}");
        }
    }

    private static void CheckDatePattern(string setting, string pattern, List<string> failures)
    {
        try
        {
            LocalDatePattern.Create(pattern, CultureInfo.InvariantCulture);
        }
        catch (InvalidPatternException exception)
        {
            failures.Add($"{setting} is invalid: {exception.Message}");
        }
    }
}
