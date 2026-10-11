using System.Reflection;

namespace MyVedicDay.Localization.Data;

/// <summary>
/// A module assembly that embeds content catalogs. Register with
/// <see cref="LocalizationDataServiceCollectionExtensions.AddContentSource"/>.
/// </summary>
/// <remarks>
/// Embed catalogs with <c>LogicalName="MyVedicDay.Content.%(Filename)%(Extension)"</c> and <c>WithCulture="false"</c>,
/// naming files <c>{module}-content.{language}.json</c>. Content keys are global, so each module owns distinct
/// categories (e.g. Panchang: <c>periods.*</c>, <c>elements.*</c>; a Muhurta module: <c>muhurta.*</c>).
/// </remarks>
public sealed record ContentSource(Assembly Assembly)
{
    public const string ResourcePrefix = "MyVedicDay.Content.";

    internal static bool IsContentResource(string resourceName) =>
        resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
        && resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}
