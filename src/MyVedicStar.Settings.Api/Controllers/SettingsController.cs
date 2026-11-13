using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicStar.API.Contracts;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;
using MyVedicStar.Settings.Business;
namespace MyVedicStar.API.Controllers;

/// <summary>Preferences and locations belonging exclusively to the authenticated account.</summary>
[ApiController, Authorize(Policy = AuthPolicyNames.ApiScope), Route("api/v1/settings")]
[EnableRateLimiting(RateLimitPolicyNames.AuthenticatedStandard)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ProblemDetails>(403), ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(503)]
public sealed class SettingsController(ISettingsFacade facade) : ControllerBase
{
    /// <summary>Reads saved defaults; never creates a settings record.</summary>
    [HttpGet, ProducesResponseType<SettingsResponse>(200)]
    public async Task<ActionResult<SettingsResponse>> Get(CancellationToken token) => Ok(SettingsResponse.From(await facade.GetAsync(User, token)));
    /// <summary>Explicitly saves defaults after validating tradition, chart style, ayanamsa and account-owned location.</summary>
    [HttpPut, ProducesResponseType<SettingsResponse>(200)]
    public async Task<ActionResult<SettingsResponse>> Save(SaveSettingsRequest request, CancellationToken token) => Ok(SettingsResponse.From(await facade.SaveAsync(User, new(request.DefaultTradition, request.DefaultLocationId, request.Revision, request.ChartStyle, request.Ayanamsa), token)));
    /// <summary>Lists only this account's saved locations.</summary>
    [HttpGet("locations"), ProducesResponseType<SavedLocationResponse[]>(200)]
    public async Task<ActionResult<SavedLocationResponse[]>> Locations(CancellationToken token) => Ok((await facade.LocationsAsync(User, token)).Select(SavedLocationResponse.From).ToArray());
    /// <summary>Saves a location idempotently without changing saved defaults.</summary>
    [HttpPost("locations"), ProducesResponseType<SavedLocationResponse>(200)]
    public async Task<ActionResult<SavedLocationResponse>> Add(SavedLocationRequest request, CancellationToken token) => Ok(SavedLocationResponse.From(await facade.AddAsync(User, new(request.City, request.State, request.Country, request.CountryCode, request.PostalCode, request.Latitude, request.Longitude, request.TimeZone), token)));
    /// <summary>Deletes an owned location and atomically clears it if it was the saved default.</summary>
    [HttpDelete("locations/{locationId:guid}"), ProducesResponseType<SettingsResponse>(200)]
    public async Task<ActionResult<SettingsResponse>> Delete(Guid locationId, CancellationToken token) => Ok(SettingsResponse.From(await facade.DeleteAsync(User, locationId, token)));
}
