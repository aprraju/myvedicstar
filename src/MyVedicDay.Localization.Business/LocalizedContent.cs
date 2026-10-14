using MyVedicDay.Localization.Data;

namespace MyVedicDay.Localization;

/// <summary>
/// Localized text for an item: display name and optional explanations.
/// </summary>
/// <param name="Name">Display name.</param>
/// <param name="Meaning">Full explanation.</param>
/// <param name="Description">Short, app-friendly description.</param>
/// <param name="Message">One-line message for compact UI.</param>
/// <param name="Icon">Optional visual indicator.</param>
public sealed record LocalizedEntry(string Name, string? Meaning, string? Description, string? Message, string? Icon, string? ShortName = null, string? CompactName = null);

/// <summary>
/// Content for one language and (optionally) one tradition. Keys are <c>{category}.{id}</c>; a tradition-specific key is
/// <c>{tradition}/{category}.{id}</c>. Each field resolves in this order: tradition-specific entry in the requested
/// language → generic entry in the requested language → tradition-specific entry in the default language → generic entry
/// in the default language → the id itself.
/// </summary>
public sealed class LocalizedContent
{
    private readonly string? _tradition;
    private readonly ContentCatalog? _catalog;
    private readonly ContentCatalog? _defaultCatalog;

    internal LocalizedContent(string language, string? tradition, ContentCatalog? catalog, ContentCatalog? defaultCatalog)
    {
        Language = language;
        _tradition = tradition;
        _catalog = catalog;
        _defaultCatalog = defaultCatalog;
    }

    /// <summary>The resolved response language.</summary>
    public string Language { get; }

    public string GetName(string category, string id) => GetEntry(category, id).Name;

    public LocalizedEntry GetEntry(string category, string id)
    {
        var key = $"{category}.{id}";
        var traditionKey = _tradition is null ? null : $"{_tradition}/{key}";

        ContentEntry?[] candidates =
        [
            Lookup(_catalog, traditionKey),
            Lookup(_catalog, key),
            Lookup(_defaultCatalog, traditionKey),
            Lookup(_defaultCatalog, key),
        ];

        return new LocalizedEntry(
            candidates.Select(entry => entry?.Name).FirstOrDefault(value => value is not null) ?? id,
            candidates.Select(entry => entry?.Meaning).FirstOrDefault(value => value is not null),
            candidates.Select(entry => entry?.Description).FirstOrDefault(value => value is not null),
            candidates.Select(entry => entry?.Message).FirstOrDefault(value => value is not null),
            candidates.Select(entry => entry?.Icon).FirstOrDefault(value => value is not null),
            candidates.Select(entry => entry?.ShortName).FirstOrDefault(value => value is not null),
            candidates.Select(entry => entry?.CompactName).FirstOrDefault(value => value is not null));
    }

    /// <summary>Ids present in a category (generic entries only), in the requested or the default language.</summary>
    public IEnumerable<string> GetIds(string category)
    {
        var prefix = category + ".";
        return (_defaultCatalog?.Entries.Keys ?? [])
            .Concat(_catalog?.Entries.Keys ?? [])
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(key => key[prefix.Length..])
            .Distinct(StringComparer.Ordinal);
    }

    private static ContentEntry? Lookup(ContentCatalog? source, string? key) =>
        key is null ? null : source?.Entries.GetValueOrDefault(key);
}
