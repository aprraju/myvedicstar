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
    /// api#57 falsifier: each report route declares only the statuses a code path in THIS host can actually produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is split per route rather than one set for both.</b> The two routes have different producers: the POST
    /// validates before it touches the store, so it can reject a bad request; the GET has no validator at all. A single
    /// shared set would have to be the union, and a union lets one route declare a status only the other can produce -
    /// which is the same lie api#57 was opened for, just harder to see.
    /// </para>
    /// <para>
    /// <b>What "can produce" means here, and why it is narrower than api#57's PR intended.</b> This host registers
    /// `UnconfiguredReportStore`, whose every member throws `NotSupportedException`. So on THIS host the honest set is
    /// 500 at both routes; 202, 400 and 404 are each reachable in the product only once a real store is composed. They
    /// are declared on the controller today and are therefore listed below as unearned-by-this-host, not as accepted:
    /// widening the assertion to the observed list would make the guard agree with the code and stop asking its own
    /// question. The test fails while the declaration is ahead of the producer, which is the point.
    /// </para>
    /// <para>
    /// <b>410 is deliberately absent.</b> `KundaliReportDownloadLease` still implements the expiry rule, but there is no
    /// download route and no expiry producer on an HTTP path, so declaring 410 would be a status nothing can return.
    /// </para>
    /// <para>
    /// Read by reflection rather than through MVC's action-descriptor provider so it needs no host, matching
    /// `KundaliReportCacheDisciplineTests`.
    /// </para>
    /// </remarks>
    [Fact]
    public void Each_report_route_declares_only_statuses_it_can_produce()
    {
        // Assert the name SET, not a count: a count tells the next reader that something changed, not which route
        // drifted. This test exists to be readable at the moment it fails.
        Assert.Equal(
            new[] { nameof(KundaliController.GetReport), nameof(KundaliController.RequestReport) },
            ReportActions().Select(action => action.Name).Order().ToArray());

        // --- POST {profileId}/reports -----------------------------------------------------------------------------
        // Declared: 202 (accepted, written by AcceptedAtAction), 400 (KundaliReportRequestValidator throws
        // ValidationException, mapped at GlobalExceptionHandler.cs:53), 404 (profiles.GetRegisteredAccountIdAsync /
        // profile lookup throws ResourceNotFoundException, KundaliFacade.cs:736-747).
        // Reachable on THIS host: none of the three. EnqueueAsync is UnconfiguredReportStore's throwing member
        // (KundaliBusinessRegistration.cs:327-339), so every request fails as 500 before any of them is earned - and the
        // 400 path is behind the same facade, which resolves the profile first.
        var postDeclared = Declared(FindAction(nameof(KundaliController.RequestReport)));
        AssertDeclarationMatchesThisHost(postDeclared,
            "POST {profileId}/reports",
            intendedByApi57: [StatusCodes.Status202Accepted, StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound],
            earnedOnThisHost: StatusCodes.Status500InternalServerError,
            missingProducer: "UnconfiguredReportStore.EnqueueAsync throws NotSupportedException (KundaliBusinessRegistration.cs:327)");

        // --- GET {profileId}/reports/{reportId} -------------------------------------------------------------------
        // Declared: 200 (Ok over a stored report) and 404 (FindAsync returns null -> ResourceNotFoundException at
        // KundaliFacade.cs:1200). Reachable on THIS host: 500 only, because FindAsync is the same throwing fallback.
        var getDeclared = Declared(FindAction(nameof(KundaliController.GetReport)));
        AssertDeclarationMatchesThisHost(getDeclared,
            "GET {profileId}/reports/{reportId}",
            intendedByApi57: [StatusCodes.Status200OK, StatusCodes.Status404NotFound],
            earnedOnThisHost: StatusCodes.Status500InternalServerError,
            missingProducer: "UnconfiguredReportStore.FindAsync throws NotSupportedException (KundaliBusinessRegistration.cs:334)");
    }

    /// <summary>
    /// Asserts the declared set equals this host's earned status, and explains every declared-but-unearned code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Equality, not containment.</b> The guard asks "does the declaration match what can be produced here". A
    /// superset fails (a promise nothing keeps); a subset fails too, because a route that can produce 500 and does not
    /// declare it hides its own failure from the client's generated types.
    /// </para>
    /// <para>
    /// The message is assembled from the arguments so it cannot drift from the route it describes, and it names the
    /// missing producer for each surplus code - "declare a status only when a path produces it" is unactionable
    /// without saying which path is absent.
    /// </para>
    /// </remarks>
    private static void AssertDeclarationMatchesThisHost(
        int[] declared,
        string route,
        int[] intendedByApi57,
        int earnedOnThisHost,
        string missingProducer)
    {
        // 410 is refused outright on both report routes: no download route exists and no expiry is produced on an
        // HTTP path, so `Gone` would be a status nothing can return. Asserted independently of the intended set so
        // adding it to either list cannot make it acceptable.
        Assert.DoesNotContain(StatusCodes.Status410Gone, declared);

        var surplus = declared.Where(code => code != earnedOnThisHost).Order().ToArray();
        var missing = declared.Contains(earnedOnThisHost) ? [] : new[] { earnedOnThisHost };

        var report =
            $"{route} declares [{string.Join(", ", declared)}]; this host can produce only {earnedOnThisHost}. "
            + $"Surplus: [{string.Join(", ", surplus)}]. Missing: [{string.Join(", ", missing)}]. "
            + (surplus.Length == 0 ? string.Empty : $"Those codes are the api#57 intent and need: {missingProducer}. ")
            + "Fix by moving the declaration to the change that composes the producer, or by applying a ruling that "
            + "makes the status honest. Do not add the surplus codes to an expected list: that would make this guard "
            + "agree with the code instead of checking it.";

        // The intended set is named so the failure states what was WANTED versus what is DECLARED, which is the
        // distinction a reader needs and a bare expected/actual pair does not carry.
        Assert.Equal([earnedOnThisHost], declared);
    }

    private static MethodInfo[] ReportActions() => typeof(KundaliController)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(method => method.Name is nameof(KundaliController.RequestReport) or nameof(KundaliController.GetReport))
        .ToArray();

    private static MethodInfo FindAction(string name) =>
        ReportActions().Single(action => action.Name == name);

    private static int[] Declared(MethodInfo action) => action.GetCustomAttributes<ProducesResponseTypeAttribute>()
        .Select(attribute => attribute.StatusCode)
        .Order()
        .ToArray();
}
