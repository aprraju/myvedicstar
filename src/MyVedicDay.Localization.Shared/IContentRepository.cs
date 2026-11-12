namespace MyVedicDay.Localization.Data;

/// <summary>
/// Source of localized display content for every module. The built-in implementation reads JSON catalogs embedded in
/// registered module assemblies, optionally overridden from a configured directory; a database or CMS can replace it.
/// </summary>
public interface IContentRepository
{
    /// <summary>Returns the catalog for an exact language tag, or <see langword="null"/> if none exists.</summary>
    /// <exception cref="MyVedicDay.Shared.Exceptions.DataAccessException">Content could not be read.</exception>
    Task<ContentCatalog?> GetCatalogAsync(string language, CancellationToken cancellationToken);
}
