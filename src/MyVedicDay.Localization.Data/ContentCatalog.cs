namespace MyVedicDay.Localization.Data;

/// <summary>
/// Display content for one language: entries keyed by <c>category.id</c>
/// (for example <c>periods.RahuKalam</c>, <c>elements.tithi</c>, <c>classifications.Avoid</c>, <c>nakshatra.Rohini</c>).
/// </summary>
/// <param name="Language">BCP 47 language tag, e.g. <c>en</c>, <c>te</c>, <c>hi</c>.</param>
/// <param name="Entries">Entries by key. Keys are case-sensitive.</param>
public sealed record ContentCatalog(string Language, IReadOnlyDictionary<string, ContentEntry> Entries);

/// <summary>
/// One content entry. Every field is optional so a translation can supply only some fields; missing fields fall
/// back to the default language.
/// </summary>
/// <param name="Name">Display name.</param>
/// <param name="Meaning">Full explanation of what the item means.</param>
/// <param name="Description">Short, app-friendly description.</param>
/// <param name="Message">One-line message for compact UI, e.g. "Avoid important new beginnings".</param>
/// <param name="Icon">Optional visual indicator, e.g. an emoji for a classification.</param>
public sealed record ContentEntry(
    string? Name,
    string? Meaning = null,
    string? Description = null,
    string? Message = null,
    string? Icon = null);
