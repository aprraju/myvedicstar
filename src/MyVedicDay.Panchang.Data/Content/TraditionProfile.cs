namespace MyVedicDay.Panchang.Data.Content;

/// <summary>
/// A Panchang tradition (regional convention): which calculation method it uses and which timings it shows.
/// </summary>
/// <param name="Key">Stable identifier, e.g. <c>telugu</c>, <c>tamil</c>, <c>north-indian</c>, <c>all</c>.</param>
/// <param name="CalculationMethod">Calculation method key understood by the calculation engine, e.g. <c>drik</c>.</param>
/// <param name="Display">
/// Timings to show, in order of emphasis: period kinds (e.g. <c>RahuKalam</c>, <c>Varjyam</c>) or period groups
/// (<c>Choghadiya</c>).
/// </param>
public sealed record TraditionProfile(string Key, string CalculationMethod, IReadOnlyList<string> Display);

/// <summary>
/// Source of Panchang tradition profiles. Built-in profiles are embedded; <c>panchang-traditions.json</c> in the
/// content directory replaces profiles by key or adds new ones.
/// </summary>
public interface ITraditionProfileRepository
{
    /// <summary>All profiles, in display order.</summary>
    /// <exception cref="MyVedicDay.Shared.Exceptions.DataAccessException">Profiles could not be read.</exception>
    Task<IReadOnlyList<TraditionProfile>> GetProfilesAsync(CancellationToken cancellationToken);
}
