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

        // Parsing happens here, at composition, so a bad key cannot reach a request.
        var selection = ProviderSelectionParser.Parse(providerKey);

        switch (selection)
        {
            case ProviderSelection.Analytical:
                services.TryAddSingleton<IEphemeris, MeeusEphemeris>();
                break;

            case ProviderSelection.Spice:
                RequireEngine(configureSpice, ProviderSelectionParser.SpiceKey);
                services.TryAddSingleton<IEphemerisEngine>(provider => configureSpice!(provider));
                services.TryAddSingleton<IEphemeris, AstronomyEngineEphemerisAdapter>();
                break;

            case ProviderSelection.Compare:
                RequireEngine(configureSpice, ProviderSelectionParser.CompareKey);
                services.TryAddSingleton<IEphemerisEngine>(provider => configureSpice!(provider));
                services.TryAddSingleton<IEphemeris, ComparingEphemeris>();
                break;
        }

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
