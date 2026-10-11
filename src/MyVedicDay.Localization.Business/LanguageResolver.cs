using Microsoft.Extensions.Options;

namespace MyVedicDay.Localization;

/// <summary>
/// Picks the response language: the first preferred tag that is supported exactly (<c>te-IN</c>) or by its base
/// language (<c>te-IN</c> → <c>te</c>); otherwise the default language. Unsupported requests are not errors.
/// </summary>
internal sealed class LanguageResolver(IOptions<LocalizationOptions> options)
{
    public string Resolve(IReadOnlyList<string>? preferredLanguages)
    {
        var supported = options.Value.GetEffectiveSupportedLanguages();

        foreach (var preferred in preferredLanguages ?? [])
        {
            if (string.IsNullOrWhiteSpace(preferred))
            {
                continue;
            }

            var tag = preferred.Trim();
            var match = supported.FirstOrDefault(language => language.Equals(tag, StringComparison.OrdinalIgnoreCase))
                ?? supported.FirstOrDefault(language => language.Equals(tag.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return options.Value.DefaultLanguage;
    }
}
