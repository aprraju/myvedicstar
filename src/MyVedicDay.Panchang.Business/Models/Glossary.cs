using MyVedicDay.Localization;

namespace MyVedicDay.Panchang.Business.Models;

/// <summary>
/// The Panchang glossary in one language, grouped by section: <c>periods</c> (keyed by period kind), <c>elements</c>
/// (<c>tithi</c>, <c>nakshatra</c>, <c>yoga</c>, <c>karana</c>), <c>terms</c> (<c>sunrise</c>, <c>sunset</c>, <c>vara</c>,
/// <c>choghadiya</c>) and <c>classifications</c> (keyed by <see cref="PeriodClassification"/>).
/// </summary>
/// <param name="Language">Language of the text.</param>
/// <param name="Sections">Entries by section, then by key.</param>
public sealed record GlossaryResult(string Language, IReadOnlyDictionary<string, IReadOnlyDictionary<string, LocalizedEntry>> Sections);
