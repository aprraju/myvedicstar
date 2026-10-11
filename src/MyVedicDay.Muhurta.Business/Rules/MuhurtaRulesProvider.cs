using System.Collections.Concurrent;
using MyVedicDay.Astronomy;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Muhurta.Data;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Shared.Exceptions;
using NodaTime;

namespace MyVedicDay.Muhurta.Business.Rules;

/// <summary>
/// Resolves the rule profile of an activity in a tradition: defaults → activity → tradition <c>*</c> → tradition
/// activity. Every name is validated, so a typo in the rules file fails clearly instead of silently changing a
/// recommendation. Profiles are cached per tradition and activity (the rules document is immutable).
/// </summary>
internal sealed class MuhurtaRulesProvider(IMuhurtaRulesRepository repository)
{
    public const string AllTraditions = "*";

    private static readonly IReadOnlyDictionary<Limb, string[]> Values = new Dictionary<Limb, string[]>
    {
        [Limb.Tithi] = Enum.GetNames<Tithi>(),
        [Limb.Nakshatra] = Enum.GetNames<Nakshatra>(),
        [Limb.Yoga] = Enum.GetNames<Yoga>(),
        [Limb.Karana] = Enum.GetNames<Karana>(),
        [Limb.Vara] = Enum.GetValues<IsoDayOfWeek>().Where(day => day != IsoDayOfWeek.None).Select(day => day.ToString()).ToArray(),
    };

    private readonly ConcurrentDictionary<string, ActivityRuleProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public Task<MuhurtaRulesDocument> GetDocumentAsync(CancellationToken cancellationToken) => repository.GetRulesAsync(cancellationToken);

