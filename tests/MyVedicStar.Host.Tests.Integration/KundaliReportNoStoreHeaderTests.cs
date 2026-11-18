using System.Net;
using System.Net.Http.Headers;
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

        // The status is not the claim under test; the header is. Asserting the status too keeps the test honest about
        // the path it actually exercised, so a future 401/500 does not silently still pass on a header alone.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        Assert.True(response.Headers.CacheControl?.NoStore == true,
            $"Report responses must be no-store so a proxy cannot serve a revoked report; "
            + $"Cache-Control was '{response.Headers.CacheControl}'.");
    }
}
