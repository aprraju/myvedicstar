using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Kundali.Business;
using MyVedicStar.Kundali.Shared;
using MyVedicStar.Settings.Business;
using MyVedicStar.VedicProfiles.Business;

namespace MyVedicStar.Kundali.Integration;

/// <summary>Read-only adapter over the public Authorize, Vedic Profiles and Settings facades. Depends on interfaces
/// only: it must never reference another module's DbContext or repository, and never writes profile, identity or
/// settings data. The caller's principal is threaded explicitly, never held in ambient state.</summary>
internal sealed class KundaliProfileReader(IAuthorizeFacade authorize, IVedicProfilesFacade profiles,
    ISettingsFacade settings) : IKundaliProfileReader
{
    public Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token) =>
        // An internally constructed principal carries its account explicitly (a background worker acts for the account
        // it was queued for); every request principal is resolved by the owning Authorize module as before.
        KundaliPrincipal.IsInternal(user)
            ? Task.FromResult(Guid.Parse(user.FindFirst(KundaliPrincipal.AccountClaim)!.Value))
            : authorize.GetRegisteredAccountIdAsync(user, token);

    public async Task<KundaliInputSnapshot?> GetSnapshotAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        // Reading through the profile facade (rather than re-querying the profiles schema) keeps the ownership rule
        // in exactly one place: the profile module decides what this principal may see. A foreign or deleted
        // profile surfaces as "not found", which the business layer turns into a 404.
        try
        {
            var profile = await profiles.GetAsync(user, profileId, token);
            return new KundaliInputSnapshot(profile.Details.DateOfBirth, profile.Details.BirthTimeMode,
                profile.Details.BirthTime, profile.Details.BirthTimeFrom, profile.Details.BirthTimeTo,
                profile.Details.Latitude, profile.Details.Longitude, profile.Details.TimezoneId, profile.Revision);
        }
        catch (MyVedicStar.Shared.Exceptions.ResourceNotFoundException) { return null; }
    }

    public async Task<KundaliProfileDisplay?> GetDisplayAsync(ClaimsPrincipal user, Guid profileId,
        CancellationToken token)
    {
        // Same facade call and the same not-found mapping as GetSnapshotAsync, so ownership can never disagree
        // between the calculation path and the read path.
        try
        {
            var profile = await profiles.GetAsync(user, profileId, token);
            var d = profile.Details;
            return new KundaliProfileDisplay(d.Name, d.DateOfBirth, d.BirthTimeMode, d.BirthTime, d.BirthTimeFrom,
                d.BirthTimeTo, d.LocationName, d.Latitude, d.Longitude, d.TimezoneId);
        }
        catch (MyVedicStar.Shared.Exceptions.ResourceNotFoundException) { return null; }
    }

    public async Task<string> GetAyanamsaAsync(ClaimsPrincipal user, CancellationToken token) =>
        (await settings.GetAsync(user, token)).Ayanamsa;
}

public static class KundaliIntegrationRegistration
{
    public static IServiceCollection AddKundaliIntegration(this IServiceCollection services)
    {
        services.AddScoped<IKundaliProfileReader, KundaliProfileReader>();
        services.AddKundaliAstronomyMetadata();
        services.AddKundaliCalculationEngine();
        services.AddKundaliDivisionalMapper();
        services.AddKundaliDignityRules();
        services.AddKundaliAshtakavargaRules();
        services.AddKundaliAshtakootaRules();
        // The result cache is the shared application cache behind a port: no new cache infrastructure is introduced
        // (KUN-21 Architect refinement). Registered as TryAdd so a host that has already registered an implementation -
        // or a test that supplies a fake - keeps it.
        services.TryAddScoped<IKundaliResultCache, KundaliResultCacheAdapter>();
        services.AddKundaliShadbalaRules();
        services.AddKundaliYogaRules();
        return services;
    }
}
