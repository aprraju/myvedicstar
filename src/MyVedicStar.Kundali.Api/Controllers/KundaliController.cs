using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicStar.API.Contracts;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;
using MyVedicStar.Kundali.Business;

namespace MyVedicStar.API.Controllers;

/// <summary>Kundali module endpoints. No business rules live here: request parsing and response shaping only, with
/// ownership, idempotency and capability checks enforced in the Business layer. The owner is always the authenticated
/// principal, so a foreign profile id returns 404 and never a 403 with detail.</summary>
[ApiController, Authorize(Policy = AuthPolicyNames.ApiScope), Route("api/v1/kundali")]
[EnableRateLimiting(RateLimitPolicyNames.AuthenticatedStandard)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ProblemDetails>(403), ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409), ProducesResponseType<ProblemDetails>(503)]
public sealed class KundaliController(IKundaliFacade facade) : ControllerBase
{
    /// <summary>The published supported/unsupported capability boundary. Account-independent reference data.</summary>
    [HttpGet("capabilities"), ProducesResponseType<KundaliCapabilitiesResponse>(200)]
    public ActionResult<KundaliCapabilitiesResponse> Capabilities() =>
        Ok(KundaliCapabilitiesResponse.From(facade.GetCapabilities()));

    /// <summary>Reads the aggregate and current revision for an owned profile. A profile with no Kundali yet returns
    /// 200 with <c>hasResult: false</c>, which the shell renders as "not generated yet".</summary>
    [HttpGet("{profileId:guid}"), ProducesResponseType<KundaliViewResponse>(200)]
    public async Task<ActionResult<KundaliViewResponse>> Get(Guid profileId, CancellationToken token) =>
        Ok(KundaliViewResponse.From(await facade.GetAsync(User, profileId, token)));

    /// <summary>Starts a generation. Idempotent per profile, input snapshot and settings, so repeated clicks enqueue
    /// once. Returns 202 with the running revision.</summary>
    [HttpPost("{profileId:guid}/generations"), ProducesResponseType<KundaliViewResponse>(202)]
    public async Task<ActionResult<KundaliViewResponse>> Generate(Guid profileId, CancellationToken token) =>
        Accepted(KundaliViewResponse.From(await facade.GenerateAsync(User, profileId, token)));

    /// <summary>Retries a failed generation for the same snapshot and settings.</summary>
    [HttpPost("{profileId:guid}/retry"), ProducesResponseType<KundaliViewResponse>(202)]
    public async Task<ActionResult<KundaliViewResponse>> Retry(Guid profileId, CancellationToken token) =>
        Accepted(KundaliViewResponse.From(await facade.RetryAsync(User, profileId, token)));

    /// <summary>Display preferences. Presentation-only: saving these never changes planetary facts or recalculates.</summary>
    [HttpGet("{profileId:guid}/preferences"), ProducesResponseType<KundaliPreferencesResponse>(200)]
    public async Task<ActionResult<KundaliPreferencesResponse>> GetPreferences(Guid profileId, CancellationToken token) =>
        Ok(KundaliPreferencesResponse.From(await facade.GetPreferencesAsync(User, profileId, token)));

    [HttpPut("{profileId:guid}/preferences"), ProducesResponseType<KundaliPreferencesResponse>(200)]
    public async Task<ActionResult<KundaliPreferencesResponse>> SavePreferences(Guid profileId,
        SaveKundaliPreferencesRequest request, CancellationToken token) =>
        Ok(KundaliPreferencesResponse.From(await facade.SavePreferencesAsync(User, profileId, request.ChartStyle,
            request.Revision, token)));
}
