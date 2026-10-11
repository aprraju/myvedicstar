
namespace MyVedicDay.Location;

internal sealed class LocationFacade(ILocationResolver locationResolver) : ILocationFacade
{
    public async Task<LocationDetails> ResolveAsync(LocationQuery query, CancellationToken cancellationToken) =>
        (await locationResolver.ResolveAsync(query, cancellationToken)).ToDetails();
}
