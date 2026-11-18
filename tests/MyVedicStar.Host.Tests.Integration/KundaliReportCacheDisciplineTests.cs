using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using MyVedicStar.API.Controllers;
using Xunit;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>
/// KUN-17 / api#57 gap 3, second half. Revocation is only stateless ON THE SERVER if the download response cannot be
/// cached by anything in between: a CDN, a proxy, or the client holding a 200 defeats every expiry check we perform.
/// So `no-store` is not a hardening nicety here - without it the live re-check never runs on the second request.
/// </summary>
public sealed class KundaliReportCacheDisciplineTests
{
    [Fact]
    public void The_report_routes_carry_the_no_store_filter()
    {
        // Guarded by reflection because the routes are added in the same PR as this test; asserting on the attribute
        // rather than a live response keeps it a statement about the code instead of about the test host.
        var reportActions = typeof(KundaliController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.Contains("Report", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(reportActions);

        foreach (var action in reportActions)
        {
            Assert.True(
                action.GetCustomAttributes<RevisionCacheHeadersAttribute>().Any(),
                $"{action.Name} must carry no-store cache discipline: a cacheable report download defeats revocation.");
        }
    }

    [Fact]
    public void The_no_store_value_is_exactly_private_no_store()
    {
        // "no-store" alone allows a private cache; "private" alone allows storage. The pair is what forbids both, and
        // the report body carries rendered birth details.
        Assert.Equal("private, no-store", RevisionCacheHeadersAttribute.CacheControlValue);
    }
}
