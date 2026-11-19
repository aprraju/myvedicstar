using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Provider selection for the astronomy port (FR-06). One port, one implementation behind a configuration key.
/// </summary>
/// <remarks>
/// <para>Keys: <c>analytical</c> (default until AST-12), <c>spice</c>, and <c>compare</c> which runs both and emits the
/// delta so the parity gate can run in CI without a second calculation path.</para>
/// <para>An unknown or absent key <b>fails at registration time</b>, not at first request. A silent fallback to the
/// analytical series would be exactly the quiet degradation the owner forbade.</para>
/// </remarks>
public static class AstronomyProviderRegistration
{
    /// <summary>Configuration key holding the provider selection.</summary>
    public const string ConfigurationKey = "Astronomy:Provider";

    /// <summary>
    /// Registers <see cref="IEphemeris"/> for the configured provider. <paramref name="configureSpice"/> supplies the
    /// rich engine when <c>spice</c> or <c>compare</c> is selected; leaving it null for those keys is an error rather
    /// than a fallback.
    /// </summary>
    public static IServiceCollection AddAstronomyProvider(this IServiceCollection services, string? providerKey,
        Func<IServiceProvider, IEphemerisEngine>? configureSpice = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // api#140: install the ONE UT->TT conversion used by reference work. It is this provider's own DeltaT, so a
        // generator and the engine cannot disagree - a divergence is impossible rather than tested-for. Set here, at
        // composition, because it is the same moment the provider itself is chosen.
        MyVedicStar.Astronomy.Core.TtOffsets.Conversion = DeltaT.ToJulianEphemerisDay;

        // Parsing happens here, at composition, so a bad key cannot reach a request.
        var selection = ProviderSelectionParser.Parse(providerKey);

        switch (selection)
        {
            case ProviderSelection.Analytical:
                services.TryAddSingleton<IEphemeris, MeeusEphemeris>();
                services.TryAddSingleton<IGrahaPositionsProvider, MeeusGrahaPositionsProvider>();
                break;

            case ProviderSelection.Spice:
                RequireEngine(configureSpice, ProviderSelectionParser.SpiceKey);
                services.TryAddSingleton<IEphemerisEngine>(provider => configureSpice!(provider));
                services.TryAddSingleton<IEphemeris, AstronomyEngineEphemerisAdapter>();
                services.TryAddSingleton<IGrahaPositionsProvider, AstronomyEngineGrahaPositionsAdapter>();
                break;

            case ProviderSelection.Compare:
                RequireEngine(configureSpice, ProviderSelectionParser.CompareKey);
                services.TryAddSingleton<IEphemerisEngine>(provider => configureSpice!(provider));
                services.TryAddSingleton<IEphemeris, ComparingEphemeris>();
                // Compare mode compares the Sun and Moon ports today. The graha surface has a single implementation
                // only when the engine supplies one, so it follows the SPICE engine rather than pretending to compare
                // nine bodies whose parity fixtures do not exist yet.
                services.TryAddSingleton<IGrahaPositionsProvider, AstronomyEngineGrahaPositionsAdapter>();
                break;
        }

        // Both providers satisfy the graha port because both are selected here, from the same one key: a provider
        // cannot be chosen for the Sun and Moon and silently change for the grahas.
        services.TryAddSingleton<GrahaPositionsResolver>();

        // The selected provider is part of provenance and is asserted by a test.
        services.TryAddSingleton(new SelectedAstronomyProvider(selection));
        return services;
    }

    private static void RequireEngine(Func<IServiceProvider, IEphemerisEngine>? configureSpice, string key) =>
        _ = configureSpice ?? throw new InvalidOperationException(
            $"Provider key '{key}' was selected but no astronomy engine was supplied. Registering a fallback here "
            + "would silently change results, so this is an error.");
}

/// <summary>The provider actually selected, for provenance and tests.</summary>
public sealed record SelectedAstronomyProvider(ProviderSelection Selection)
{
    public string Key => ProviderSelectionParser.ToKey(Selection);
}
