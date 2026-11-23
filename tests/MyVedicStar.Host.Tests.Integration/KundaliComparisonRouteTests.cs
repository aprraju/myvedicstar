extern alias SharedHost;

using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>
/// api#55: the comparison route is REGISTERED on the composed host, not merely implemented on a facade.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> The facade tests for the comparison (KundaliComparisonCrossAccountTests) assert
/// behaviour through <c>IKundaliFacade</c>. A controller method that is never decorated, or a route that never reaches
/// the composed endpoint table, would leave every one of them green: the facade is reachable by constructing a scope,
/// which is not the same claim as "an HTTP request reaches it". Review finding on api#55, PR #204.</para>
/// <para><b>Read from the endpoint table, not from an HTTP call.</b> This assertion is about registration and shape -
/// the composition question - so it needs no database, no profile, and no authenticated principal, which is why it can
/// be claimed while the storage dependencies are unprovisioned. The behavioural claims stay in the facade tests.</para>
/// </remarks>
public sealed class KundaliComparisonRouteTests
{
    private static RouteEndpoint[] KundaliRouteEndpoints(SharedHostTests.CombinedFactory factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToArray();

    [Fact]
    public void The_comparison_route_is_registered_with_both_profile_ids_as_guids()
    {
        using var factory = new SharedHostTests.CombinedFactory();

        var matches = KundaliRouteEndpoints(factory)
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("comparisons", StringComparison.Ordinal) == true)
            .ToArray();

        // Exactly one: a second route for the same pair would make "which handler answered" ambiguous, and the pair
        // rule lives in one place.
        var endpoint = Assert.Single(matches);

        Assert.Equal("GET", endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
            .SingleOrDefault());
        Assert.Equal("api/v1/kundali/comparisons/{profileA:guid}/{profileB:guid}",
            endpoint.RoutePattern.RawText);

        var action = endpoint.Metadata.GetRequiredMetadata<ControllerActionDescriptor>();
        Assert.Equal("KundaliController", action.ControllerTypeInfo.Name);
        Assert.Equal("Compare", action.ActionName);

        // Both ids are constrained to guids, so a malformed id is a 404 from routing rather than reaching the facade -
        // which matters here because the ownership rule is the facade's, and a routing-level guess must not preempt it.
        var parameters = endpoint.RoutePattern.Parameters
            .Select(parameter => (parameter.Name, Inline: parameter.ParameterPolicies.Count))
            .ToArray();
        Assert.Equal(2, parameters.Length);
        Assert.All(parameters, parameter => Assert.True(parameter.Inline > 0,
            "each profile id must carry its guid constraint"));
    }

    [Fact]
    public void The_registered_comparison_route_requires_authorization()
    {
        using var factory = new SharedHostTests.CombinedFactory();

        var endpoint = KundaliRouteEndpoints(factory)
            .Single(candidate => candidate.RoutePattern.RawText?.Contains("comparisons", StringComparison.Ordinal) == true);

        // An anonymous comparison route would let anyone holding two profile ids read cross-account compatibility, which
        // is the exact leak the ownership tests in the unit suite exist to prevent one layer down.
        Assert.Null(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>());
        Assert.NotEmpty(endpoint.Metadata
            .GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
    }
}
