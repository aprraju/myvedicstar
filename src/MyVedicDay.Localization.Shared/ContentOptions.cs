namespace MyVedicDay.Localization.Data;

/// <summary>
/// Content settings. Bound from the <c>Content</c> configuration section.
/// </summary>
public sealed class ContentOptions
{
    public const string SectionName = "Content";

    /// <summary>
    /// Optional directory containing <c>{module}-content.{language}.json</c> files (e.g. <c>panchang-content.te.json</c>).
    /// Entries override the built-in content field by field, and files for new languages add those languages, without a
    /// redeploy. Modules may also read their own configuration files from here. Changes are read at application start.
    /// </summary>
    public string? Directory { get; set; }
}
