using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Api.Tests.Integration.Infrastructure;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Jpl.Provider;

namespace MyVedicDay.Api.Tests.Integration.Configuration;

/// <summary>
/// Pins the UNCHANGED state of the provider cutover (AST-12, #77): an absent <c>Astronomy:Provider</c> resolves to the
/// analytical provider, and a host with no native toolkit still serves every request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a named test.</b> <c>astronomy/FR-06-CUTOVER-RECORD.md</c> records 40 <c>SpiceUnavailableException</c>
/// failures on <c>MyVedicDay.Api.Tests.Integration</c> when the absent-key default was flipped before the toolkit was
/// provisioning. That number was an observation in prose; nothing in the suite asserted the property that produced it.
/// A future flip would therefore break the guarantee and the suite could stay green - exactly the failure mode api#173
/// removed for the provider itself, one level up.
/// </para>
/// <para>
/// <b>Both halves are asserted here.</b> A resolution assertion alone would pass on a host that has the toolkit and
/// would not say whether a toolkit-free host still ANSWERS. So the second half removes any hint of tooling from the
/// environment - it can only fail if the analytical path is genuinely serving.
/// </para>
/// </remarks>
public sealed class ProviderCutoverGuardTests
{
    private const string Hyderabad = "latitude=17.385&longitude=78.4867";

    [Fact]
    public void An_absent_provider_key_resolves_to_the_analytical_provider()
    {
        var selection = ProductProviderSelection.Resolve(null);

        Assert.Equal(ProviderSelection.Analytical, selection);
        Assert.Equal(ProviderSelectionParser.AnalyticalKey, ProviderSelectionParser.ToKey(selection));

        // Absence and an explicit analytical key must agree: if they can disagree, the default is not a default.
        Assert.Equal(selection, ProductProviderSelection.Resolve(""));
        Assert.Equal(selection, ProductProviderSelection.Resolve("   "));
        Assert.Equal(selection, ProductProviderSelection.Resolve(ProviderSelectionParser.AnalyticalKey));
    }

    [Fact]
    public async Task A_host_without_the_native_toolkit_serves_every_request_on_the_absent_key_path()
    {
        // Belt and braces on the premise this test rests on: on a host that HAS the toolkit this branch would be
        // vacuous, so it says so rather than passing quietly.
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        Assert.False(engine.IsNativeToolkitAvailable,
            "This test is only meaningful where CSPICE is absent. On a provisioned host it cannot distinguish the " +
            "analytical path from the SPICE path, so it must not be reported as proof there.");

        using var factory = new ApiTestFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            // No Astronomy:Provider at all, and no Astronomy:Spice section: the exact composition a bare host has.
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Astronomy:Provider"] = null,
                    ["Astronomy:Spice:ToolkitRoot"] = null,
                    ["Astronomy:Spice:KernelPath"] = null
                }));
        });
        using var client = host.CreateClient();

        // The provider actually composed is the analytical one - not merely configured as such.
        var ephemeris = host.Services.GetRequiredService<IEphemeris>();
        var selected = host.Services.GetRequiredService<SelectedAstronomyProvider>();
        Assert.Equal(ProviderSelection.Analytical, selected.Selection);
        Assert.DoesNotContain("Spice", ephemeris.GetType().Name, StringComparison.OrdinalIgnoreCase);

        // And a toolkit-free host still answers. /health/live proves the host started; the Panchang request proves the
        // astronomy path ran, which is the part the 40 failures were about.
        using var health = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        using var panchang = await client.GetAsync(new Uri($"/api/v1/panchang/today?{Hyderabad}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, panchang.StatusCode);
        var body = await panchang.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.DoesNotContain("SpiceUnavailableException", body, StringComparison.Ordinal);
    }
}
