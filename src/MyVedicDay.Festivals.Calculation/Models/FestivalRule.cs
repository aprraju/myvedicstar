using MyVedicDay.Astronomy;
using MyVedicDay.Panchang.Calculation.Models;

namespace MyVedicDay.Festivals.Calculation.Models;

/// <summary>
/// Part of the day in which the defining Tithi or Nakshatra must prevail (Kala). The day is divided by sunrise and
/// sunset; the night runs from sunset to the next sunrise.
/// </summary>
public enum ObservanceTime
{
    /// <summary>At sunrise (Udaya). If the Tithi prevails at two sunrises, the first day.</summary>
    Sunrise = 1,

    /// <summary>The 96 minutes (4 ghatis) before sunrise.</summary>
    Arunodaya,

    /// <summary>The first half of the daytime.</summary>
    Purvahna,

    /// <summary>The middle fifth of the daytime.</summary>
    Madhyahna,

    /// <summary>The fourth fifth of the daytime.</summary>
    Aparahna,

    /// <summary>At sunset.</summary>
    Sunset,

    /// <summary>The first fifth of the night after sunset (about 2 h 24 min in equinox nights).</summary>
    Pradosha,

    /// <summary>The 8th of the night's 15 muhurtas, around local midnight.</summary>
    Nishita,
}

/// <summary>What a festival rule is anchored to.</summary>
public enum FestivalBasis
{
    /// <summary>A Tithi, optionally in a given lunar or solar month.</summary>
    Tithi = 1,

    /// <summary>A Nakshatra, optionally in a given lunar or solar month.</summary>
    Nakshatra,

    /// <summary>The Sun entering a sidereal sign.</summary>
    Sankranti,
}

/// <summary>
/// One way a festival is determined. Tithi and Nakshatra rules pick, for each occurrence of the element, the day on
/// which it prevails longest during <see cref="Time"/>; when it touches no such window, the day it covers most.
/// Sankranti rules pick the first day whose <see cref="Time"/> is at or after the Sankranti.
/// </summary>
/// <param name="Basis">What the rule is anchored to.</param>
/// <param name="Time">Observance time used to choose the day.</param>
/// <param name="Tithi">Required for <see cref="FestivalBasis.Tithi"/>.</param>
/// <param name="Nakshatra">Required for <see cref="FestivalBasis.Nakshatra"/>.</param>
/// <param name="LunarMonth">Restricts to this Amanta lunar month (never an Adhika month); null for every month.</param>
/// <param name="SolarMonth">Restricts to days the Sun is in this sign at sunset (Tamil and Kerala solar months).</param>
/// <param name="Sign">For <see cref="FestivalBasis.Sankranti"/>: the sign entered; null for every Sankranti.</param>
/// <param name="OffsetDays">Days after (positive) or before (negative) the anchor day, e.g. Holi is +1 from Holika Dahan.</param>
public sealed record FestivalRule(
    FestivalBasis Basis,
    ObservanceTime Time,
    Tithi? Tithi = null,
    Nakshatra? Nakshatra = null,
    LunarMonth? LunarMonth = null,
    Rashi? SolarMonth = null,
    Rashi? Sign = null,
    int OffsetDays = 0);

/// <summary>A festival and the rules that determine it; any matching rule produces an occurrence.</summary>
/// <param name="Key">Stable festival key, e.g. <c>GaneshChaturthi</c>.</param>
/// <param name="Rules">One or more rules, e.g. Shukla and Krishna Ekadashi.</param>
public sealed record FestivalDefinition(string Key, IReadOnlyList<FestivalRule> Rules);
