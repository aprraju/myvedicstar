using System.Text.Json;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization.Data;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Festivals.Data;

/// <summary>
/// Reads the embedded <c>festival-rules.json</c>, or the file of the same name in <see cref="ContentOptions.Directory"/>
/// when present (it replaces the built-in rules entirely). Loaded once.
/// </summary>
internal sealed class JsonFestivalRulesRepository(IOptions<ContentOptions> options) : IFestivalRulesRepository
{
    private const string FileName = "festival-rules.json";
    private const string ResourceName = "MyVedicDay.Festivals.Rules.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly Lazy<FestivalRulesDocument> _rules = new(() => Load(options.Value));

    public Task<FestivalRulesDocument> GetRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(_rules.Value);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DataAccessException("Festival rules could not be loaded.", exception);
        }
    }

    private static FestivalRulesDocument Load(ContentOptions options)
    {
        var overridePath = string.IsNullOrWhiteSpace(options.Directory) ? null : Path.Combine(options.Directory, FileName);
        using var stream = overridePath is not null && File.Exists(overridePath)
            ? File.OpenRead(overridePath)
            : typeof(JsonFestivalRulesRepository).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        return JsonSerializer.Deserialize<FestivalRulesDocument>(stream, SerializerOptions)
            ?? throw new JsonException("Festival rules file is empty.");
    }
}
