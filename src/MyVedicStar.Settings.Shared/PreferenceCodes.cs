namespace MyVedicStar.Settings.Data;

/// <summary>Canonical persisted and wire preference codes; labels are presentation-only.</summary>
public static class PreferenceCodes
{
    public const string DefaultChartStyle = "south-indian";
    public const string DefaultAyanamsa = "lahiri";
    public const string InvalidChartStyle = "Choose a supported chart style.";
    public const string InvalidAyanamsa = "Choose a supported ayanamsa.";
    public static IReadOnlyList<string> ChartStyles { get; } = Array.AsReadOnly(new[] { "north-indian", DefaultChartStyle, "east-indian" });
    public static IReadOnlyList<string> Ayanamsas { get; } = Array.AsReadOnly(new[] { DefaultAyanamsa, "raman", "krishnamurti", "true-chitra" });
}
