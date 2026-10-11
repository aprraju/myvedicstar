using System.Text.Json;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization.Data;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Panchang.Data.Guidance;

/// <summary>
/// Reads the embedded <c>panchang-guidance-rules.json</c>, or the file of the same name in
/// <see cref="ContentOptions.Directory"/> when present (it replaces the built-in rules entirely). Loaded once.
/// </summary>
internal sealed class JsonGuidanceRulesRepository(IOptions<ContentOptions> options) : IGuidanceRulesRepository
{
    private const string FileName = "panchang-guidance-rules.json";
    private const string ResourceName = "MyVedicDay.Panchang.GuidanceRules.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly Lazy<GuidanceRulesDocument> _rules = new(() => Load(options.Value));

    public Task<GuidanceRulesDocument> GetRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(_rules.Value);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DataAccessException("Daily-guidance rules could not be loaded.", exception);
        }
    }

    private static GuidanceRulesDocument Load(ContentOptions options)
    {
        var overridePath = string.IsNullOrWhiteSpace(options.Directory) ? null : Path.Combine(options.Directory, FileName);
        using var stream = overridePath is not null && File.Exists(overridePath)
            ? File.OpenRead(overridePath)
            : typeof(JsonGuidanceRulesRepository).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        return JsonSerializer.Deserialize<GuidanceRulesDocument>(stream, SerializerOptions)
            ?? throw new JsonException("Daily-guidance rules file is empty.");
    }
}
