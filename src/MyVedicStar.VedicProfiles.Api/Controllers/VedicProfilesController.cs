using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicStar.API.Contracts;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.RateLimiting;
using MyVedicStar.VedicProfiles.Business;

namespace MyVedicStar.API.Controllers;

/// <summary>Vedic profiles belonging exclusively to the authenticated account. No business rules live here:
/// request parsing and response shaping only, with all invariants enforced in the Business layer.</summary>
[ApiController, Authorize(Policy = AuthPolicyNames.ApiScope), Route("api/v1/vedic-profiles")]
[EnableRateLimiting(RateLimitPolicyNames.AuthenticatedStandard)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ProblemDetails>(403), ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409), ProducesResponseType<ProblemDetails>(503)]
public sealed class VedicProfilesController(IVedicProfilesFacade facade) : ControllerBase
{
    /// <summary>Lists only this account's profiles; cross-account rows are never returned.</summary>
    [HttpGet, ProducesResponseType<VedicProfileSummaryResponse[]>(200)]
    public async Task<ActionResult<VedicProfileSummaryResponse[]>> List(CancellationToken token) =>
        Ok((await facade.ListAsync(User, token)).Select(VedicProfileSummaryResponse.From).ToArray());

    /// <summary>Reads one owned profile; a foreign or missing id is indistinguishable (404).</summary>
    [HttpGet("{id:guid}"), ProducesResponseType<VedicProfileResponse>(200)]
    public async Task<ActionResult<VedicProfileResponse>> Get(Guid id, CancellationToken token) =>
        Ok(VedicProfileResponse.From(await facade.GetAsync(User, id, token)));

    /// <summary>Creates a profile owned by the caller with server-generated id and revision 1.</summary>
    [HttpPost, ProducesResponseType<VedicProfileResponse>(201)]
    public async Task<ActionResult<VedicProfileResponse>> Create(VedicProfileRequest request, CancellationToken token)
    {
        var created = await facade.CreateAsync(User, request.ToDetails(), token);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, VedicProfileResponse.From(created));
    }

    /// <summary>Replaces an owned profile after a revision match; a stale revision is a safe 409.</summary>
    [HttpPut("{id:guid}"), ProducesResponseType<VedicProfileResponse>(200)]
    public async Task<ActionResult<VedicProfileResponse>> Replace(Guid id, ReplaceVedicProfileRequest request, CancellationToken token) =>
        Ok(VedicProfileResponse.From(await facade.ReplaceAsync(User, id, request.ToDetails(), request.Revision, token)));

    /// <summary>Deletes an owned profile; missing or foreign id returns 404. No revision is required.</summary>
    [HttpDelete("{id:guid}"), ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token)
    {
        await facade.DeleteAsync(User, id, token);
        return NoContent();
    }
}
