namespace MyVedicDay.Panchang.Business.Models;

/// <summary>
/// A Panchang tradition (regional convention). Location drives the calculation; the tradition drives which timings
/// are shown, their order of emphasis and their regional names.
/// </summary>
/// <param name="Key">Stable identifier, e.g. <c>telugu</c>, <c>tamil</c>, <c>north-indian</c>, <c>all</c>.</param>
/// <param name="Name">Localized display name (shared across domains).</param>
/// <param name="Description">Localized description of which Panchang timings the tradition shows.</param>
/// <param name="Display">
/// Timings shown, in order of emphasis: period kinds (e.g. <c>Varjyam</c>) or the <c>Choghadiya</c> group.
/// </param>
public sealed record PanchangTradition(string Key, string Name, string? Description, IReadOnlyList<string> Display);

/// <summary>Panchang's profile of every tradition, in one language.</summary>
public sealed record PanchangTraditionsResult(string Language, IReadOnlyList<PanchangTradition> Traditions);
