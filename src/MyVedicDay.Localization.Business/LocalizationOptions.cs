using System.ComponentModel.DataAnnotations;

namespace MyVedicDay.Localization;

/// <summary>
/// Response languages. Bound from the <c>Localization</c> configuration section and validated at startup.
/// </summary>
/// <remarks>
/// Adding a language: provide <c>panchang-content.{language}.json</c> (built in, or in <c>Content:Directory</c>),
/// then add the tag to <see cref="SupportedLanguages"/>. Untranslated fields fall back to <see cref="DefaultLanguage"/>.
/// </remarks>
public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    /// <summary>Language used when no requested language is supported, and for untranslated fields.</summary>
    [Required]
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>
    /// Language tags clients may request, e.g. <c>["en", "te", "hi", "ta"]</c>. When empty, only
    /// <see cref="DefaultLanguage"/> is supported. (Empty by default because the configuration binder appends to
    /// array defaults rather than replacing them.)
    /// </summary>
    public string[] SupportedLanguages { get; set; } = [];

    /// <summary>The supported languages, always including <see cref="DefaultLanguage"/>.</summary>
    public IReadOnlyList<string> GetEffectiveSupportedLanguages() =>
        SupportedLanguages.Length == 0 ? [DefaultLanguage] : SupportedLanguages;
}
