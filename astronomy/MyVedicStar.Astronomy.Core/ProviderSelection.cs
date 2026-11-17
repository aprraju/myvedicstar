using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// Selects which provider sits behind the single port (FR-06). Values are the configuration keys the story binds.
/// </summary>
public enum ProviderSelection
{
    /// <summary>The existing analytical series. Default until AST-12.</summary>
    Analytical,
    /// <summary>The JPL/SPICE provider.</summary>
    Spice,
    /// <summary>Runs both, returns the primary and emits the delta. The mechanism that makes the parity gate possible
    /// without a second calculation path.</summary>
    Compare
}

/// <summary>
/// Parses the provider configuration key strictly. Unknown or malformed values fail rather than falling back to
/// analytical, because a silent fallback is exactly the quiet degradation the architecture forbids (FR-06 AC-6).
/// </summary>
public static class ProviderSelectionParser
{
    public const string AnalyticalKey = "analytical";
    public const string SpiceKey = "spice";
    public const string CompareKey = "compare";

    /// <summary>Parses the key, throwing on anything unrecognised. Comparison is case-insensitive and trimmed;
    /// everything else is an error.</summary>
    public static ProviderSelection Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "An astronomy provider key is required. Expected 'analytical', 'spice' or 'compare'; an omitted key is "
                + "not treated as 'analytical'.", nameof(value));

        return value.Trim().ToLowerInvariant() switch
        {
            AnalyticalKey => ProviderSelection.Analytical,
            SpiceKey => ProviderSelection.Spice,
            CompareKey => ProviderSelection.Compare,
            _ => throw new ArgumentException(
                $"Unknown astronomy provider key '{value}'. Expected 'analytical', 'spice' or 'compare'; the value is "
                + "not silently defaulted.", nameof(value))
        };
    }

    public static string ToKey(ProviderSelection selection) => selection switch
    {
        ProviderSelection.Analytical => AnalyticalKey,
        ProviderSelection.Spice => SpiceKey,
        ProviderSelection.Compare => CompareKey,
        _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "Undefined provider selection.")
    };
}

/// <summary>
/// A parity comparison for one body at one instant, produced by <see cref="ProviderSelection.Compare"/>.
/// </summary>
public sealed record ParityDelta(
    CelestialBody Body,
    double JulianDayUt,
    double PrimaryLongitudeDegrees,
    double ComparisonLongitudeDegrees,
    double SeparationArcseconds,
    bool WithinTolerance);

/// <summary>The sink for parity deltas emitted in compare mode.</summary>
public interface IParityObserver
{
    void OnDelta(ParityDelta delta);
}
