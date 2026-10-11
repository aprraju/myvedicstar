using System.Text.Json;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization.Data;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Panchang.Data.Content;

/// <summary>
/// Reads the embedded <c>panchang-traditions.json</c> and applies <c>panchang-traditions.json</c> from
/// <see cref="ContentOptions.Directory"/>: profiles with an existing key replace it, new keys are appended.
/// Loaded once and kept in memory.
/// </summary>
internal sealed class JsonTraditionProfileRepository(IOptions<ContentOptions> options) : ITraditionProfileRepository
{
    private const string FileName = "panchang-traditions.json";
    private const string ResourceName = "MyVedicDay.Panchang.TraditionProfiles.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly Lazy<IReadOnlyList<TraditionProfile>> _profiles = new(() => Load(options.Value));

    public Task<IReadOnlyList<TraditionProfile>> GetProfilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(_profiles.Value);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DataAccessException("Panchang tradition profiles could not be loaded.", exception);
        }
    }

    private static List<TraditionProfile> Load(ContentOptions options)
    {
        using var embedded = typeof(JsonTraditionProfileRepository).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        var profiles = Read(embedded);

        var overridePath = string.IsNullOrWhiteSpace(options.Directory) ? null : Path.Combine(options.Directory, FileName);
        if (overridePath is not null && File.Exists(overridePath))
        {
            using var stream = File.OpenRead(overridePath);
            foreach (var profile in Read(stream))
            {
                var index = profiles.FindIndex(existing => existing.Key.Equals(profile.Key, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    profiles[index] = profile;
                }
                else
                {
                    profiles.Add(profile);
                }
            }
        }

        return profiles;
    }

    private static List<TraditionProfile> Read(Stream stream) =>
        [.. (JsonSerializer.Deserialize<TraditionFile>(stream, SerializerOptions) ?? throw new JsonException("Tradition file is empty.")).Traditions];

    private sealed record TraditionFile(IReadOnlyList<TraditionProfile> Traditions);
}
