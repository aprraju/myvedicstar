using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization.Data;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Muhurta.Data;

/// <summary>Reads the embedded rules, or the override file in <see cref="ContentOptions.Directory"/>. Loaded once.</summary>
internal sealed class JsonMuhurtaRulesRepository(IOptions<ContentOptions> options) : IMuhurtaRulesRepository
{
    private const string FileName = "muhurta-activities.json";
    private const string ResourceName = "MyVedicDay.Muhurta.Activities.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly Lazy<MuhurtaRulesDocument> _rules = new(() => Load(options.Value));

    public Task<MuhurtaRulesDocument> GetRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(_rules.Value);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DataAccessException("Muhurta rules could not be loaded.", exception);
        }
    }

    private static MuhurtaRulesDocument Load(ContentOptions options)
    {
        var overridePath = string.IsNullOrWhiteSpace(options.Directory) ? null : Path.Combine(options.Directory, FileName);
        using var stream = overridePath is not null && File.Exists(overridePath)
            ? File.OpenRead(overridePath)
            : typeof(JsonMuhurtaRulesRepository).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        return JsonSerializer.Deserialize<MuhurtaRulesDocument>(stream, SerializerOptions)
            ?? throw new JsonException("Muhurta rules file is empty.");
    }
}

/// <summary>Reads the embedded prompts, or the override file in <see cref="ContentOptions.Directory"/>. Loaded once.</summary>
internal sealed class JsonMuhurtaPromptRepository(IOptions<ContentOptions> options) : IMuhurtaPromptRepository
{
    private const string FileName = "muhurta-ai-prompts.json";
    private const string ResourceName = "MyVedicDay.Muhurta.AiPrompts.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly Lazy<MuhurtaAiPrompts> _prompts = new(() => Load(options.Value));

    public Task<MuhurtaAiPrompts> GetPromptsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Task.FromResult(_prompts.Value);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new DataAccessException("Muhurta AI prompts could not be loaded.", exception);
        }
    }

    private static MuhurtaAiPrompts Load(ContentOptions options)
    {
        var overridePath = string.IsNullOrWhiteSpace(options.Directory) ? null : Path.Combine(options.Directory, FileName);
        using var stream = overridePath is not null && File.Exists(overridePath)
            ? File.OpenRead(overridePath)
            : typeof(JsonMuhurtaPromptRepository).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        return JsonSerializer.Deserialize<MuhurtaAiPrompts>(stream, SerializerOptions)
            ?? throw new JsonException("Muhurta AI prompts file is empty.");
    }
}

/// <summary>Registers the Muhurta data: rules and display content (contributed to the shared content repository).</summary>
public static class MuhurtaDataServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IMuhurtaRulesRepository"/> and this assembly's <c>muhurta-content.*.json</c> catalogs.</summary>
    public static IServiceCollection AddMuhurtaData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddContentRepository(configuration);
        services.AddContentSource(typeof(MuhurtaDataServiceCollectionExtensions).Assembly);
        services.TryAddSingleton<IMuhurtaRulesRepository, JsonMuhurtaRulesRepository>();
        services.TryAddSingleton<IMuhurtaPromptRepository, JsonMuhurtaPromptRepository>();

        return services;
    }
}
