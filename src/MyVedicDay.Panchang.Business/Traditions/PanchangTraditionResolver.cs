using MyVedicDay.Panchang.Calculation;
using MyVedicDay.Panchang.Data.Content;
using MyVedicDay.Shared.Exceptions;
using MyVedicDay.Traditions;

namespace MyVedicDay.Panchang.Business.Traditions;

/// <summary>
/// Maps the user's tradition (validated by the shared <see cref="ITraditionRegistry"/>) to Panchang's profile for it:
/// which timings to show and which calculation method to use. A tradition without a Panchang profile uses the
/// <c>all</c> profile.
/// </summary>
internal sealed class PanchangTraditionResolver(
    ITraditionRegistry registry,
    ITraditionProfileRepository profiles,
    IPanchangTraditionCatalog calculationMethods)
{
    public const string FallbackProfile = "all";

    public async Task<TraditionProfile> ResolveAsync(string? requestedTradition, CancellationToken cancellationToken)
    {
        var key = await registry.ResolveAsync(requestedTradition, cancellationToken);
        var available = await profiles.GetProfilesAsync(cancellationToken);

        var profile = Find(available, key);
        if (profile is null)
        {
            var fallback = Find(available, FallbackProfile)
                ?? throw new CalculationException($"No Panchang profile is configured for tradition '{key}'.");
            profile = fallback with { Key = key };
        }

        return calculationMethods.IsSupported(profile.CalculationMethod)
            ? profile
            : throw new CalculationException($"Tradition '{profile.Key}' is configured with an unsupported calculation method.");
    }

    public async Task<IReadOnlyList<TraditionProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var keys = await registry.GetKeysAsync(cancellationToken);
        var result = new List<TraditionProfile>(keys.Count);
        foreach (var key in keys)
        {
            result.Add(await ResolveAsync(key, cancellationToken));
        }

        return result;
    }

    private static TraditionProfile? Find(IReadOnlyList<TraditionProfile> profiles, string key) =>
        profiles.FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
