using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MyVedicDay.Localization.Data;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Muhurta.Data;

/// <summary>
/// The versioned Muhurta rules (<c>muhurta-activities.json</c>, ADR-0011): traditional value groups, default rules,
/// the activities with their partial overrides, and partial overrides per tradition. Interpretation only, no
/// astronomy. Values are the enum names of the calculation engine.
/// </summary>
/// <param name="Version">Rules version, returned with every result and part of the cache key; bump it on every change.</param>
/// <param name="Groups">Traditional groups per limb (<c>tithi</c>, <c>nakshatra</c>, <c>yoga</c>, <c>karana</c>, <c>vara</c>), e.g. <c>nakshatra.dhruva</c>.</param>
/// <param name="Defaults">Rules every activity starts from.</param>
/// <param name="Activities">Activities in display order.</param>
/// <param name="Traditions">Overrides per tradition: <c>*</c> for every activity, or an activity key.</param>
public sealed record MuhurtaRulesDocument(
    string Version,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Groups,
    MuhurtaRuleSet Defaults,
    IReadOnlyList<MuhurtaActivityDefinition> Activities,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, MuhurtaRuleSet>>? Traditions);

/// <summary>An activity the user can choose.</summary>
/// <param name="Key">Stable key, e.g. <c>business</c>; also the content key <c>muhurtaActivities.{key}</c>.</param>
/// <param name="Icon">Icon key for clients, e.g. <c>briefcase</c>; unknown keys fall back to a generic icon.</param>
/// <param name="Completeness"><c>full</c>, or <c>partial</c> when traditional factors the engine cannot evaluate yet matter (Lagna, planets, personal charts).</param>
/// <param name="RequiresInput">The user describes the activity in free text (<c>other</c>); it is mapped to another activity.</param>
/// <param name="Keywords">Words that map free text to this activity.</param>
/// <param name="Rules">Partial rules overriding the defaults; null for activities that only map to others.</param>
public sealed record MuhurtaActivityDefinition(
    string Key,
    string Icon,
    string? Completeness,
    bool RequiresInput,
    IReadOnlyList<string>? Keywords,
    MuhurtaRuleSet? Rules);

/// <summary>
/// A full (defaults) or partial (override) rule set. Every field is optional in an override; later layers win.
/// </summary>
/// <param name="Window"><c>Daytime</c> or <c>Any</c>.</param>
/// <param name="MinimumMinutes">Shortest Good Time.</param>
/// <param name="MaximumStatus">Best status allowed, e.g. <c>Favorable</c>.</param>
/// <param name="Weights">Limb weights (<c>tithi</c>, <c>vara</c>, <c>nakshatra</c>, <c>yoga</c>, <c>karana</c>).</param>
/// <param name="EffectScores">Score per effect (<c>Supportive</c>, <c>Neutral</c>, <c>Adverse</c>, <c>Excluded</c>).</param>
/// <param name="Thresholds">Minimum score per status.</param>
/// <param name="Factors">Per limb: <c>*</c> (every value), a group name, or a value key → effect. Within a layer <c>*</c> applies first, then groups, then values.</param>
/// <param name="FavorablePeriods">Per period kind: bonus and weekdays it does not apply, or <c>ignore</c>.</param>
/// <param name="UnfavorablePeriods">Per period kind: severity alone and with a favorable period, or <c>ignore</c>.</param>
/// <param name="ExcludedLunarMonths">Amanta months the activity is not undertaken in.</param>
/// <param name="ExcludeAdhikaMonth">Whether an Adhika month rules the activity out.</param>
public sealed record MuhurtaRuleSet(
    string? Window = null,
    int? MinimumMinutes = null,
    string? MaximumStatus = null,
    IReadOnlyDictionary<string, double>? Weights = null,
    IReadOnlyDictionary<string, int>? EffectScores = null,
    IReadOnlyList<MuhurtaThreshold>? Thresholds = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Factors = null,
    IReadOnlyDictionary<string, FavorablePeriodData>? FavorablePeriods = null,
    IReadOnlyDictionary<string, UnfavorablePeriodData>? UnfavorablePeriods = null,
    IReadOnlyList<string>? ExcludedLunarMonths = null,
    bool? ExcludeAdhikaMonth = null);

/// <summary>Minimum score for a status.</summary>
public sealed record MuhurtaThreshold(string Status, int MinScore);

/// <summary>A favorable period: score bonus and weekdays it does not apply; <c>ignore</c> removes it.</summary>
public sealed record FavorablePeriodData(int Bonus = 0, IReadOnlyList<string>? ExcludedWeekdays = null, bool Ignore = false);

/// <summary>An unfavorable period's severity alone and when overlapping a favorable period; <c>ignore</c> removes it.</summary>
public sealed record UnfavorablePeriodData(string? Alone = null, string? WithFavorable = null, bool Ignore = false);

/// <summary>
/// Source of the Muhurta rules. The embedded file can be replaced as a whole by <c>muhurta-activities.json</c> in the
/// content directory.
/// </summary>
public interface IMuhurtaRulesRepository
{
    /// <exception cref="DataAccessException">The rules could not be read.</exception>
    Task<MuhurtaRulesDocument> GetRulesAsync(CancellationToken cancellationToken);
}

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

/// <summary>Versioned instructions for the optional AI features (<c>muhurta-ai-prompts.json</c>, ADR-0012).</summary>
/// <param name="Version">Prompt version; part of the explanation cache key and returned with AI results.</param>
/// <param name="Explanation">Instructions for the "Why this time?" narrative.</param>
/// <param name="ActivityClassification">Instructions for mapping "Other" text to an activity key.</param>
public sealed record MuhurtaAiPrompts(string Version, string Explanation, string ActivityClassification);

/// <summary>
/// Source of the AI prompts. The embedded file can be replaced by <c>muhurta-ai-prompts.json</c> in the content
/// directory.
/// </summary>
public interface IMuhurtaPromptRepository
{
    /// <exception cref="DataAccessException">The prompts could not be read.</exception>
    Task<MuhurtaAiPrompts> GetPromptsAsync(CancellationToken cancellationToken);
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
