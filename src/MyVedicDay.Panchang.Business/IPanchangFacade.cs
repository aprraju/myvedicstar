using MyVedicDay.Panchang.Business.Models;

namespace MyVedicDay.Panchang.Business;

/// <summary>
/// Public entry point to Panchang functionality. "Today" is always the current local date at the requested
/// location, never the server's date.
/// </summary>
/// <remarks>
/// Every method validates its input and throws <see cref="MyVedicDay.Shared.Exceptions.ValidationException"/>
/// for invalid coordinates, time zones, traditions or dates, and
/// <see cref="MyVedicDay.Shared.Exceptions.BusinessRuleException"/> when the Panchang is undefined
/// (for example no sunrise during polar night).
/// </remarks>
public interface IPanchangFacade
{
    Task<PanchangResult> GetTodayAsync(PanchangQuery query, CancellationToken cancellationToken);

    Task<PanchangResult> GetTomorrowAsync(PanchangQuery query, CancellationToken cancellationToken);

    Task<PanchangResult> GetDateAsync(PanchangQuery query, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Returns <paramref name="days"/> consecutive days starting at <paramref name="startDate"/>.</summary>
    Task<PanchangResult> GetDateRangeAsync(PanchangQuery query, DateOnly startDate, int days, CancellationToken cancellationToken);

    /// <summary>Returns ten consecutive days starting with the location's local today.</summary>
    Task<PanchangResult> GetTenDaysAsync(PanchangQuery query, CancellationToken cancellationToken);

    Task<PanchangResult> GetMonthAsync(PanchangQuery query, int year, int month, CancellationToken cancellationToken);

    Task<PanchangResult> GetYearAsync(PanchangQuery query, int year, CancellationToken cancellationToken);
}