    /// <summary>The activity definition for <paramref name="key"/> (case-insensitive), or null.</summary>
    public static MuhurtaActivityDefinition? FindActivity(MuhurtaRulesDocument document, string key) =>
        document.Activities.FirstOrDefault(activity => activity.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public async Task<ActivityRuleProfile> GetProfileAsync(string activity, string tradition, CancellationToken cancellationToken)
    {
        if (_profiles.TryGetValue($"{tradition}|{activity}", out var cached))
        {
            return cached;
        }

        var document = await repository.GetRulesAsync(cancellationToken);
        var definition = FindActivity(document, activity) ?? throw new CalculationException($"No Muhurta rules for activity '{activity}'.");
        if (definition.Rules is null)
        {
            throw new CalculationException($"Activity '{activity}' has no rules of its own.");
        }

        var traditionRules = document.Traditions?
            .FirstOrDefault(entry => entry.Key.Equals(tradition, StringComparison.OrdinalIgnoreCase)).Value;
        var layers = new[]
        {
            document.Defaults,
            definition.Rules,
            traditionRules?.GetValueOrDefault(AllTraditions),
            traditionRules?.GetValueOrDefault(definition.Key),
        };

        return _profiles.GetOrAdd($"{tradition}|{activity}", Resolve(definition.Key, document, layers.OfType<MuhurtaRuleSet>().ToArray()));
    }

    private static ActivityRuleProfile Resolve(string activity, MuhurtaRulesDocument document, IReadOnlyList<MuhurtaRuleSet> layers)
    {
        string? window = null;
        int? minimumMinutes = null;
        string? maximumStatus = null;
        IReadOnlyList<MuhurtaThreshold>? thresholds = null;
        IReadOnlyList<string>? excludedMonths = null;
        bool? excludeAdhika = null;
        var weights = new Dictionary<Limb, double>();
        var effectScores = new Dictionary<FactorEffect, int>();
        var factors = Enum.GetValues<Limb>().ToDictionary(limb => limb, _ => new Dictionary<string, FactorEffect>(StringComparer.Ordinal));
        var favorable = new Dictionary<PanchangPeriodKind, FavorablePeriodRule>();
        var unfavorable = new Dictionary<PanchangPeriodKind, UnfavorablePeriodRule>();

        foreach (var layer in layers)
        {
            window = layer.Window ?? window;
            minimumMinutes = layer.MinimumMinutes ?? minimumMinutes;
            maximumStatus = layer.MaximumStatus ?? maximumStatus;
            thresholds = layer.Thresholds ?? thresholds;
            excludedMonths = layer.ExcludedLunarMonths ?? excludedMonths;
            excludeAdhika = layer.ExcludeAdhikaMonth ?? excludeAdhika;

            foreach (var (limb, weight) in layer.Weights ?? new Dictionary<string, double>())
            {
                weights[Parse<Limb>(activity, "weights", limb)] = weight;
            }

            foreach (var (effect, score) in layer.EffectScores ?? new Dictionary<string, int>())
            {
                effectScores[Parse<FactorEffect>(activity, "effectScores", effect)] = score;
            }

            foreach (var (limbName, entries) in layer.Factors ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
            {
                ApplyFactors(activity, document, Parse<Limb>(activity, "factors", limbName), entries, factors);
            }

            foreach (var (kindName, rule) in layer.FavorablePeriods ?? new Dictionary<string, FavorablePeriodData>())
            {
                var kind = Parse<PanchangPeriodKind>(activity, "favorablePeriods", kindName);
                if (rule.Ignore)
                {
                    favorable.Remove(kind);
                    continue;
                }

                var weekdays = rule.ExcludedWeekdays?.Select(day => Parse<IsoDayOfWeek>(activity, "excludedWeekdays", day)).ToHashSet()
                    ?? (favorable.TryGetValue(kind, out var previous) ? previous.ExcludedWeekdays.ToHashSet() : []);
                favorable[kind] = new FavorablePeriodRule(rule.Bonus, weekdays);
            }

            foreach (var (kindName, rule) in layer.UnfavorablePeriods ?? new Dictionary<string, UnfavorablePeriodData>())
            {
                var kind = Parse<PanchangPeriodKind>(activity, "unfavorablePeriods", kindName);
                if (rule.Ignore)
                {
                    unfavorable.Remove(kind);
                    continue;
                }

                unfavorable.TryGetValue(kind, out var previous);
                unfavorable[kind] = new UnfavorablePeriodRule(
                    rule.Alone is null ? previous?.Alone ?? MuhurtaStatus.Avoid : Parse<MuhurtaStatus>(activity, "alone", rule.Alone),
                    rule.WithFavorable is null ? previous?.WithFavorable ?? MuhurtaStatus.Mixed : Parse<MuhurtaStatus>(activity, "withFavorable", rule.WithFavorable));
            }
        }

        if (Enum.GetValues<FactorEffect>().Any(effect => !effectScores.ContainsKey(effect)) || thresholds is null || thresholds.Count == 0)
        {
            throw new CalculationException($"The Muhurta rules for '{activity}' need a score for every effect and at least one threshold.");
        }

        return new ActivityRuleProfile(
            activity,
            window is null ? EvaluationWindow.Daytime : Parse<EvaluationWindow>(activity, "window", window),
            minimumMinutes ?? 24,
            maximumStatus is null ? null : Parse<MuhurtaStatus>(activity, "maximumStatus", maximumStatus),
            weights,
            factors.ToDictionary(entry => entry.Key, entry => (IReadOnlyDictionary<string, FactorEffect>)entry.Value),
            effectScores,
            thresholds.Select(threshold => new StatusThreshold(Parse<MuhurtaStatus>(activity, "thresholds", threshold.Status), threshold.MinScore)).ToArray(),
            favorable,
            unfavorable,
            (excludedMonths ?? []).Select(month => Parse<LunarMonth>(activity, "excludedLunarMonths", month)).ToHashSet(),
            excludeAdhika ?? false);
    }

    /// <summary>Applies one layer of one limb: <c>*</c> first, then group names, then value keys.</summary>
    private static void ApplyFactors(
        string activity,
        MuhurtaRulesDocument document,
        Limb limb,
        IReadOnlyDictionary<string, string> entries,
        Dictionary<Limb, Dictionary<string, FactorEffect>> factors)
    {
        var values = Values[limb];
        var groups = document.Groups
            .FirstOrDefault(entry => entry.Key.Equals(limb.ToString(), StringComparison.OrdinalIgnoreCase)).Value
            ?? new Dictionary<string, IReadOnlyList<string>>();

        var ordered = entries.OrderBy(entry => entry.Key == "*" ? 0 : groups.ContainsKey(entry.Key) ? 1 : 2);
        foreach (var (key, effectName) in ordered)
        {
            var effect = Parse<FactorEffect>(activity, $"factors.{limb}", effectName);
            IEnumerable<string> targets = key == "*"
                ? values
                : groups.TryGetValue(key, out var members)
                    ? members
                    : [key];

            foreach (var value in targets)
            {
                if (!values.Contains(value, StringComparer.Ordinal))
                {
                    throw new CalculationException($"Muhurta rules for '{activity}': '{value}' is not a {limb} value or group.");
                }

                factors[limb][value] = effect;
            }
        }
    }

    private static TEnum Parse<TEnum>(string activity, string field, string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _)
            ? parsed
            : throw new CalculationException($"Muhurta rules for '{activity}' have an unknown {field} '{value}'.");
}
