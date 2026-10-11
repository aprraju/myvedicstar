using MyVedicDay.Localization;
using MyVedicDay.Location;

namespace MyVedicDay.Panchang.Business.Models;

/// <summary>
/// Panchang for one or more consecutive local dates at one location, localized into <see cref="Glossary"/>'s language.
/// </summary>
/// <param name="Location">The location actually used, after coordinate normalization and time zone resolution.</param>
/// <param name="Tradition">Panchang tradition (regional convention) applied to the presentation.</param>
/// <param name="CalculationMethod">Calculation method used by the tradition, e.g. <c>drik</c>.</param>
/// <param name="CalculationVersion">Version of the calculation algorithms and rule tables.</param>
/// <param name="LocalToday">Current local date at the location when the request was served.</param>
/// <param name="LocalTodayDisplay"><paramref name="LocalToday"/> in the location's date format.</param>
/// <param name="DisplayFormat">Format used for every display string in the result.</param>
/// <param name="GeneratedAt">UTC instant the response was assembled.</param>
/// <param name="Glossary">Meanings and descriptions of every item kind, included once rather than per item.</param>
/// <param name="Days">One entry per requested date, in date order.</param>
public sealed record PanchangResult(
    LocationDetails Location,
    PanchangTradition Tradition,
    string CalculationMethod,
    string CalculationVersion,
    DateOnly LocalToday,
    string LocalTodayDisplay,
    DateTimeDisplayFormat DisplayFormat,
    DateTimeOffset GeneratedAt,
    GlossaryResult Glossary,
    IReadOnlyList<DailyPanchang> Days);
