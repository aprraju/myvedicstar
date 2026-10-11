using System.Globalization;
using System.Text;

namespace MyVedicDay.Location.Integration.GeoNames;

/// <summary>
/// Normalizes place names for matching: case- and accent-insensitive, punctuation treated as spaces
/// ("São Paulo" = "sao paulo", "St. Louis" = "st louis").
/// </summary>
internal static class PlaceNameNormalizer
{
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(character));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }
}
