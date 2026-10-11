using MyVedicDay.Panchang.Calculation.Models;
using MyVedicDay.Shared.Exceptions;

namespace MyVedicDay.Muhurta.Business;

/// <summary>Normalizes public birth-star names to the existing 27-star catalog. Abhijit is not a catalog star.</summary>
internal static class BirthNakshatra
{
    private static readonly IReadOnlyDictionary<string, Nakshatra> Names = CreateNames();

    public static Nakshatra? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Names.TryGetValue(Key(value), out var star)) return star;
        throw new ValidationException("nakshatra", "Unknown birth Nakshatra. Use one of the 27 standard Nakshatra names; Abhijit is not supported.");
    }

    private static string Key(string value) => value.Trim().Replace(" ", "", StringComparison.Ordinal)
        .Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);

    private static Dictionary<string, Nakshatra> CreateNames()
    {
        var names = Enum.GetValues<Nakshatra>().ToDictionary(star => star.ToString(), star => star, StringComparer.OrdinalIgnoreCase);
        names["Aswini"] = Nakshatra.Ashwini;
        names["Krithika"] = Nakshatra.Krittika;
        names["Mrigashirsha"] = Nakshatra.Mrigashira;
        names["Mrigasira"] = Nakshatra.Mrigashira;
        names["Arudra"] = Nakshatra.Ardra;
        names["Aslesha"] = Nakshatra.Ashlesha;
        names["Jyestha"] = Nakshatra.Jyeshtha;
        names["Moola"] = Nakshatra.Mula;
        names["Sravana"] = Nakshatra.Shravana;
        names["Dhanistha"] = Nakshatra.Dhanishta;
        names["Satabhisha"] = Nakshatra.Shatabhisha;
        return names;
    }
}
