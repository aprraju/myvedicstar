using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// AST-10 (api#75): the concurrency claim, as a computational fact rather than a reflection census.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequestScopedTelemetryTests.Astronomy_assemblies_have_no_mutable_static_state_to_cross_a_request_boundary"/>
/// proves there is no mutable static field that <i>could</i> carry input across a boundary. That is a census of the
/// source, and it would still pass if a shared <b>instance</b> held per-request state, or if an implementation were
/// merely thread-unsafe in its locals-to-fields handling. This test exercises the other half: many threads calling the
/// same singleton provider concurrently and each getting its own answer.
/// </para>
/// <para>
/// The strong form is used deliberately. It is not enough that parallel results are individually plausible - a
/// cross-contaminating implementation returns plausible numbers too, just someone else's. Each worker's expected value
/// is computed <b>serially, before any thread starts</b>, and every parallel result must match its own expectation
/// bit-for-bit. A contamination of any magnitude, on any thread, fails.
/// </para>
/// </remarks>
public sealed class ParallelCalculationIsolationTests
{
    /// <summary>Enough workers to exceed any plausible core count, so the scheduler genuinely interleaves.</summary>
    private const int Workers = 32;

    /// <summary>Distinct instants spread across a century, so no two workers share a Julian Day.</summary>
    private static readonly double[] Instants =
        Enumerable.Range(0, Workers).Select(i => 2451545.0 + i * 913.25).ToArray();

    private static IEphemeris Provider()
    {
        var services = new ServiceCollection();
        services.AddAstronomyProvider(ProviderSelectionParser.AnalyticalKey);
        return services.BuildServiceProvider().GetRequiredService<IEphemeris>();
    }

    /// <summary>The four port calls, named so a failure says which one crossed.</summary>
    /// <summary>
    /// Bit-exact rendering of the four port outputs. `R` round-trips a double exactly, so two calls that differ in the
    /// last bit produce different strings - an equality check on a formatted number would hide precisely the
    /// contamination this test exists to catch.
    /// </summary>
    private static string Measure(IEphemeris ephemeris, double julianDay)
    {
        var sun = ephemeris.GetSunEquatorial(julianDay);
        var moon = ephemeris.GetMoonEquatorial(julianDay);
        return string.Join("|",
            ephemeris.GetSunLongitude(julianDay).ToString("R", CultureInfo.InvariantCulture),
            ephemeris.GetMoonLongitude(julianDay).ToString("R", CultureInfo.InvariantCulture),
            sun.RightAscension.ToString("R", CultureInfo.InvariantCulture),
            sun.Declination.ToString("R", CultureInfo.InvariantCulture),
            moon.RightAscension.ToString("R", CultureInfo.InvariantCulture),
            moon.Declination.ToString("R", CultureInfo.InvariantCulture),
            moon.HorizontalParallax.ToString("R", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Parallel_calculations_against_one_provider_never_cross_contaminate()
    {
        var ephemeris = Provider();

        // Serial baseline first: these are the only values any thread is allowed to produce.
        var expected = Instants.Select(instant => Measure(ephemeris, instant)).ToArray();

        // Every failure is collected rather than thrown, so one contaminating thread cannot hide the others behind an
        // aggregate's first exception: a partial defect is exactly what this test has to survive reporting.
        var failures = new ConcurrentBag<string>();

        // A start gate, so the threads are genuinely simultaneous instead of queueing behind the first spawn.
        using var gate = new Barrier(Workers);

        var workers = Enumerable.Range(0, Workers).Select(index => Task.Run(() =>
        {
            gate.SignalAndWait();
            for (var repetition = 0; repetition < 8; repetition++)
            {
                var actual = Measure(ephemeris, Instants[index]);
                if (actual != expected[index])
                    failures.Add($"worker {index} repetition {repetition} (JD {Instants[index]:R}) returned {actual}, "
                        + $"serial expectation {expected[index]}");
            }
        })).ToArray();

        await Task.WhenAll(workers);

        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task The_same_instant_asked_by_many_threads_yields_one_answer()
    {
        // The complement of the test above: there, workers differ and must not see each other. Here every worker asks the
        // same question, and any disagreement means some thread's local state leaked into the number.
        var ephemeris = Provider();
        const double Instant = 2448908.5;
        var expected = Measure(ephemeris, Instant);

        var results = new ConcurrentBag<string>();
        using var gate = new Barrier(Workers);

        await Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => Task.Run(() =>
        {
            gate.SignalAndWait();
            for (var repetition = 0; repetition < 8; repetition++) results.Add(Measure(ephemeris, Instant));
        })));

        var distinct = results.Distinct(StringComparer.Ordinal).ToArray();
        Assert.True(distinct.Length == 1 && distinct[0] == expected,
            $"Concurrent callers of one instant produced {distinct.Length} distinct answers; expected exactly one: {expected}");
    }

    [Fact]
    public void The_provider_is_one_shared_instance_so_the_tests_above_are_about_sharing()
    {
        // Nails the premise the two tests above rely on. If DI handed each thread its own provider, they would pass
        // trivially and prove nothing about a shared singleton.
        var services = new ServiceCollection();
        services.AddAstronomyProvider(ProviderSelectionParser.AnalyticalKey);
        using var provider = services.BuildServiceProvider();

        var resolved = Enumerable.Range(0, Workers)
            .Select(_ => provider.GetRequiredService<IEphemeris>())
            .ToArray();

        Assert.All(resolved, instance => Assert.Same(resolved[0], instance));
    }
}
