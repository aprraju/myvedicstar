namespace MyVedicDay.Festivals.Data;

/// <summary>
/// The versioned festival rules (<c>festival-rules.json</c>): which festivals exist, how each date is determined, and
/// which traditions observe them. Interpretation only, no astronomy.
/// </summary>
/// <param name="Version">Rules version, returned with every result and part of the cache key; bump it on every change.</param>
/// <param name="Festivals">Festivals in display order (within a date, earlier entries are listed first).</param>
public sealed record FestivalRulesDocument(string Version, IReadOnlyList<FestivalRuleEntry> Festivals);

/// <summary>A festival and the rules that determine its date.</summary>
/// <param name="Key">Stable key, also the content key <c>festivals.{key}</c>.</param>
/// <param name="Category"><c>festival</c> or <c>observance</c> (recurring vrata such as Ekadashi or Pradosham).</param>
/// <param name="Traditions">Traditions that observe it, or <c>*</c> for every tradition. The <c>all</c> tradition shows every festival.</param>
/// <param name="Rules">One or more rules; any match produces an occurrence.</param>
/// <param name="Supersedes">Festival keys hidden on the same date when this festival is shown (e.g. a named Ekadashi hides the generic one).</param>
public sealed record FestivalRuleEntry(
    string Key,
    string Category,
    IReadOnlyList<string> Traditions,
    IReadOnlyList<FestivalRuleData> Rules,
    IReadOnlyList<string>? Supersedes);

/// <summary>One way to determine a festival's date. Values are the enum names of the festival calculation engine.</summary>
/// <param name="Basis"><c>Tithi</c>, <c>Nakshatra</c> or <c>Sankranti</c>.</param>
/// <param name="Time">Observance time: <c>Sunrise</c>, <c>Arunodaya</c>, <c>Purvahna</c>, <c>Madhyahna</c>, <c>Aparahna</c>, <c>Sunset</c>, <c>Pradosha</c> or <c>Nishita</c>.</param>
/// <param name="Tithi">Tithi key, e.g. <c>ShuklaChaturthi</c>.</param>
/// <param name="Nakshatra">Nakshatra key, e.g. <c>Shravana</c>.</param>
/// <param name="LunarMonth">Amanta lunar month, e.g. <c>Bhadrapada</c>.</param>
/// <param name="SolarMonth">Solar month as a sign, e.g. <c>Simha</c> (Chingam / Avani).</param>
/// <param name="Sign">Sign entered at the Sankranti, e.g. <c>Makara</c>; omit for every Sankranti.</param>
/// <param name="OffsetDays">Days after (positive) or before (negative) the day the rule finds.</param>
/// <param name="AvoidBhadra">Count the observance window only where Bhadra (Vishti Karana) does not prevail.</param>
/// <param name="SunriseDayMinimumFraction">
/// When no day's window is fully covered: choose the day at whose sunrise the element prevails if it lasts at least
/// this fraction (0–1] of that day's daytime, e.g. <c>0.2</c> (three Muhurtas) or <c>0.75</c> (three Praharas).
/// </param>
/// <param name="Occurrence">With <c>solarMonth</c>: <c>All</c> (default) or <c>Last</c> (only the month's last occurrence).</param>
/// <param name="Ekadashi">For an Ekadashi Tithi: the fasting convention, <c>Smarta</c> or <c>Vaishnava</c>.</param>
public sealed record FestivalRuleData(
    string Basis,
    string Time,
    string? Tithi = null,
    string? Nakshatra = null,
    string? LunarMonth = null,
    string? SolarMonth = null,
    string? Sign = null,
    int OffsetDays = 0,
    bool AvoidBhadra = false,
    double? SunriseDayMinimumFraction = null,
    string? Occurrence = null,
    string? Ekadashi = null);

/// <summary>
/// Source of the festival rules. The embedded file can be replaced as a whole by <c>festival-rules.json</c> in the
/// content directory.
/// </summary>
public interface IFestivalRulesRepository
{
    /// <exception cref="MyVedicDay.Shared.Exceptions.DataAccessException">The rules could not be read.</exception>
    Task<FestivalRulesDocument> GetRulesAsync(CancellationToken cancellationToken);
}
