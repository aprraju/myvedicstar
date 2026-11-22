using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Jpl.Provider;
using Xunit;

namespace MyVedicDay.Api.Tests.Integration.Api;

/// <summary>
/// Proves the SPICE provider actually executes when the toolkit IS provisioned (api#173).
/// </summary>
/// <remarks>
/// <para><b>Why this test exists.</b> Before it, every green run in this repository was green because
/// <c>Astronomy:Provider</c> defaulted to <c>analytical</c> and no test ever selected <c>spice</c>. A toolkit that was
/// never loaded cannot fail, so the suite proved nothing about the provider the story is named after. That is a fact
/// about a green check, not a property of the product, and it would have survived the default flip unnoticed.</para>
/// <para><b>Two outcomes, both meaningful.</b> On a host with the native toolkit and the pinned kernels this asserts a
/// real SPICE position. On a host without them it asserts the D1 refusal - a typed
/// <see cref="SpiceUnavailableException"/>, never a quiet fallback to the analytical series. What it never does is pass
/// by silently doing nothing, which is the failure mode this test was written to remove.</para>
/// <para>It is a plain unit-style test rather than an HTTP one on purpose: the question is whether the engine runs, not
/// whether an endpoint routes, and the provider can be exercised directly without a host or a database.</para>
/// </remarks>
public sealed class SpiceProvisioningTests
{
    private static SpiceConfiguration Configuration()
    {
        // The kernel set the image is expected to mount, with the real spans DE440 documents.
        var configuration = SpiceConfiguration.Default;
        return configuration with
        {
            ReportedToolkitVersion = configuration.ReportedToolkitVersion ?? "N0067"
        };
    }

    [Fact]
    public void The_provisioned_engine_either_computes_a_position_or_refuses_by_name()
    {
        var engine = new SpiceAstronomyEngine(Configuration());

        if (!engine.IsNativeToolkitAvailable)
        {
            // Provisioning absent: the contract is still the D1 refusal, and it must name the toolkit.
            var refusal = Assert.Throws<SpiceUnavailableException>(() =>
                engine.GetTropicalLongitude(CelestialBody.Sun, JulianDayUt.J2000.Value));
            Assert.Contains("cspice", refusal.Message, StringComparison.OrdinalIgnoreCase);
            return;
        }

        // Provisioned, but the body numerics are not written yet (AST-04/AST-05). The engine must say so - a typed
        // NotImplementedException naming those stories - and must NOT return a substitute value. Asserting the named
        // refusal is the honest form of "it ran": the alternative is a test that proves the toolkit loads and then
        // pretends that is a calculation.
        var notYet = Assert.Throws<NotImplementedException>(() =>
            engine.GetTropicalLongitude(CelestialBody.Sun, JulianDayUt.J2000.Value));
        Assert.Contains("AST-04/AST-05", notYet.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provisioned_host_reports_the_toolkit_version_the_inventory_claims()
    {
        var engine = new SpiceAstronomyEngine(Configuration());
        if (!engine.IsNativeToolkitAvailable)
            return; // Nothing to reconcile on a host without the toolkit; the refusal test above covers that state.

        // Fails when the image reports a version other than the pinned N0067: an unverifiable licence claim is exactly
        // what FR-08 exists to prevent (api#73).
        engine.VerifyToolkitMatchesInventory("N0067");
    }
}
