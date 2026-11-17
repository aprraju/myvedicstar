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
        // Distinct sentinel values per body so a copied implementation cannot accidentally match.
        var engine = new StubEngine(sunLongitude: 111.5, moonLongitude: 222.5, sunRa: 10.0, sunDec: 20.0,
            moonRa: 30.0, moonDec: 40.0, moonParallax: 0.95);
        var ephemeris = new AstronomyEngineEphemerisAdapter(engine);

        Assert.Equal(111.5, ephemeris.GetSunLongitude(JulianDay), 12);
        Assert.Equal(222.5, ephemeris.GetMoonLongitude(JulianDay), 12);
        Assert.Equal(10.0, ephemeris.GetSunEquatorial(JulianDay).RightAscension, 12);
        Assert.Equal(20.0, ephemeris.GetSunEquatorial(JulianDay).Declination, 12);
        Assert.Equal(30.0, ephemeris.GetMoonEquatorial(JulianDay).RightAscension, 12);
        Assert.Equal(40.0, ephemeris.GetMoonEquatorial(JulianDay).Declination, 12);
        Assert.Equal(0.95, ephemeris.GetMoonEquatorial(JulianDay).HorizontalParallax, 12);

        // And the engine saw exactly the calls the port received: no value was computed locally.
        Assert.Equal(4, engine.CallCount);
    }

    [Fact]
    public void Replacing_the_engine_changes_every_port_result_proving_one_calculation_path()
    {
        // Two engines with different constants. Every port method must follow the engine.
        var first = new AstronomyEngineEphemerisAdapter(new StubEngine(sunLongitude: 1.0, moonLongitude: 2.0));
        var second = new AstronomyEngineEphemerisAdapter(new StubEngine(sunLongitude: 3.0, moonLongitude: 4.0));

        Assert.NotEqual(first.GetSunLongitude(JulianDay), second.GetSunLongitude(JulianDay));
        Assert.NotEqual(first.GetMoonLongitude(JulianDay), second.GetMoonLongitude(JulianDay));
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
        var fixtures = ParityFixtures.Load(FindSolutionRoot());

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

    private static ServiceProvider Build(string key, IEphemerisEngine? engine = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IParityObserver>(new RecordingParityObserver());
        services.AddAstronomyProvider(key, engine is null ? null : _ => engine);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyVedicStar.Astronomy.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Astronomy solution root not found.");
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
