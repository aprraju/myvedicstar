using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization.Data;

namespace MyVedicDay.Localization;

/// <summary>
/// Resolves the response language and supplies content with per-field fallback to the default language.
/// </summary>
public interface ILocalizedContentProvider
{
    /// <param name="preferredLanguages">Language tags in order of preference (e.g. <c>?language=</c>, then <c>Accept-Language</c>).</param>
    /// <param name="tradition">Optional tradition key whose regional names take precedence.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<LocalizedContent> GetContentAsync(IReadOnlyList<string>? preferredLanguages, string? tradition, CancellationToken cancellationToken);
}

internal sealed partial class LocalizedContentProvider(
    IContentRepository content,
    LanguageResolver languageResolver,
    IOptions<LocalizationOptions> options,
    ILogger<LocalizedContentProvider> logger) : ILocalizedContentProvider
{
    public async Task<LocalizedContent> GetContentAsync(
        IReadOnlyList<string>? preferredLanguages, string? tradition, CancellationToken cancellationToken)
    {
        var language = languageResolver.Resolve(preferredLanguages);
        var defaultLanguage = options.Value.DefaultLanguage;

        var catalog = await content.GetCatalogAsync(language, cancellationToken);
        var defaultCatalog = language.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase)
            ? catalog
            : await content.GetCatalogAsync(defaultLanguage, cancellationToken);

        if (catalog is null)
        {
            LogCatalogMissing(language);
        }

        return new LocalizedContent(language, tradition, catalog, defaultCatalog);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Language {Language} is supported but has no content catalog; using the default language")]
    private partial void LogCatalogMissing(string language);
}
