using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Localization.Data;

/// <summary>
/// Combines the content catalogs embedded in every registered module assembly (resources named
/// <c>MyVedicDay.Content.{module}-content.{language}.json</c>) into one catalog per language, then merges
/// <c>*-content.{language}.json</c> files from <see cref="ContentOptions.Directory"/> over them field by field.
/// Catalogs are loaded once and kept in memory.
/// </summary>
internal sealed partial class JsonContentRepository(
    IEnumerable<ContentSource> sources, IOptions<ContentOptions> options, ILogger<JsonContentRepository> logger)
    : IContentRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly Lazy<IReadOnlyDictionary<string, ContentCatalog>> _catalogs =
        new(() => Load(sources.Select(source => source.Assembly).Distinct(), options.Value, logger));

    public Task<ContentCatalog?> GetCatalogAsync(string language, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return Task.FromResult(GetCatalogs().GetValueOrDefault(language));
    }

    private IReadOnlyDictionary<string, ContentCatalog> GetCatalogs()
    {
        try
        {
            return _catalogs.Value;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DataAccessException("Display content could not be loaded.", exception);
        }
    }

    private static Dictionary<string, ContentCatalog> Load(IEnumerable<Assembly> assemblies, ContentOptions options, ILogger logger)
    {
        var catalogs = new Dictionary<string, ContentCatalog>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            foreach (var resourceName in assembly.GetManifestResourceNames().Where(ContentSource.IsContentResource))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                Merge(catalogs, Read(stream));
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Directory))
        {
            if (Directory.Exists(options.Directory))
            {
                foreach (var path in Directory.EnumerateFiles(options.Directory, "*-content.*.json"))
                {
                    using var stream = File.OpenRead(path);
                    Merge(catalogs, Read(stream));
                    LogOverrideLoaded(logger, Path.GetFileName(path));
                }
            }
            else
            {
                LogOverrideDirectoryMissing(logger);
            }
        }

        return catalogs;
    }

    private static ContentCatalog Read(Stream stream)
    {
        var catalog = JsonSerializer.Deserialize<ContentCatalog>(stream, SerializerOptions)
            ?? throw new JsonException("Content file is empty.");

        return catalog.Language is not null && LanguageTag().IsMatch(catalog.Language)
            ? catalog
            : throw new JsonException($"Content file has an invalid language tag '{catalog.Language}'.");
    }

    private static void Merge(Dictionary<string, ContentCatalog> catalogs, ContentCatalog incoming)
    {
        if (!catalogs.TryGetValue(incoming.Language, out var existing))
        {
            catalogs[incoming.Language] = incoming;
            return;
        }

        var merged = new Dictionary<string, ContentEntry>(existing.Entries, StringComparer.Ordinal);
        foreach (var (key, entry) in incoming.Entries)
        {
            merged[key] = merged.TryGetValue(key, out var current)
                ? new ContentEntry(
                    entry.Name ?? current.Name,
                    entry.Meaning ?? current.Meaning,
                    entry.Description ?? current.Description,
                    entry.Message ?? current.Message,
                    entry.Icon ?? current.Icon,
                    entry.ShortName ?? current.ShortName,
                    entry.CompactName ?? current.CompactName)
                : entry;
        }

        catalogs[incoming.Language] = existing with { Entries = merged };
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex LanguageTag();

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded content override {FileName}")]
    private static partial void LogOverrideLoaded(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Configured content directory does not exist; using built-in content only")]
    private static partial void LogOverrideDirectoryMissing(ILogger logger);
}
