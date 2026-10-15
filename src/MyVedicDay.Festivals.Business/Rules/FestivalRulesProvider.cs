using MyVedicDay.Astronomy;
using MyVedicDay.Festivals.Calculation.Models;
using MyVedicDay.Festivals.Data;
using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Festivals.Business.Rules;

/// <summary>Presentation data of a festival: category, observing traditions, superseded festivals and display order.</summary>
internal sealed record FestivalMetadata(string Category, IReadOnlySet<string> Traditions, IReadOnlySet<string> Supersedes, int Order)
{
    public const string AllTraditions = "*";

    /// <summary>Whether <paramref name="tradition"/> observes the festival. The <c>all</c> tradition shows every festival.</summary>
    public bool IsObservedBy(string tradition) =>
        tradition.Equals(FestivalRules.AllTradition, StringComparison.OrdinalIgnoreCase)
        || Traditions.Contains(AllTraditions)
        || Traditions.Contains(tradition);
}

/// <summary>The festival rules parsed for the calculation engine, with their presentation data.</summary>
internal sealed record FestivalRules(
    string Version, IReadOnlyList<FestivalDefinition> Definitions, IReadOnlyDictionary<string, FestivalMetadata> Metadata)
{
    public const string AllTradition = "all";
}

/// <summary>
/// Parses the festival rules once (the rules document is immutable) and validates every name, so a typo in the rules
/// file fails clearly instead of silently dropping a festival.
/// </summary>
internal sealed class FestivalRulesProvider(IFestivalRulesRepository repository)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FestivalRules? _rules;

    public async Task<FestivalRules> GetRulesAsync(CancellationToken cancellationToken)
    {
        if (_rules is { } cached)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _rules ??= Parse(await repository.GetRulesAsync(cancellationToken));
        }
        finally
        {
            _lock.Release();
        }
    }

    private static FestivalRules Parse(FestivalRulesDocument document)
    {
        var duplicate = document.Festivals.GroupBy(festival => festival.Key, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new CalculationException($"The festival rules define '{duplicate.Key}' more than once.");
        }

        var definitions = document.Festivals
            .Select(festival => new FestivalDefinition(festival.Key, festival.Rules.Select(rule => ParseRule(festival.Key, rule)).ToArray()))
            .ToArray();

        var metadata = document.Festivals
            .Select((festival, index) => (festival.Key, Value: new FestivalMetadata(
                festival.Category,
                festival.Traditions.ToHashSet(StringComparer.OrdinalIgnoreCase),
                (festival.Supersedes ?? []).ToHashSet(StringComparer.Ordinal),
                index)))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        return new FestivalRules(document.Version, definitions, metadata);
    }

    private static FestivalRule ParseRule(string festival, FestivalRuleData rule)
    {
        var basis = Parse<FestivalBasis>(festival, "basis", rule.Basis);
        var parsed = new FestivalRule(
            basis,
            Parse<ObservanceTime>(festival, "time", rule.Time),
            ParseOptional<Tithi>(festival, "tithi", rule.Tithi),
            ParseOptional<Nakshatra>(festival, "nakshatra", rule.Nakshatra),
            ParseOptional<LunarMonth>(festival, "lunarMonth", rule.LunarMonth),
            ParseOptional<Rashi>(festival, "solarMonth", rule.SolarMonth),
            ParseOptional<Rashi>(festival, "sign", rule.Sign),
            rule.OffsetDays,
            rule.AvoidBhadra,
            rule.SunriseDayMinimumFraction,
            ParseOptional<SolarMonthOccurrence>(festival, "occurrence", rule.Occurrence) ?? SolarMonthOccurrence.All,
            ParseOptional<EkadashiConvention>(festival, "ekadashi", rule.Ekadashi));

        var complete = basis switch
        {
            FestivalBasis.Tithi => parsed.Tithi is not null,
            FestivalBasis.Nakshatra => parsed.Nakshatra is not null,
            _ => true,
        };

        if (!complete)
        {
            throw new CalculationException($"Festival '{festival}' has a {basis} rule without a {basis.ToString().ToLowerInvariant()}.");
        }

        if (parsed.SunriseDayMinimumFraction is { } fraction && fraction is not (> 0 and <= 1))
        {
            throw new CalculationException($"Festival '{festival}' has a sunriseDayMinimumFraction outside (0, 1].");
        }

        if (parsed.Occurrence != SolarMonthOccurrence.All && parsed.SolarMonth is null)
        {
            throw new CalculationException($"Festival '{festival}' has an occurrence without a solarMonth.");
        }

        if (parsed.Ekadashi is not null && parsed.Tithi is not (Tithi.ShuklaEkadashi or Tithi.KrishnaEkadashi))
        {
            throw new CalculationException($"Festival '{festival}' has an ekadashi convention on a rule that is not an Ekadashi Tithi.");
        }

        return parsed;
    }

    private static TEnum? ParseOptional<TEnum>(string festival, string field, string? value)
        where TEnum : struct, Enum =>
        value is null ? null : Parse<TEnum>(festival, field, value);

    private static TEnum Parse<TEnum>(string festival, string field, string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new CalculationException($"Festival '{festival}' has an unknown {field} '{value}'.");
}
