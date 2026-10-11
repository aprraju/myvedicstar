using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Panchang.Business;

/// <summary>
/// Localized meanings and descriptions of Panchang items, for help screens and client-side caching.
/// </summary>
public interface IGlossaryFacade
{
    /// <param name="preferredLanguages">Language tags in order of preference; the default language is used if none is supported.</param>
    /// <param name="tradition">Tradition whose timings and regional names to use; the default (all) when omitted.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="MyVedicDay.Shared.Exceptions.ValidationException">Unknown tradition.</exception>
    Task<GlossaryResult> GetGlossaryAsync(IReadOnlyList<string>? preferredLanguages, string? tradition, CancellationToken cancellationToken);
}

/// <summary>
/// Panchang's view of each tradition: which timings it shows, in order of emphasis.
/// </summary>
public interface IPanchangTraditionFacade
{
    /// <param name="preferredLanguages">Language tags in order of preference.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<PanchangTraditionsResult> GetTraditionsAsync(IReadOnlyList<string>? preferredLanguages, CancellationToken cancellationToken);
}
