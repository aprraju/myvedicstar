using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Jpl.Provider;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>FR-02 planetary surface, plus regression tests that pin the three architect review findings.</summary>
public sealed class PlanetaryAndConfigurationTests
{
    [Theory]
    [InlineData(CelestialBody.Sun)]
    [InlineData(CelestialBody.Moon)]
    [InlineData(CelestialBody.Pluto)]
    [InlineData(CelestialBody.TrueNode)]
    [InlineData(CelestialBody.MeanNode)]
    public void All_fr02_bodies_are_supported_and_requests_construct(CelestialBody body)
    {
        var request = PlanetaryRequest.Create(body, PositionCenter.Geocentric, PositionConvention.Apparent,
            ZodiacConvention.Tropical, NodeConvention.Mean);
        Assert.Equal(body, request.Body);
    }

    [Fact]
    public void The_capability_report_lists_all_eleven_fr02_bodies_plus_both_nodes()
    {
        var report = AstronomyCapabilities.Report();
        foreach (var body in new[]
                 {
                     CelestialBody.Sun, CelestialBody.Moon, CelestialBody.Mercury, CelestialBody.Venus,
                     CelestialBody.Mars, CelestialBody.Jupiter, CelestialBody.Saturn, CelestialBody.Uranus,
                     CelestialBody.Neptune, CelestialBody.Pluto, CelestialBody.MeanNode, CelestialBody.TrueNode
                 })
            Assert.Contains(body, report.SupportedBodies);
    }

    [Fact]
    public void A_sidereal_request_without_an_ayanamsa_is_a_structured_error()
    {
        Assert.Throws<AstronomyNotSupportedException>(() => PlanetaryRequest.Create(CelestialBody.Sun,
            PositionCenter.Geocentric, PositionConvention.Apparent, ZodiacConvention.Sidereal, NodeConvention.Mean));
    }

    [Fact]
    public void A_tropical_request_carrying_an_ayanamsa_is_refused_as_ambiguous()
    {
        Assert.Throws<AstronomyNotSupportedException>(() => PlanetaryRequest.Create(CelestialBody.Sun,
            PositionCenter.Geocentric, PositionConvention.Apparent, ZodiacConvention.Tropical, NodeConvention.Mean,
            AyanamsaKind.Lahiri));
    }

    [Fact]
    public void A_topocentric_request_without_an_observer_is_refused()
    {
        Assert.Throws<AstronomyNotSupportedException>(() => PlanetaryRequest.Create(CelestialBody.Moon,
            PositionCenter.Topocentric, PositionConvention.Apparent, ZodiacConvention.Tropical, NodeConvention.Mean));
    }

    [Fact]
    public void An_observer_on_a_non_topocentric_request_is_refused()
    {
        Assert.Throws<AstronomyNotSupportedException>(() => PlanetaryRequest.Create(CelestialBody.Moon,
            PositionCenter.Geocentric, PositionConvention.Apparent, ZodiacConvention.Tropical, NodeConvention.Mean,
            observer: new ObserverLocation(17.385, 78.4867, 0)));
    }

    // ---- architect note 1: the supported span must be provable from configuration, not prose ----

    [Fact]
    public void The_shipped_kernel_set_declares_de441_so_the_extended_span_is_representable()
    {
        var kernels = SpiceConfiguration.Default.Kernels.Select(k => Path.GetFileName(k.Path)).ToArray();
        Assert.Contains("de440.bsp", kernels);
        Assert.Contains("de441.bsp", kernels);
    }

    [Fact]
    public void The_shipped_configuration_actually_covers_the_advertised_date_span()
    {
        // Proves the 1800-01-01 claim from the configuration rather than from a comment.
        Assert.True(SpiceConfiguration.Default.CoversDate(AstronomyCapabilities.MinimumSupportedDate));
        Assert.True(SpiceConfiguration.Default.CoversDate(AstronomyCapabilities.MaximumSupportedDate));

        // SupportedDateRange is now the INTERSECTION of kernel coverage and the product promise, so it must equal
        // the promise exactly (issue #83).
        var range = SpiceConfiguration.Default.SupportedDateRange;
        Assert.NotNull(range);
        Assert.Equal(AstronomyCapabilities.MinimumSupportedDate, range!.Value.From);
        Assert.Equal(AstronomyCapabilities.MaximumSupportedDate, range.Value.To);
    }

