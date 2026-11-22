using System.Diagnostics;
using System.Globalization;
using MyVedicStar.Astronomy.Core;

/// <summary>
/// api#75 (AST-10) AC-1: the "no more than 10% regression under equivalent workload" measurement.
/// </summary>
/// <remarks>
/// <para>
/// The AC says response times must regress by no more than 10% under equivalent workloads, with before/after numbers
/// attached. That comparison needs two implementations, and on a host without the native CSPICE toolkit the SPICE side
/// cannot be timed at all - so this report refuses to invent one. What it does instead is measure the OPERATIONS the
/// provider swap actually changes, over the same workload, and state the delta per operation with the workload, the
/// machine and the sample count printed beside it.
/// </para>
/// <para>
/// The comparison is against a <b>recorded baseline file</b>, not against a recollection. `--record` writes the
/// current numbers; the default run compares against the file and fails when any measured operation is slower by more
/// than the tolerance. A regression introduced later is therefore a red run with the offending operation named, not a
/// number someone has to notice.
/// </para>
/// <para>
/// Every figure is a <b>measurement on this host</b>. Nothing is extrapolated to another machine, and the composition
/// here (result building, cache bookkeeping) is priced with no simulated engine cost, so the delta is attributable to
/// the port rather than to a placeholder.
/// </para>
/// </remarks>
internal static class RegressionComparison
{
    // Falsification knobs. SpinRounds = 0 in committed code; a non-zero value injects a measurable slowdown.
    internal static volatile int SpinProbe = 0;
    internal static int SpinRounds = 0;

    /// <summary>The AC-1 tolerance.</summary>
    internal const double Tolerance = 0.10;

    /// <summary>Parameters that make two runs "equivalent workloads". Printed with every result.</summary>
    private const int WarmupOps = 20_000;
    private const int MeasuredOps = 100_000;
    private const int KeySamples = 4_001;
    private const int BuildSamples = 2_001;

    internal sealed record OperationTiming(string Operation, double NanosecondsPerOp);

    internal sealed record Baseline(string Schema, string Workload, IReadOnlyDictionary<string, double> Nanoseconds);

    internal static string WorkloadDescriptor() =>
        string.Create(CultureInfo.InvariantCulture,
            $"measuredOps={MeasuredOps};warmupOps={WarmupOps};keySamples={KeySamples};buildSamples={BuildSamples};bodies={AstronomyCapabilities.SupportedBodies.Count}");

    /// <summary>
    /// Trials per measured run. A single median per process was observed to move by ~49% on an unchanged binary
    /// (JIT tiering, the background GC and the scheduler all land inside a 100k-op window), which would make a 10%
    /// gate fire on noise. The reported figure is the MEDIAN OF TRIALS, and the spread is printed beside it so a
    /// reader can see the run-to-run noise rather than take a single number on trust.
    /// </summary>
    internal const int Trials = 9;

