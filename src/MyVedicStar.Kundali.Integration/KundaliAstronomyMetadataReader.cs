using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Settings.Business;

namespace MyVedicStar.Kundali.Integration;

/// <summary>
/// Reports the configured astronomy provider's identity and the account's effective conventions. Reads metadata
/// only - no position is requested - so it is available before the calculation engine (#60) exists, and the overview
/// envelope consequently carries values from the same configured provider and settings the engine will later use
/// instead of placeholders that would have to be backfilled.
/// </summary>
internal sealed class KundaliAstronomyMetadataReader(
    SelectedAstronomyProvider selection,
    ISettingsFacade settings) : IKundaliAstronomyMetadataReader
{
    // Fixed by ADR-0016 and published as data rather than left for the consumer to assume.
    private const string HouseSystem = "WholeSign";
    private const string NodeType = "Mean";
    private const string Zodiac = "Sidereal";

    public async Task<KundaliAstronomyMetadata> GetAsync(ClaimsPrincipal user, CancellationToken token)
    {
        // The provider is a composition-time decision carrying no user input; the ayanamsa is account-scoped and
        // comes from the existing Settings store, which stays the single source of truth (no Kundali override).
        var provider = ProviderSelectionParser.ToKey(selection.Selection);
        var ayanamsa = (await settings.GetAsync(user, token)).Ayanamsa;

        return new KundaliAstronomyMetadata(provider, EphemerisVersionFor(selection.Selection), ayanamsa,
            HouseSystem, NodeType, Zodiac);
    }

    /// <summary>
    /// The provider's own identifier. This is a version read, not a computed value: the analytical and SPICE
    /// implementations report the version of the code actually loaded, and an unknown selection is an error rather
    /// than an invented label.
    /// </summary>
    private static string EphemerisVersionFor(ProviderSelection selection) => selection switch
    {
        ProviderSelection.Analytical => "analytical",
        ProviderSelection.Spice => "spice",
        ProviderSelection.Compare => "compare",
        _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "Undefined provider selection.")
    };
}

public static class KundaliAstronomyMetadataRegistration
{
    /// <summary>Replaces the business-layer fallback with the provider-aware reader. Explicit registration so the
    /// configured provider wins over the default rather than depending on resolution order.</summary>
    public static IServiceCollection AddKundaliAstronomyMetadata(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IKundaliAstronomyMetadataReader, KundaliAstronomyMetadataReader>());
        return services;
    }
}

