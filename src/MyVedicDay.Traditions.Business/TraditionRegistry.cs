using System.Text.Json;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization.Data;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Traditions;

/// <summary>
/// The regional traditions a user can choose (Telugu, Tamil, North Indian…). Shared by every domain: each domain maps
/// a tradition to its own rules (Panchang: which timings to show; Muhurta: which rules to apply).
/// </summary>
public interface ITraditionRegistry
{
    /// <summary>Tradition keys in display order.</summary>
    Task<IReadOnlyList<string>> GetKeysAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the canonical key for <paramref name="requested"/> (case-insensitive), or the configured default when omitted.
    /// </summary>
    /// <exception cref="ValidationException">Unknown tradition.</exception>
    Task<string> ResolveAsync(string? requested, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the embedded <c>traditions.json</c>; a <c>traditions.json</c> in <see cref="ContentOptions.Directory"/>
/// replaces the list. Loaded once and kept in memory.
/// </summary>
internal sealed class TraditionRegistry(IOptions<TraditionsOptions> options, IOptions<ContentOptions> contentOptions) : ITraditionRegistry
{
    private const string FileName = "traditions.json";
    private const string ResourceName = "MyVedicDay.Traditions.Registry.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly Lazy<IReadOnlyList<string>> _keys = new(() => Load(contentOptions.Value));

    public Task<IReadOnlyList<string>> GetKeysAsync(CancellationToken cancellationToken) => Task.FromResult(GetKeys());

    public Task<string> ResolveAsync(string? requested, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(requested) ? options.Value.DefaultTradition : requested.Trim();
        var keys = GetKeys();

        return Task.FromResult(
            keys.FirstOrDefault(candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException("tradition", $"Unsupported tradition. Supported traditions: {string.Join(", ", keys)}."));
    }

    private IReadOnlyList<string> GetKeys()
    {
        try
        {
            return _keys.Value;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DataAccessException("The tradition list could not be loaded.", exception);
        }
    }

    private static IReadOnlyList<string> Load(ContentOptions contentOptions)
    {
        var overridePath = string.IsNullOrWhiteSpace(contentOptions.Directory) ? null : Path.Combine(contentOptions.Directory, FileName);
        using var stream = overridePath is not null && File.Exists(overridePath)
            ? File.OpenRead(overridePath)
            : typeof(TraditionRegistry).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        var file = JsonSerializer.Deserialize<TraditionFile>(stream, SerializerOptions) ?? throw new JsonException("Tradition list is empty.");
        return file.Traditions.Count > 0 ? file.Traditions : throw new JsonException("Tradition list is empty.");
    }

    private sealed record TraditionFile(IReadOnlyList<string> Traditions);
}