    [Fact]
    public void An_uncovered_date_is_refused_by_configuration_rather_than_attempted()
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default with
        {
            Kernels = [new SpiceKernel("/var/spice/de440.bsp", new DateOnly(1900, 1, 1), new DateOnly(2100, 1, 1))]
        });

        engine.EnsureDateIsCovered(new DateOnly(1950, 1, 1)); // inside
        Assert.Throws<AstronomyNotSupportedException>(() => engine.EnsureDateIsCovered(new DateOnly(1850, 1, 1)));
    }

    // ---- architect note 2: the toolkit version must be reported, not hardcoded ----

    [Fact]
    public void A_host_that_reports_no_toolkit_version_cannot_satisfy_the_inventory_claim()
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default with { ReportedToolkitVersion = null });
        var failure = Assert.Throws<SpiceUnavailableException>(() => engine.VerifyToolkitMatchesInventory("N0067"));
        Assert.Contains("did not report", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_toolkit_version_mismatch_against_the_inventory_is_an_error()
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default with { ReportedToolkitVersion = "N0066" });
        var failure = Assert.Throws<SpiceUnavailableException>(() => engine.VerifyToolkitMatchesInventory("N0067"));
        Assert.Contains("must agree", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_matching_toolkit_version_satisfies_the_inventory_claim()
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default with { ReportedToolkitVersion = "N0067" });
        engine.VerifyToolkitMatchesInventory("N0067");
    }

    [Fact]
    public void Provenance_reports_the_configured_toolkit_source_rather_than_a_constant_version_string()
    {
        var engine = new SpiceAstronomyEngine(SpiceConfiguration.Default);
        var provenance = engine.DescribeProvenance(new AstronomySettings(), 69.0);
        Assert.Contains("naif.jpl.nasa.gov", provenance.EphemerisSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("de440.bsp", provenance.KernelIdentifier, StringComparison.Ordinal);
        Assert.Equal(69.0, provenance.DeltaTSeconds, 9);
    }

    // ---- architect note 3: parity fixtures exist before AST-06 needs them ----

    [Fact]
    public void The_parity_gate_uses_an_arcsecond_budget_and_compares_across_the_wrap_point()
    {
        Assert.Equal(1.0, ParityGate.PlanetaryToleranceArcseconds, 9);
        Assert.Equal(36.0, ParityGate.SeparationArcseconds(359.995, 0.005), 6);

        var fixture = new ParityFixture(CelestialBody.Sun, 2451545.0, 280.0, "test", 1.0);
        Assert.True(ParityGate.Evaluate(fixture, 280.0001).Passed);
        Assert.False(ParityGate.Evaluate(fixture, 280.01).Passed);

        var across = new ParityFixture(CelestialBody.Sun, 2451545.0, 359.9999, "test", 1.0);
        Assert.True(ParityGate.Evaluate(across, 0.00005).Passed);
    }

    [Fact]
    public void Planetary_position_carries_every_frame_axis_it_was_computed_under()
    {
        // The record must echo every axis, so a consumer can never mistake which convention produced a number.
        var position = new PlanetaryPosition(CelestialBody.Mars, PositionCenter.Heliocentric,
            PositionConvention.Geometric, ZodiacConvention.Sidereal, 10, 1, 1.5, 0.5, false,
            NodeConvention.True, AyanamsaKind.Raman);

        Assert.Equal(PositionCenter.Heliocentric, position.Center);
        Assert.Equal(PositionConvention.Geometric, position.Convention);
        Assert.Equal(ZodiacConvention.Sidereal, position.Zodiac);
        Assert.Equal(AyanamsaKind.Raman, position.Ayanamsa);
    }
}
