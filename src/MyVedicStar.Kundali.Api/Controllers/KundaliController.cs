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
    [HttpGet("{profileId:guid}"), RevisionCacheHeaders, ProducesResponseType<KundaliViewResponse>(200), ProducesResponseType(304)]
    public async Task<ActionResult<KundaliViewResponse>> Get(Guid profileId, CancellationToken token) =>
        Ok(KundaliViewResponse.From(await facade.GetAsync(User, profileId, token)));

    /// <summary>KUN-05 overview slice: birth details, calculation conventions and whichever astrological blocks can
    /// currently be produced. Blocks that cannot be computed are omitted and reported in <c>unsupported</c> with a
    /// reason code, so a missing engine is never presented as birth-time uncertainty. A foreign or unknown profile id
    /// returns 404 - never 403 with detail.</summary>
    [HttpGet("{profileId:guid}/overview"), RevisionCacheHeaders, ProducesResponseType<KundaliOverviewResponse>(200), ProducesResponseType(304)]
    public async Task<ActionResult<KundaliOverviewResponse>> Overview(Guid profileId, CancellationToken token) =>
        Ok(KundaliOverviewResponse.From(await facade.GetOverviewAsync(User, profileId, token)));


    /// <summary>KUN-09 dasha timeline. <c>at</c> selects the instant the period path describes; omitting it means
    /// server now, and the resolved instant is echoed in the response so the caller never has to assume it.
    /// A <c>system</c> that does not exist is a 400, never a silent fallback to another system's dates. A system that exists but is not yet validated is answered with a reason code.</summary>
    [HttpGet("{profileId:guid}/sections/dashas"), ProducesResponseType<KundaliDashaTimelineResponse>(200)]
    public async Task<ActionResult<KundaliDashaTimelineResponse>> Dashas(Guid profileId,
        [FromQuery] string? system, [FromQuery] DateTimeOffset? at, CancellationToken token) =>
        Ok(KundaliDashaTimelineResponse.From(
            await facade.GetDashasAsync(User, profileId, system ?? "vimshottari", at, token)));



    /// <summary>
    /// KUN-15: compares two owned profiles with Ashtakoota. Deliberately OUTSIDE the `sections` family: a comparison
    /// is about a pair, not about one profile's section, and nesting it under a profile id would imply the result
    /// belongs to whichever profile happened to be named first.
    /// </summary>
    /// <remarks>
    /// <para><b>Scores only, no verdict.</b> The response reports each koota, the total and a certainty. It carries no
    /// pass/fail field because the minimum acceptable-match threshold is an owner decision that is still open; nothing
    /// here invents one to fill the gap.</para>
    /// <para>Same profile twice is a 400. A profile the caller does not own is a 404, identical to a missing one, so
    /// this endpoint cannot be used to probe for another account's profiles.</para>
    /// </remarks>
    [HttpGet("comparisons/{profileA:guid}/{profileB:guid}"), ProducesResponseType<KundaliAshtakootaComparisonResponse>(200)]
    public async Task<ActionResult<KundaliAshtakootaComparisonResponse>> Compare(Guid profileA, Guid profileB,
        CancellationToken token) =>
        Ok(KundaliAshtakootaComparisonResponse.From(
            await facade.CompareAsync(User, profileA, profileB, token)));

    /// <summary>KUN-07 divisional chart. Only the divisions whose mapping rule is implemented are served; an unknown
    /// one is a 400 rather than a nearest-approximation. Placements cover the bodies the engine can produce - sun and
    /// moon - and the rest are absent with a precision status that says so, never silently blank.</summary>
    [HttpGet("{profileId:guid}/sections/charts"), ProducesResponseType<KundaliChartsSectionResponse>(200)]
    public async Task<ActionResult<KundaliChartsSectionResponse>> Charts(Guid profileId,
        [FromQuery] int divisional, CancellationToken token) =>
        Ok(KundaliChartsSectionResponse.From(
            await facade.GetChartsAsync(User, profileId, divisional == 0 ? 1 : divisional, token)));



    /// <summary>KUN-08 planets section. The response separates implemented <c>rules</c> from actual <c>placements</c>:
    /// dignity and aspect rules cover all nine grahas, but a placement appears only when the engine has a position for
    /// it. Anything absent is named in <c>unsupported</c> - never an empty cell.</summary>
    [HttpGet("{profileId:guid}/sections/planets"), ProducesResponseType<KundaliPlanetsSectionResponse>(200)]
    public async Task<ActionResult<KundaliPlanetsSectionResponse>> Planets(Guid profileId, CancellationToken token) =>
        Ok(KundaliPlanetsSectionResponse.From(await facade.GetPlanetsAsync(User, profileId, token)));



    /// <summary>KUN-11 ashtakavarga. Returns bhinnashtakavarga per planet and the sarvashtakavarga aggregate. When any
    /// required reference point is unavailable the section is withheld with a reason - a partial table would look
    /// complete and be silently wrong.</summary>
    [HttpGet("{profileId:guid}/sections/ashtakavarga"), ProducesResponseType<KundaliAshtakavargaResponse>(200)]
    public async Task<ActionResult<KundaliAshtakavargaResponse>> Ashtakavarga(Guid profileId, CancellationToken token) =>
        Ok(KundaliAshtakavargaResponse.From(await facade.GetAshtakavargaAsync(User, profileId, token)));

    /// <summary>
    /// KUN-13 transit section for an explicit reference instant. <c>at</c> selects the instant the positions describe;
    /// omitting it means server now, and the resolved instant is echoed so the caller never has to assume it. An
    /// unresolvable <c>tz</c> is a 400, never a quiet substitution of the profile's zone.
    /// </summary>
    [HttpGet("{profileId:guid}/sections/transits"), ProducesResponseType<KundaliTransitsSectionResponse>(200)]
    public async Task<ActionResult<KundaliTransitsSectionResponse>> Transits(Guid profileId,
        [FromQuery] DateTimeOffset? at, [FromQuery] string? tz, CancellationToken token) =>
        Ok(KundaliTransitsSectionResponse.From(
            await facade.GetTransitsAsync(User, profileId, at, tz, token)));

    /// <summary>
    /// KUN-13 alert consents for an owned profile. Empty subscriptions mean the reader has opted out of everything,
    /// which is the default and is reported as an empty list rather than a 404.
    /// </summary>
    [HttpGet("{profileId:guid}/alerts"), ProducesResponseType<KundaliAlertsResponse>(200)]
    public async Task<ActionResult<KundaliAlertsResponse>> Alerts(Guid profileId, CancellationToken token) =>
        Ok(KundaliAlertsResponse.From(await facade.GetAlertsAsync(User, profileId, token)));

    /// <summary>
    /// KUN-13 opt-in. Idempotent: repeating the same request leaves one consent per event type and keeps the original
    /// consent timestamp, so a retry cannot look like a new consent event. Email only, per the approved default.
    /// </summary>
    [HttpPut("{profileId:guid}/alerts"), ProducesResponseType<KundaliAlertsResponse>(200)]
    public async Task<ActionResult<KundaliAlertsResponse>> SubscribeAlerts(Guid profileId,
        KundaliAlertSubscriptionRequest request, CancellationToken token) =>
        Ok(KundaliAlertsResponse.From(await facade.GrantAlertsAsync(User, profileId, request.EventTypes,
            request.Channel, token)));

    /// <summary>KUN-13 opt-out. Removes every consent for the profile; the count is echoed so the caller can tell
    /// "revoked something" from "there was nothing to revoke".</summary>
    [HttpDelete("{profileId:guid}/alerts"), ProducesResponseType(204)]
    public async Task<IActionResult> RevokeAlerts(Guid profileId, CancellationToken token)
    {
        _ = await facade.RevokeAlertsAsync(User, profileId, token);
        return NoContent();
    }

    /// <summary>KUN-12 Shadbala and Bhava Bala. Returned as two separate top-level objects - planetary strength and house
    /// strength are different quantities and are never merged into one score.</summary>
    /// <remarks><c>complete</c> is false while any classical component is missing, which is the current state: Kala and
    /// Chesta are not producible, so <c>ratioToThreshold</c> must not be read against the classical required-rupas table.
    /// <c>bhavaBala</c> is empty with an explanation because house cusps need the unvalidated ascendant.</remarks>
    /// <summary>KUN-10 yoga and dosha assessments. Evaluated and never-evaluated combinations are returned together,
    /// each with its own completeness and reason - plus every withheld combination enumerated by name, so an empty
    /// satisfied list is never read as a clean bill of health.</summary>
    [HttpGet("{profileId:guid}/sections/bala"), ProducesResponseType<KundaliBalaResponse>(200)]
    public async Task<ActionResult<KundaliBalaResponse>> Bala(Guid profileId, CancellationToken token) =>
        Ok(KundaliBalaResponse.From(await facade.GetBalaAsync(User, profileId, token)));

    /// <summary>KUN-10 yoga and dosha assessments.</summary>
    [HttpGet("{profileId:guid}/sections/yogas"), ProducesResponseType<KundaliYogasResponse>(200)]
    public async Task<ActionResult<KundaliYogasResponse>> Yogas(Guid profileId, CancellationToken token) =>
        Ok(KundaliYogasResponse.From(await facade.GetYogasAsync(User, profileId, token)));

    /// <summary>KUN-16 remedies (api#128). Each item is bound to the finding it addresses. Only evaluated
    /// findings carry a remedy; suppressed ones are reported in <c>unsupported</c> with a reason code.</summary>
    [HttpGet("{profileId:guid}/sections/remedies"), ProducesResponseType<KundaliRemediesResponse>(200)]
    public async Task<ActionResult<KundaliRemediesResponse>> Remedies(Guid profileId, CancellationToken token) =>
        Ok(KundaliRemediesResponse.From(await facade.GetRemediesAsync(User, profileId, token)));


    /// <summary>KUN-14 evidence-based interpretations for one domain (api#54). The response enumerates every domain in
    /// the closed set with its own status and reason, so a withheld domain is visible rather than absent. An unknown
    /// domain is a 400, never a silent fallback to another one; a foreign profile id is a 404. An ABSENT domain means no
    /// filter: all five domains are returned with their own status and reason, so the plain route is well defined.</summary>
    [HttpGet("{profileId:guid}/sections/interpretations"), ProducesResponseType<KundaliInterpretationsResponse>(200)]
    public async Task<ActionResult<KundaliInterpretationsResponse>> Interpretations(Guid profileId,
        [FromQuery] string? domain, CancellationToken token) =>
        Ok(KundaliInterpretationsResponse.From(await facade.GetInterpretationsAsync(User, profileId,
            domain, token)));

    /// <summary>Starts a generation. Idempotent per profile, input snapshot and settings, so repeated clicks enqueue
    /// once. Returns 202 with the running revision.</summary>
    [HttpPost("{profileId:guid}/generations"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None),
        ProducesResponseType<KundaliViewResponse>(202)]
    public async Task<ActionResult<KundaliViewResponse>> Generate(Guid profileId, CancellationToken token) =>
        Accepted(KundaliViewResponse.From(await facade.GenerateAsync(User, profileId, token)));

    /// <summary>Retries a failed generation for the same snapshot and settings.</summary>
    [HttpPost("{profileId:guid}/retry"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None),
        ProducesResponseType<KundaliViewResponse>(202)]
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

    /// <summary>Requests a report (KUN-17). Not implemented in this host: the report store and renderer are absent, so
    /// the action refuses and no report is ever created.</summary>
    /// <remarks>
    /// The previous declaration of 202/400/404/409 was removed in api#57. It was unreachable: <c>RequestReportAsync</c>
    /// throws unconditionally, so the only status this route can produce is 500 — a 202 behind a note describing
    /// <c>Location</c> was documentation for an endpoint that does not exist. The declarations belong back in the same
    /// change that wires report generation, not before it. Cross-cutting statuses stay declared class-wide at line 18.
    /// </remarks>
    [HttpPost("{profileId:guid}/reports"), RevisionCacheHeaders,
     ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<KundaliReportResponse>> RequestReport(Guid profileId,
        KundaliReportRequestResponse request, CancellationToken token)
    {
        var report = KundaliReportResponse.From(
            await facade.RequestReportAsync(User, profileId, request.ToRequest(), token));

        return AcceptedAtAction(nameof(GetReport), new { profileId, reportId = report.ReportId }, report);
    }

    /// <summary>Reads report state, and the download URL once ready. Not implemented in this host.</summary>
    /// <remarks>
    /// The previous declaration of 200/404/410 was removed in api#57, unreachable for the same reason as the POST
    /// above: <c>GetReportAsync</c> throws unconditionally. The 410 ruling is NOT withdrawn — an expired or revoked
    /// lease remains a distinct fact from 404, and <c>KundaliReportDownloadLease</c> still implements and tests it.
    /// Only the unearned HTTP declaration is removed, to return with the surface that can produce it.
    /// </remarks>
    [HttpGet("{profileId:guid}/reports/{reportId:guid}"), RevisionCacheHeaders,
     ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<KundaliReportResponse>> GetReport(Guid profileId, Guid reportId,
        CancellationToken token) =>
        Ok(KundaliReportResponse.From(await facade.GetReportAsync(User, profileId, reportId, token)));
}