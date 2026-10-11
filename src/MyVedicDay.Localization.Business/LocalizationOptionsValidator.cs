using Microsoft.Extensions.Options;

namespace MyVedicDay.Localization;

internal sealed class LocalizationOptionsValidator : IValidateOptions<LocalizationOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.GetEffectiveSupportedLanguages().Contains(options.DefaultLanguage, StringComparer.OrdinalIgnoreCase)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{nameof(LocalizationOptions.DefaultLanguage)} must be one of {nameof(LocalizationOptions.SupportedLanguages)}.");
    }
}
