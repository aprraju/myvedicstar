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

    /// <summary>
    /// At moonrise between this sunrise and the next (Sankashti Chaturthi, Karwa Chauth). A day on which the Moon does
    /// not rise has no window.
    /// </summary>
    Moonrise,
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

/// <summary>How the day of an Ekadashi fast is chosen.</summary>
public enum EkadashiConvention
{
    /// <summary>
    /// The Ekadashi prevailing at sunrise (the earlier day when it prevails at two). When Dwadashi ends before the
    /// following sunrise the fast could not be broken (Parana) within Dwadashi, so it moves to the previous day.
    /// </summary>
    Smarta = 1,

    /// <summary>
    /// The Ekadashi must be present at Arunodaya (96 minutes before sunrise), untouched by Dashami: the last such day;
    /// when there is none, the following (Dwadashi) day.
    /// </summary>
    Vaishnava,
}

/// <summary>Which occurrences of a solar-month rule count when the element occurs more than once in the month.</summary>
public enum SolarMonthOccurrence
{
    /// <summary>Every occurrence.</summary>
    All = 1,

    /// <summary>Only the last one, e.g. the Thiruvonam of Onam or the Krittika of Karthigai Deepam.</summary>
    Last,
}

/// <summary>
/// One way a festival is determined. Tithi and Nakshatra rules pick, for each occurrence of the element, a day in
/// this order (the first two steps only for rules with <see cref="AvoidBhadra"/> or
/// <see cref="SunriseDayMinimumFraction"/>):
/// <list type="number">
/// <item><description>the first day whose whole <see cref="Time"/> window the element covers, free of Bhadra when
/// <see cref="AvoidBhadra"/>;</description></item>
/// <item><description>with <see cref="SunriseDayMinimumFraction"/>, the day at whose sunrise the element prevails,
/// when it lasts at least that fraction of the daytime after sunrise;</description></item>
/// <item><description>the day on which it covers most of the window (Bhadra-free first, the earlier day on a tie),
/// and when it touches no window, the day it covers most.</description></item>
/// </list>
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
/// <param name="AvoidBhadra">The window counts only where Bhadra (Vishti Karana) does not prevail (Holika Dahan, Raksha Bandhan).</param>
/// <param name="SunriseDayMinimumFraction">
/// Fraction of the daytime (0–1] the element must last after the sunrise it prevails at for that day to be chosen
/// when no window is fully covered, e.g. 0.2 (three of fifteen Muhurtas) for Raksha Bandhan and Vijayadashami, 0.75
/// (three Praharas) for Holika Dahan. Null to skip this step.
/// </param>
/// <param name="Occurrence">For <see cref="SolarMonth"/> rules: whether every occurrence in the month counts or only the last.</param>
/// <param name="Ekadashi">
/// For Ekadashi rules: the fasting convention, which replaces the day choice above (<see cref="Time"/> then only sets the
/// reported window). Null for the plain day choice.
/// </param>
public sealed record FestivalRule(
    FestivalBasis Basis,
    ObservanceTime Time,
    Tithi? Tithi = null,
    Nakshatra? Nakshatra = null,
    LunarMonth? LunarMonth = null,
    Rashi? SolarMonth = null,
    Rashi? Sign = null,
    int OffsetDays = 0,
    bool AvoidBhadra = false,
    double? SunriseDayMinimumFraction = null,
    SolarMonthOccurrence Occurrence = SolarMonthOccurrence.All,
    EkadashiConvention? Ekadashi = null);

/// <summary>A festival and the rules that determine it; any matching rule produces an occurrence.</summary>
/// <param name="Key">Stable festival key, e.g. <c>GaneshChaturthi</c>.</param>
/// <param name="Rules">One or more rules, e.g. Shukla and Krishna Ekadashi.</param>
public sealed record FestivalDefinition(string Key, IReadOnlyList<FestivalRule> Rules);
