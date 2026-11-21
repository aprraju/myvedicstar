using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// The provider key a product host composes when nothing is configured (FR-06, AST-12 #77).
/// </summary>
/// <remarks>
/// <para>Before AST-12 the absent-key answer was the analytical key, written out literally in each host. The cutover
/// changed that answer, and the hosts had to change with it - which is the defect this type exists to remove. The
/// default is now declared once and the hosts ask for it, so a third answer cannot appear.</para>
/// <para><b>Absence is not silence about the value.</b> An absent key is a normal state (the Day and Star hosts load
/// their own layered configuration), so the default is returned by name; but the returned key is still parsed by
/// <see cref="ProviderSelectionParser"/>, so a default that stopped being a recognised key would fail at composition
/// rather than quietly select something else. An <i>unknown</i> configured value is an error, as before.</para>
/// </remarks>
public static class ProductProviderSelection
{
    /// <summary>The key used when <c>Astronomy:Provider</c> is absent from host configuration.</summary>
    /// <remarks>
    /// <c>spice</c> since AST-12 (#77): the JPL/SPICE provider is the production default and the superseded
    /// ADR-0016 analytical series is retired from the default path. Setting <c>Astronomy:Provider=analytical</c> or
    /// <c>compare</c> keeps the old provider reachable for comparison and rollback (story AC-2); that is
    /// configuration, not a code change.
    /// </remarks>
    public const string DefaultKey = ProviderSelectionParser.SpiceKey;

    /// <summary>
    /// Resolves the provider key from configuration: the configured value when present, otherwise
    /// <see cref="DefaultKey"/>. The result is parsed, not trusted, so this method cannot return a key that
    /// <see cref="AstronomyProviderRegistration.AddAstronomyProvider"/> would reject for being unknown.
    /// </summary>
    /// <remarks>
    /// <c>Spice</c> is the only default that composes: selecting the SPICE provider requires an astronomy engine, and a
    /// registration that selects it without one is an error by design. A host therefore supplies the engine on the
    /// default path as well as on an explicit <c>spice</c>.
    /// </remarks>
    public static ProviderSelection Resolve(string? configuredValue)
    {
        var key = string.IsNullOrWhiteSpace(configuredValue) ? DefaultKey : configuredValue;
        return ProviderSelectionParser.Parse(key);
    }
}
