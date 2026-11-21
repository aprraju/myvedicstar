using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// FR-06 provider selection, delegation and parity.
/// </summary>
/// <remarks>
/// The delegation test is the important one. "IEphemeris returns a number" proves nothing; a version with its own
/// parallel maths would pass that. These tests replace the engine with a known stub and assert that <b>every</b>
/// port method changes with it, which is only true when there is a single calculation path.
/// </remarks>
public sealed class ProviderMigrationTests
{
    private const double JulianDay = 2448908.5;

    [Fact]
    public void An_unknown_provider_key_fails_at_registration_not_at_first_request()
    {
        var services = new ServiceCollection();
        var failure = Assert.Throws<ArgumentException>(() =>
            services.AddAstronomyProvider("swisseph"));
        Assert.Contains("Unknown astronomy provider key", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_provider_key_is_an_error_not_a_silent_analytical_default()
    {
        var services = new ServiceCollection();
        var failure = Assert.Throws<ArgumentException>(() => services.AddAstronomyProvider(null));
        Assert.Contains("not treated as 'analytical'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selecting_spice_without_an_engine_is_an_error_rather_than_a_fallback()
    {
        var services = new ServiceCollection();
        var failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddAstronomyProvider("spice", configureSpice: null));
        Assert.Contains("silently change results", failure.Message, StringComparison.Ordinal);
    }

    // ---- AST-12 (#77): the production default is the SPICE provider, and the old provider stays reachable ----

    [Fact]
    public void An_absent_key_resolves_to_the_documented_default_in_one_place()
    {
        // AST-12 (#77) ruling: the default stays analytical until the CSPICE toolkit is provisioned (AST-05, #70).
        // The cutover composition is complete and tested; the flip is deferred, not forgotten.
        Assert.Equal(ProviderSelection.Analytical, ProductProviderSelection.Resolve(null));
        Assert.Equal(ProviderSelection.Analytical, ProductProviderSelection.Resolve("   "));

        // The declared default is a key the parser accepts, so absence can never produce an unparseable value.
        Assert.Equal(ProviderSelection.Analytical, ProviderSelectionParser.Parse(ProductProviderSelection.DefaultKey));
    }

    [Fact]
    public void The_SPICE_provider_is_opt_in_by_configuration_after_the_deferral()
    {
        Assert.Equal(ProviderSelection.Spice, ProductProviderSelection.Resolve("spice"));
        Assert.Equal(ProviderSelection.Compare, ProductProviderSelection.Resolve("compare"));
    }

    [Fact]
    public void A_configured_key_still_wins_over_the_cutover_default()
    {
        Assert.Equal(ProviderSelection.Analytical, ProductProviderSelection.Resolve("analytical"));
        Assert.Equal(ProviderSelection.Compare, ProductProviderSelection.Resolve("COMPARE "));
    }

    [Fact]
    public void An_unknown_configured_key_is_still_an_error_after_the_cutover()
    {
        var failure = Assert.Throws<ArgumentException>(() => ProductProviderSelection.Resolve("swisseph"));
        Assert.Contains("Unknown astronomy provider key", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_provider_is_constructible_from_the_production_engine_type()
    {
        // The cutover is only real if a production type - not a test double - satisfies the engine port the SPICE
        // selection needs. This is the defect AST-12 found: no type implemented IEphemerisEngine.
        var adapter = new ProviderEngineAdapter(new StubAstronomyEngine());
        Assert.IsAssignableFrom<IEphemerisEngine>(adapter);
    }

    [Fact]
    public void The_analytical_key_registers_the_existing_implementation()
    {
        using var provider = Build("analytical");
        Assert.IsType<MeeusEphemeris>(provider.GetRequiredService<IEphemeris>());
        Assert.Equal("analytical", provider.GetRequiredService<SelectedAstronomyProvider>().Key);
    }

    [Fact]
    public void The_spice_key_registers_the_adapter_and_records_the_selection()
    {
        using var provider = Build("spice", new StubEngine());
        Assert.IsType<AstronomyEngineEphemerisAdapter>(provider.GetRequiredService<IEphemeris>());
        Assert.Equal("spice", provider.GetRequiredService<SelectedAstronomyProvider>().Key);
    }

    [Fact]
    public void Provider_selection_is_case_insensitive_and_trimmed()
    {
        using var provider = Build("  SPICE  ", new StubEngine());
        Assert.Equal(ProviderSelection.Spice, provider.GetRequiredService<SelectedAstronomyProvider>().Selection);
    }

    [Fact]
    public void IEphemeris_delegates_every_method_to_the_engine_with_no_parallel_maths()
    {
        // A spy engine records exactly which engine call each port method made, so an implementation that computed
        // a value locally would fail even if the number happened to match.
        var spy = new SpyEngine(sunLongitude: 111.5, moonLongitude: 222.5, sunRa: 10.0, sunDec: 20.0,
            moonRa: 30.0, moonDec: 40.0, moonParallax: 0.95);
        var ephemeris = new AstronomyEngineEphemerisAdapter(spy);

        // 1. Each port method must reach the engine with the RIGHT body.
        Assert.Equal(111.5, ephemeris.GetSunLongitude(JulianDay), 12);
        Assert.Equal([(SpyEngine.Call.TropicalLongitude, CelestialBody.Sun)], spy.Observed);

        spy.Reset();
        Assert.Equal(222.5, ephemeris.GetMoonLongitude(JulianDay), 12);
        Assert.Equal([(SpyEngine.Call.TropicalLongitude, CelestialBody.Moon)], spy.Observed);

        spy.Reset();
        var sunEquatorial = ephemeris.GetSunEquatorial(JulianDay);
        Assert.Equal([(SpyEngine.Call.Equatorial, CelestialBody.Sun)], spy.Observed);
        Assert.Equal(10.0, sunEquatorial.RightAscension, 12);
        Assert.Equal(20.0, sunEquatorial.Declination, 12);

        spy.Reset();
        var moonEquatorial = ephemeris.GetMoonEquatorial(JulianDay);
        Assert.Equal([(SpyEngine.Call.Equatorial, CelestialBody.Moon)], spy.Observed);
        Assert.Equal(30.0, moonEquatorial.RightAscension, 12);
        Assert.Equal(40.0, moonEquatorial.Declination, 12);
        Assert.Equal(0.95, moonEquatorial.HorizontalParallax, 12);
    }

    [Fact]
    public void Replacing_the_engine_changes_every_port_result_proving_no_second_calculation_path()
    {
        // Two engines with different constants. EVERY port method must follow the engine. If any method had its own
        // maths, it would return the same value for both engines and this assertion would fail.
        var first = new AstronomyEngineEphemerisAdapter(new StubEngine(sunLongitude: 1.0, moonLongitude: 2.0,
            sunRa: 5.0, sunDec: 6.0, moonRa: 7.0, moonDec: 8.0, moonParallax: 0.9));
        var second = new AstronomyEngineEphemerisAdapter(new StubEngine(sunLongitude: 3.0, moonLongitude: 4.0,
            sunRa: 50.0, sunDec: 60.0, moonRa: 70.0, moonDec: 80.0, moonParallax: 0.95));

        Assert.NotEqual(first.GetSunLongitude(JulianDay), second.GetSunLongitude(JulianDay));
        Assert.NotEqual(first.GetMoonLongitude(JulianDay), second.GetMoonLongitude(JulianDay));
        Assert.NotEqual(first.GetSunEquatorial(JulianDay).RightAscension,
            second.GetSunEquatorial(JulianDay).RightAscension);
        Assert.NotEqual(first.GetSunEquatorial(JulianDay).Declination,
            second.GetSunEquatorial(JulianDay).Declination);
        Assert.NotEqual(first.GetMoonEquatorial(JulianDay).RightAscension,
            second.GetMoonEquatorial(JulianDay).RightAscension);
        Assert.NotEqual(first.GetMoonEquatorial(JulianDay).Declination,
            second.GetMoonEquatorial(JulianDay).Declination);
        Assert.NotEqual(first.GetMoonEquatorial(JulianDay).HorizontalParallax,
            second.GetMoonEquatorial(JulianDay).HorizontalParallax);
    }

    [Fact]
    public void Compare_mode_returns_the_primary_value_and_reports_the_delta()
    {
        var observer = new RecordingParityObserver();
        // The stub agrees with nothing, so every delta is a real, non-zero measurement.
        var comparing = new ComparingEphemeris(new StubEngine(sunLongitude: 100.0, moonLongitude: 200.0),
            [observer]);

        var returned = comparing.GetSunLongitude(JulianDay);

        Assert.Equal(100.0, returned, 12);
        Assert.NotEmpty(observer.Deltas);
        Assert.Contains(observer.Deltas, d => d.Body == CelestialBody.Sun);
        Assert.All(observer.Deltas, d => Assert.True(d.SeparationArcseconds >= 0));
    }

    [Fact]
    public void Compare_mode_with_an_engine_that_reproduces_the_analytical_value_reports_zero_delta()
    {
        // An engine echoing the analytical series' own output must produce an exact zero delta, which shows the
        // comparison is a real measurement of the two paths rather than a constant.
        var analytical = new MeeusEphemeris();
        var echo = new EchoEngine(analytical);
        var observer = new RecordingParityObserver();
        var comparing = new ComparingEphemeris(echo, [observer]);

        var sun = comparing.GetSunLongitude(JulianDay);

        Assert.Equal(analytical.GetSunLongitude(JulianDay), sun, 12);
        Assert.All(observer.Deltas, d => Assert.True(d.WithinTolerance));
        Assert.All(observer.Deltas, d => Assert.Equal(0.0, d.SeparationArcseconds, 9));
    }

    /// <summary>An engine that simply returns the analytical implementation's values, so the comparison against that
    /// same implementation must be exactly zero.</summary>
    private sealed class EchoEngine(IEphemeris reference) : IEphemerisEngine
    {
        public double GetTropicalLongitude(CelestialBody body, double julianDayUt) => body == CelestialBody.Sun
            ? reference.GetSunLongitude(julianDayUt)
            : reference.GetMoonLongitude(julianDayUt);

        public AstronomyEngineEquatorial GetEquatorial(CelestialBody body, double julianDayUt)
        {
            if (body == CelestialBody.Sun)
            {
                var sun = reference.GetSunEquatorial(julianDayUt);
                return new AstronomyEngineEquatorial(sun.RightAscension, sun.Declination);
            }
            var moon = reference.GetMoonEquatorial(julianDayUt);
            return new AstronomyEngineEquatorial(moon.RightAscension, moon.Declination, moon.HorizontalParallax);
        }
    }

    // ---- parity fixtures: populated, tolerance in data, covering the port's four methods ----

    [Fact]
    public void The_parity_fixture_file_exists_and_is_populated()
    {
        var fixtures = ParityFixtures.Load(FindSolutionRoot());

        Assert.NotEmpty(fixtures);
        Assert.All(fixtures, f => Assert.True(f.ToleranceArcseconds > 0,
            "Tolerance must come from the fixture data, not a code default."));
        Assert.All(fixtures, f => Assert.False(string.IsNullOrWhiteSpace(f.Source),
            "Every fixture must name its source so the value is attributable."));
    }

    [Fact]
    public void The_analytical_provider_matches_the_parity_fixtures_within_their_recorded_tolerance()
    {
        // The fixture values are independent references (Meeus worked examples), so this also pins the analytical
        // implementation itself: a regression in the current provider fails here, before any SPICE comparison.
        var ephemeris = new MeeusEphemeris();
        // Comparisons, not every row: an UnoffsetSeries row records the analytical series BEFORE the Drik offset, so
        // comparing it against GetMoonLongitude would fail for the documented reason that the offset exists (api#130).
        var fixtures = ParityFixtures.Comparisons(ParityFixtures.Load(FindSolutionRoot()));

        foreach (var fixture in fixtures)
        {
            var actual = fixture.Body switch
            {
                CelestialBody.Sun => ephemeris.GetSunLongitude(fixture.JulianDayUt),
                CelestialBody.Moon => ephemeris.GetMoonLongitude(fixture.JulianDayUt),
                _ => throw new InvalidOperationException($"Fixture body {fixture.Body} is not covered by this test.")
            };

            var separation = ParityGate.SeparationArcseconds(fixture.TropicalLongitudeDegrees, actual);
            Assert.True(separation <= fixture.ToleranceArcseconds,
                $"{fixture.Body} at JD {fixture.JulianDayUt} differs by {separation:0.###} arcsec, tolerance "
                + $"{fixture.ToleranceArcseconds}.");
        }
    }

    [Fact]
    public void The_parity_gate_passes_for_a_matching_engine_and_fails_for_a_diverging_one()
    {
        var fixtures = ParityFixtures.Load(FindSolutionRoot());
        foreach (var fixture in fixtures)
        {
            var agreeing = ParityGate.Evaluate(fixture, fixture.TropicalLongitudeDegrees);
            Assert.True(agreeing.Passed);

            // One arcminute of divergence must fail a one-arcsecond budget.
            var diverging = ParityGate.Evaluate(fixture, fixture.TropicalLongitudeDegrees + (1.0 / 60.0));
            Assert.False(diverging.Passed);
        }
    }

    [Fact]
    public void AddAstronomy_alone_binds_the_port_through_the_selection_not_a_hardcoded_provider()
    {
        // Regression for the defect found during AST-05: AddAstronomy registered MeeusEphemeris directly while
        // AddAstronomyProvider registered the configured provider. Both used TryAddSingleton, so which one won
        // depended on call order and the configured key could be silently ignored.
        var services = new ServiceCollection();
        services.AddAstronomy();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        // Exactly one registration of the port: two would mean two authors of the same decision.
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEphemeris));
        Assert.IsType<MeeusEphemeris>(provider.GetRequiredService<IEphemeris>());

        // The selection is recorded even on the default path, so provenance cannot be absent.
        Assert.Equal(ProviderSelection.Analytical, provider.GetRequiredService<SelectedAstronomyProvider>().Selection);
    }

    [Fact]
    public void A_configured_spice_selection_survives_AddAstronomy_being_called_afterwards()
    {
        // Order independence. If AddAstronomy still registered the analytical implementation directly, calling it
        // after the provider registration would either win (wrong provider silently) or add a second descriptor.
        var engine = new StubEngine(sunLongitude: 42.0);
        var services = new ServiceCollection();
        services.AddAstronomyProvider(ProviderSelectionParser.SpiceKey, _ => engine);
        services.AddAstronomy();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEphemeris));
        Assert.IsType<AstronomyEngineEphemerisAdapter>(provider.GetRequiredService<IEphemeris>());
        Assert.Equal(ProviderSelection.Spice, provider.GetRequiredService<SelectedAstronomyProvider>().Selection);
    }

    [Fact]
    public void AddAstronomy_with_an_explicit_selection_binds_that_selection()
    {
        var engine = new StubEngine(sunLongitude: 7.0);
        var services = new ServiceCollection();
        services.AddAstronomy(ProviderSelection.Spice, _ => engine);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.IsType<AstronomyEngineEphemerisAdapter>(provider.GetRequiredService<IEphemeris>());
    }

    [Fact]
    public void A_second_conflicting_provider_registration_cannot_downgrade_the_first()
    {
        // TryAdd means the FIRST registration wins. So calling the provider registration twice with different keys
        // is not order-neutral: the earlier key decides. What matters is that this cannot happen silently for the
        // real composition, which calls it once - and that a later cheap call cannot quietly replace an expensive
        // or explicitly requested provider with the analytical default.
        var engine = new StubEngine(sunLongitude: 5.0);
        var services = new ServiceCollection();
        services.AddAstronomyProvider(ProviderSelectionParser.SpiceKey, _ => engine);
        services.AddAstronomy(ProviderSelection.Analytical);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        // First registration wins, so the requested spice provider survives a later analytical default.
        Assert.IsType<AstronomyEngineEphemerisAdapter>(provider.GetRequiredService<IEphemeris>());
        Assert.Single(services, d => d.ServiceType == typeof(IEphemeris));
    }

    [Fact]
    public void Registration_order_is_harmless_when_the_selection_agrees()
    {
        // The realistic composition path: one provider registration plus a later AddAstronomy() that names the same
        // selection. No matter the order, one descriptor and the same resolution.
        var services = new ServiceCollection();
        services.AddAstronomy();
        services.AddAstronomyProvider(ProviderSelectionParser.AnalyticalKey);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.Single(services, d => d.ServiceType == typeof(IEphemeris));
        Assert.IsType<MeeusEphemeris>(provider.GetRequiredService<IEphemeris>());
    }

    [Fact]
    public void An_absent_key_resolves_to_the_documented_default_at_composition()
    {
        // Absence is a normal state: the Day and Star hosts load their own layered configuration and neither ships an
        // Astronomy section. Composition must therefore default it explicitly in one named place, NOT fail. An
        // UNKNOWN value is different and still throws - see the test below.
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddAstronomyProvider(
            configuration[AstronomyProviderRegistration.ConfigurationKey] ?? ProviderSelectionParser.AnalyticalKey);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.Equal(ProviderSelection.Analytical, provider.GetRequiredService<SelectedAstronomyProvider>().Selection);
    }

    [Fact]
    public void A_present_but_unknown_key_still_fails_at_composition()
    {
        // The default applies to absence only. A typo must never degrade to analytical.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AstronomyProviderRegistration.ConfigurationKey] = "spice-ish"
            })
            .Build();