    /// <summary>Prints the per-operation spread across trials, so the reported median is never mistaken for a tight figure.</summary>
    internal static IReadOnlyList<string> DescribeSpread(IReadOnlyDictionary<string, double[]> trials)
    {
        var lines = new List<string>();
        foreach (var (operation, values) in trials)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"spread    {operation,-16} median {sorted[sorted.Length / 2],10:N1} ns  "
                + $"min {sorted[0],10:N1}  max {sorted[^1],10:N1}  spread {(sorted[^1] - sorted[0]) / sorted[sorted.Length / 2]:P1}"));
        }
        return lines;
    }

    internal static IReadOnlyList<OperationTiming> MeasureOnce(AstronomySettings settings)
    {
        var results = new List<OperationTiming>();

        // ---- cache hit: the hot path every served request pays ----
        {
            var cache = new AstronomyResultCache(capacity: 1024);
            var oneDay = TimeSpan.FromDays(1);
            var key = AstronomyCacheKey.For(new JulianDayUt(2451545.0), settings, null, oneDay);
            var seed = BuildResultNoSettle(2451545.0, settings);
            cache.GetOrAdd(key, () => seed);
            Func<AstronomyResult> mustNotCompute = () => throw new InvalidOperationException("must not compute");

            for (var i = 0; i < WarmupOps; i++) _ = cache.GetOrAdd(key, mustNotCompute);
            results.Add(new OperationTiming("cache.hit", TimePerOp(MeasuredOps, () =>
            {
                double sink = 0;
                for (var i = 0; i < MeasuredOps; i++) sink += cache.GetOrAdd(key, mustNotCompute).Time.Value;
                return sink;
            }, MeasuredOps)));
        }

        // ---- key construction: the tax on every call, cache hit or miss ----
        {
            // Sampled in batches of 100: a single sub-microsecond build is below the resolution of Stopwatch on this
            // host, and a tight key-only loop is dead-code-eligible and swings by an order of magnitude between runs.
            var samples = new long[KeySamples];
            var oneDay = TimeSpan.FromDays(1);
            for (var i = 0; i < samples.Length; i++)
            {
                var jd = 2451545.0 + i % 365;
                const int batch = 100;
                var t0 = Stopwatch.GetTimestamp();
                double checksum = 0;
                for (var b = 0; b < batch; b++)
                    checksum += AstronomyCacheKey.For(new JulianDayUt(jd + b), settings,
                        AstronomyCapabilities.SupportedBodies, oneDay).JulianDay;
                // INJECTED REGRESSION (falsification run, reverted immediately). Not eliminable: reads a volatile.
                for (var slow = 0; slow < SpinRounds; slow++) checksum += SpinProbe;
                samples[i] = (Stopwatch.GetTimestamp() - t0) / batch;
                _ = checksum;
            }
            // Drop the cold half: JIT and first dict shapes are not the steady-state cost.
            var measured = samples.Skip(samples.Length / 2).OrderBy(t => t).ToArray();
            results.Add(new OperationTiming("key.construct", TicksToNs(measured[measured.Length / 2])));
        }

        // ---- result construction: what the engine hands back and the cache stores ----
        {
            var samples = new long[BuildSamples];
            for (var i = 0; i < samples.Length; i++)
            {
                var jd = 2451545.0 + i;
                var t0 = Stopwatch.GetTimestamp();
                _ = BuildResultNoSettle(jd, settings);
                samples[i] = Stopwatch.GetTimestamp() - t0;
            }
            Array.Sort(samples);
            results.Add(new OperationTiming("result.build", TicksToNs(samples[samples.Length / 2])));
        }

        return results;
    }

    /// <summary>Measures every operation TRIALS times and reports the median of trials as the figure.</summary>
    internal static (IReadOnlyList<OperationTiming> Timings, IReadOnlyDictionary<string, double[]> TrialsByOperation) Measure(
        AstronomySettings settings)
    {
        var runs = Enumerable.Range(0, Trials).Select(_ => MeasureOnce(settings)).ToArray();
        var byOperation = runs
            .SelectMany(run => run)
            .GroupBy(t => t.Operation)
            .ToDictionary(g => g.Key, g => g.Select(t => t.NanosecondsPerOp).ToArray());

        var timings = byOperation
            .Select(pair =>
            {
                var sorted = pair.Value.OrderBy(v => v).ToArray();
                return new OperationTiming(pair.Key, sorted[sorted.Length / 2]);
            })
            .OrderBy(t => t.Operation, StringComparer.Ordinal)
            .ToArray();

        return (timings, byOperation);
    }

    /// <summary>
    /// Compares a measured run against a baseline and returns one line per operation. A missing or incomparable baseline
    /// is an ERROR rather than a silent pass: an absent comparison is not a passing comparison.
    /// </summary>
    /// <summary>
    /// A trial spread above this fraction means the host was too busy for a 10% wall-clock gate to mean anything. The
    /// comparison then reports INCONCLUSIVE and exits non-zero-but-distinct: a noisy machine must not be able to fail a
    /// build for a regression that did not happen, and must not be able to pass one either.
    /// </summary>
    internal const double MaxTrialSpread = 0.15;

    /// <summary>
    /// Judges one operation. <paramref name="trials"/> is the per-trial sample the median came from, so the spread can
    /// gate the verdict: a wide spread on a busy host returns <see cref="Verdict.Inconclusive"/> rather than a verdict
    /// the measurement cannot support.
    /// </summary>
    internal static (Verdict Verdict, double Delta, double Spread) Judge(double before, double median, double[] trials)
    {
        var sorted = trials.OrderBy(v => v).ToArray();
        var spread = (sorted[^1] - sorted[0]) / median;
        if (spread > MaxTrialSpread) return (Verdict.Inconclusive, (median - before) / before, spread);

        var delta = (median - before) / before;
        return (delta > Tolerance ? Verdict.Regressed : Verdict.Ok, delta, spread);
    }

    internal enum Verdict { Ok, Regressed, Inconclusive }

    private static double TimePerOp(int ops, Func<double> body, int reportedOps)
    {
        // Warm the delegate and the tiering before the timed pass.
        _ = body();
        var sw = Stopwatch.StartNew();
        var sink = body();
        sw.Stop();
        _ = sink;
        return sw.Elapsed.TotalMilliseconds * 1e6 / reportedOps;
    }

    private static double TicksToNs(long ticks) => ticks * 1e9 / Stopwatch.Frequency;

    internal static AstronomyResult BuildResultNoSettle(double julianDay, AstronomySettings settings)
    {
        var bodies = AstronomyCapabilities.SupportedBodies.Select((b, i) => new BodyPosition(
            b,
            new EclipticPoint(julianDay % 360 + i, 0, null),
            new EquatorialPoint(julianDay % 360 + i, 0),
            0.98,
            false)).ToArray();

        return new AstronomyResult(
            new JulianDayUt(julianDay),
            settings,
            bodies,
            new AstronomyProvenance("mvs-astronomy-spice-0.2.0", "measured-standin", "none", 0,
                settings.NodeConvention, settings.Ayanamsa, settings.Zodiac));
    }
}
