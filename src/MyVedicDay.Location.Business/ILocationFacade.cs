
namespace MyVedicDay.Location;

/// <summary>
/// Resolves locations without calculating anything, for example so a client can confirm "Austin, Texas, United States"
/// or label the browser's position before loading the calendar.
/// </summary>
public interface ILocationFacade
{
    /// <exception cref="MyVedicDay.Shared.Exceptions.ValidationException">Invalid input or no matching city.</exception>
    Task<LocationDetails> ResolveAsync(LocationQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves a client location to validated coordinates and an authoritative time zone. Used by every calculation domain.
/// </summary>
public interface ILocationResolver
{
    /// <exception cref="MyVedicDay.Shared.Exceptions.ValidationException">Invalid coordinates, time zone or place, or no matching city.</exception>
    /// <exception cref="MyVedicDay.Shared.Exceptions.BusinessRuleException">No time zone can be determined.</exception>
    Task<ResolvedLocation> ResolveAsync(LocationQuery query, CancellationToken cancellationToken);
}
