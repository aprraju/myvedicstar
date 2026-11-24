using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyVedicStar.API.Controllers;
using Xunit;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>
/// KUN-17 / api#57 gap 3 - the no-store claim asserted on the WIRE rather than by reflection.
/// </summary>
/// <remarks>
/// <para>
/// <c>KundaliReportCacheDisciplineTests</c> proves the attribute is attached to the report actions. That is a
/// statement about the code. The property the design actually rests on is that <b>a proxy cannot cache the answer</b>,
/// and an attribute that is attached but never executed - a filter registered the wrong way, an action that bypasses
/// it, a results pipeline that overwrites headers - satisfies the first claim and breaks the second.
/// </para>
/// <para>
/// So this test makes a real request and reads the real response. It deliberately uses a request whose outcome is a
/// non-2xx: what is being asserted is that the response <i>carries</i> the header, not that the body is downloadable.
/// The filter is an <c>OnActionExecuted</c> step, so a request that produced a result at all is enough to prove it ran.
/// </para>
/// </remarks>
public sealed class KundaliReportNoStoreHeaderTests
{
    [Fact]
    public async Task A_report_response_carries_no_store_on_the_wire()
    {
        using var factory = new SharedHostTests.CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.Token());

        var response = await client.GetAsync(
            "/api/v1/kundali/" + Guid.NewGuid() + "/reports/" + Guid.NewGuid());

        // The status is now part of the claim, not a sanity sidelight. api#57: this assertion used to exclude only
        // Unauthorized, so a 500 passed the guard whose comment promised "a future 401/500 does not silently still pass".
        //
        // This guard is INVERTED deliberately, per architect ruling, rather than relaxed or routed around:
        // 500 is the honest status while the report store and renderer are absent. 501 would mean reshaping the
        // exception to suit the response code - the same inversion as a 202 behind a Location note, one layer down -
        // and 503 would say "try again later" when nothing will change. So the test asserts the truthful answer:
        // the endpoint refuses, and it refuses as a server-side gap rather than as a client error.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Kept, and this is why: it proves the no-store filter RAN ON THE ERROR PATH. A pipeline that drops headers
        // when an exception escapes would leave a 500 cacheable, and the 500 assertion above would still pass.
        Assert.True(response.Headers.CacheControl?.NoStore == true,
            $"Report responses must be no-store so a proxy cannot serve a revoked report; "
            + $"Cache-Control was '{response.Headers.CacheControl}'.");
    }

    /// <summary>
    /// api#57 falsifier: the DECLARED status set for both report routes is exactly 500 - nothing more.
    /// </summary>
    /// <remarks>
    /// The wire test above proves the behaviour today. This proves the contract agrees with it, and it exists to fail
    /// the moment the two drift apart. Concretely: when report generation is wired, if 202/200/404/409/410 are declared
    /// without a code path that can produce them, that is the same defect api#57 was opened for - a declaration that
    /// lies to the client. Wired honestly, this test goes red and must be updated in the same change that earns the
    /// statuses. Wired carelessly, it goes red for the right reason.
    ///
    /// Read by reflection rather than through MVC's action-descriptor provider so it needs no host, matching
    /// KundaliReportCacheDisciplineTests.
    /// </remarks>
    /// <summary>
    /// Each report route declares exactly the codes it can produce, asserted PER ROUTE.
    /// </summary>
    /// <remarks>
    /// <para><b>Why not one shared expectation.</b> This test originally asserted <c>[500]</c> for both actions, which was
    /// true only while every route was a throwing stub. Now that the routes are real, a single list applied to both would
    /// have to be the union <c>[202, 400, 404]</c> - and that describes NEITHER route: <c>POST</c> cannot produce 404 for
    /// a missing report (it creates one) and <c>GET</c> cannot produce 202 (it does not enqueue). A test whose expected
    /// value fits no subject has stopped asking its own question, which is worse than deleting it.</para>
    /// <para><b>Every declared code names the path that produces it.</b> If a code cannot be traced to a throw or an
    /// explicit result in the controller, it does not belong in this list - declared codes are a promise the OpenAPI
    /// document makes on the route's behalf.</para>
    /// </remarks>
    [Fact]
    public void Each_report_route_declares_only_the_codes_it_can_produce()
    {
        var reportActions = typeof(KundaliController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name is nameof(KundaliController.RequestReport) or nameof(KundaliController.GetReport))
            .ToArray();

        // Assert the name SET, not a count: a count tells the next reader something changed, not which route
        // drifted. This test exists to be readable at the moment it fails.
        Assert.Equal(
            new[] { nameof(KundaliController.GetReport), nameof(KundaliController.RequestReport) },
            reportActions.Select(action => action.Name).Order().ToArray());

        int[] Declared(MethodInfo action) => action
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Order()
            .ToArray();

        var request = reportActions.Single(action => action.Name == nameof(KundaliController.RequestReport));
        Assert.Equal(
            [
                StatusCodes.Status202Accepted,   // the report was accepted and queued
                StatusCodes.Status400BadRequest, // KundaliReportRequestValidator: unknown variant, empty selection,
                                                 // undefined section, or a section this variant does not render
                StatusCodes.Status404NotFound,   // the profile lookup throws ResourceNotFoundException
            ],
            Declared(request));

        var get = reportActions.Single(action => action.Name == nameof(KundaliController.GetReport));
        Assert.Equal(
            [
                StatusCodes.Status200OK,       // the report row exists and is owned by the caller
                StatusCodes.Status404NotFound, // no row, not owned, or profile deleted - one answer on purpose
            ],
            Declared(get));

        // 500 is deliberately NOT in either list, and NOT declared anywhere: it is produced by the throwing store and
        // surfaces through the global exception handler. Declaring it would promise a code no attribute names, which is
        // the same lie as omitting one that does. The wire test above is where the 500 is asserted, because that is the
        // only place it is real.

        // 410 is deliberately absent from both: no download route and no expiry producer exist, so declaring it would be
        // an unearned promise. Asserted explicitly so a future author cannot add it without deleting this line first.
        Assert.DoesNotContain(StatusCodes.Status410Gone, Declared(request));
        Assert.DoesNotContain(StatusCodes.Status410Gone, Declared(get));
    }
}
