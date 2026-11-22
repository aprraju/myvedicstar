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

        // api#173: and it must NOT be the SpiceUnavailableException refusal. This is the distinction the whole
        // provisioned job rests on: on a runner where the toolkit is installed, "refused because the toolkit is
        // missing" and "refused because the numerics are not written" are different facts, and only the second means
        // provisioning worked. Without this line both branches produce a failing call and a green test, and the
        // test cannot tell a working provisioning step from a missing one.
        Assert.IsNotType<SpiceUnavailableException>(notYet);
    }

    /// <summary>
    /// api#173: on a host that HAS the toolkit, say so explicitly, and name the version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal tests above pass on a provisioned host and on a bare one, so a CI job that installs
    /// nothing can still be green. This test is the one that can only pass when the toolkit actually
    /// loads: it asserts <see cref="SpiceAstronomyEngine.IsNativeToolkitAvailable"/> is true and that the
    /// reported version is the pinned N0067. On a host without the toolkit it fails, which is the intent -
    /// the spice-provisioning workflow provisions first and then runs this.
    /// </para>
    /// <para>
    /// <b>Skip, rather than fail, on an unprovisioned host.</b> This test runs in
    /// <c>myvedicday-ci.yml</c> too, where no toolkit is installed and must not be - that job is the
    /// broad suite, not the provisioning proof. Failing there would make the default branch red for a
    /// missing optional dependency and push people to delete the assertion. Skipping keeps the default
    /// suite green and still fails loudly in the one job whose entire purpose is to provision; the
    /// spice-provisioning workflow additionally asserts, from its own log, that this test reported
    /// exactly one PASSED rather than a skip - so a silent skip cannot be read as proof there.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void A_provisioned_host_loads_the_pinned_toolkit()
    {
        var engine = new SpiceAstronomyEngine(Configuration());

        // Fails when the job that EXISTS to provision the toolkit did not provision it, and skips when
        // the toolkit was never expected here. The environment variable is set by the spice-provisioning
        // workflow after it installs the toolkit, so:
        //   required + not loaded -> FAIL  (provisioning broke; this is the case worth going red for)
        //   not required + not loaded -> SKIP (broad suite on a bare runner; not this test's subject)
        //   loaded -> assert the pinned version, wherever it is.
        var requiresProvisioning = Environment.GetEnvironmentVariable("MYVEDICSTAR_REQUIRE_SPICE") == "1";

        if (!engine.IsNativeToolkitAvailable)
        {
            Assert.False(requiresProvisioning,
                "MYVEDICSTAR_REQUIRE_SPICE=1 was set, so this runner was supposed to have the native SPICE "
                + "toolkit, and it did not load. Provisioning failed; no SPICE number from this job is "
                + "trustworthy. Check the provisioning step's log before anything downstream.");

            Skip.If(true, "The native SPICE toolkit is not installed on this host and this job did not "
                + "require it. The provisioned-execution proof lives in spice-provisioning.yml.");
        }

        // Toolkit loaded: this is the claim no other test in this repository makes.
        engine.VerifyToolkitMatchesInventory("N0067");
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