        var services = new ServiceCollection();
        var failure = Assert.Throws<ArgumentException>(() => services.AddAstronomyProvider(
            configuration[AstronomyProviderRegistration.ConfigurationKey] ?? ProviderSelectionParser.AnalyticalKey));
        Assert.Contains("Unknown astronomy provider key", failure.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(string key, IEphemerisEngine? engine = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IParityObserver>(new RecordingParityObserver());
        services.AddAstronomyProvider(key, engine is null ? null : _ => engine);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    /// <summary>
    /// Locates the repository root by looking for the astronomy fixture file itself, walking up from the test binary.
    /// Looking for the astronomy solution file does not work here: this test runs from the Day test project, whose
    /// binary directory is not inside the astronomy solution.
    /// </summary>
    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "astronomy", ParityFixtures.FileName))
                || File.Exists(Path.Combine(directory.FullName, ParityFixtures.FileName)))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            $"Parity fixture file '{ParityFixtures.FileName}' not found in any ancestor of {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// A spy that records exactly which engine operation each call made, so a delegation test can assert the adapter
    /// forwarded to the RIGHT engine call rather than merely producing a plausible value.
    /// </summary>
    private sealed class SpyEngine(
        double sunLongitude = 10.0,
        double moonLongitude = 20.0,
        double sunRa = 1.0,
        double sunDec = 2.0,
        double moonRa = 3.0,
        double moonDec = 4.0,
        double moonParallax = 0.9) : IEphemerisEngine
    {
        public enum Call { TropicalLongitude, Equatorial }

        private readonly List<(Call Operation, CelestialBody Body)> _observed = [];
        public IReadOnlyList<(Call Operation, CelestialBody Body)> Observed => _observed;
        public void Reset() => _observed.Clear();

        public double GetTropicalLongitude(CelestialBody body, double julianDayUt)
        {
            _observed.Add((Call.TropicalLongitude, body));
            return body == CelestialBody.Sun ? sunLongitude : moonLongitude;
        }

        public AstronomyEngineEquatorial GetEquatorial(CelestialBody body, double julianDayUt)
        {
            _observed.Add((Call.Equatorial, body));
            return body == CelestialBody.Sun
                ? new AstronomyEngineEquatorial(sunRa, sunDec)
                : new AstronomyEngineEquatorial(moonRa, moonDec, moonParallax);
        }
    }


    private sealed class StubAstronomyEngine : IAstronomyEngine
    {
        public AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings) =>
            GetPositions(time, settings, AstronomyCapabilities.SupportedBodies);

        public AstronomyResult GetPositions(JulianDayUt time, AstronomySettings settings,
            IReadOnlyCollection<CelestialBody> bodies) => new(time, settings,
            bodies.Select(body => new BodyPosition(body,
                new EclipticPoint(bodies.ToList().IndexOf(body) * 10.0 + 90.0, 0.0, null),
                new EquatorialPoint(0.0, 0.0), 0.0, false)).ToArray(),
            new AstronomyProvenance("stub", "stub", "stub", 0.0, NodeConvention.Mean, AyanamsaKind.Lahiri,
                ZodiacConvention.Tropical));

        public AstronomyCapabilityReport GetCapabilities() => AstronomyCapabilities.Report();
    }

    private sealed class StubEngine(
        double sunLongitude = 10.0,
        double moonLongitude = 20.0,
        double sunRa = 1.0,
        double sunDec = 2.0,
        double moonRa = 3.0,
        double moonDec = 4.0,
        double moonParallax = 0.9) : IEphemerisEngine
    {
        public int CallCount { get; private set; }

        public double GetTropicalLongitude(CelestialBody body, double julianDayUt)
        {
            CallCount++;
            return body == CelestialBody.Sun ? sunLongitude : moonLongitude;
        }

        public AstronomyEngineEquatorial GetEquatorial(CelestialBody body, double julianDayUt)
        {
            CallCount++;
            return body == CelestialBody.Sun
                ? new AstronomyEngineEquatorial(sunRa, sunDec)
                : new AstronomyEngineEquatorial(moonRa, moonDec, moonParallax);
        }
    }
}
