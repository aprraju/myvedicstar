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
    [Fact]
    public void Both_report_routes_declare_only_the_500_they_can_produce()
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

        foreach (var action in reportActions)
        {
            var declared = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(attribute => attribute.StatusCode)
                .Order()
                .ToArray();

            Assert.Equal([StatusCodes.Status500InternalServerError], declared);
        }
    }
}
