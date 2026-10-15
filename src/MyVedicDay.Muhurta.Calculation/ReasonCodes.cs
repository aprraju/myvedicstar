using System.Text;
using MyVedicDay.Muhurta.Calculation.Models;
using MyVedicDay.Panchang.Calculation.Models;

namespace MyVedicDay.Muhurta.Calculation;

/// <summary>
/// Stable UPPER_SNAKE reason and conflict codes. Clients branch on these, never on labels.
/// </summary>
public static class ReasonCodes
{
    public const string AdhikaMasa = "ADHIKA_MASA";
    public const string ExcludedLunarMonth = "EXCLUDED_LUNAR_MONTH";

    /// <summary>A planet the activity needs is combust (too close to the Sun), e.g. Guru or Shukra Asta; the subject is the planet.</summary>
    public const string CombustPlanet = "COMBUST_PLANET";

    /// <summary><c>FAVORABLE_NAKSHATRA</c>, <c>ADVERSE_TITHI</c>, <c>EXCLUDED_KARANA</c>…</summary>
    public static string ForLimb(FactorEffect effect, Limb limb) => effect switch
    {
        FactorEffect.Supportive => "FAVORABLE_" + limb.ToString().ToUpperInvariant(),
        FactorEffect.Adverse => "ADVERSE_" + limb.ToString().ToUpperInvariant(),
        FactorEffect.Excluded => "EXCLUDED_" + limb.ToString().ToUpperInvariant(),
        _ => "NEUTRAL_" + limb.ToString().ToUpperInvariant(),
    };

    /// <summary><c>RahuKalam</c> → <c>RAHU_KALAM</c>; <c>ChoghadiyaLabh</c> → <c>CHOGHADIYA_LABH</c>.</summary>
    public static string ForPeriod(PanchangPeriodKind kind)
    {
        var name = kind.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(name[i]));
        }

        return builder.ToString();
    }

    /// <summary><c>RahuKalam</c> → <c>NO_RAHU_KALAM_CONFLICT</c>.</summary>
    public static string NoConflict(PanchangPeriodKind kind) => "NO_" + ForPeriod(kind) + "_CONFLICT";
}
