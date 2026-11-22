using System.Diagnostics;
using System.Runtime;
using MyVedicStar.Astronomy.Core;

// api#72 (AST-07) measurement harness.
//
// It measures the CACHE, not the ephemeris: the engine call is supplied as a fake with a known, timed cost, so the
// overhead the cache adds or removes is attributable. Every number printed is measured on this host with these
// settings and is reprinted with them (machine name, processor count, runtime, iteration counts) so a reader can
// judge whether it transfers. Nothing here is extrapolated.

var settings = new AstronomySettings();
var oneDay = TimeSpan.FromDays(1);
const int lookups = 200_000;
const int sweeps = 20;

// Declared cost of one simulated engine call. 5 ms is an order-of-magnitude placeholder for "a native call per
// body"; the report labels it as a SETTING, not a measurement of SPICE.
var settleMs = double.Parse(Environment.GetEnvironmentVariable("MVS_SETTLE_MS") ?? "0.5", System.Globalization.CultureInfo.InvariantCulture);
var settleTicks = (long)(settleMs / 1000.0 * Stopwatch.Frequency);
Console.WriteLine($"engine call (simulated) : {settleMs} ms SETTING, not a measurement");
Console.WriteLine();

// Falsification knob for the AC-1 gate: a non-zero value injects a measurable slowdown into key construction.
RegressionComparison.SpinRounds = int.Parse(Environment.GetEnvironmentVariable("MVS_SPIN_ROUNDS") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
RegressionComparison.SpinProbe = int.Parse(Environment.GetEnvironmentVariable("MVS_SPIN_PROBE") ?? "1", System.Globalization.CultureInfo.InvariantCulture);

Console.WriteLine("=== api#72 AST-07 cache measurement ===");
Console.WriteLine($"machine           : {Environment.MachineName}");
Console.WriteLine($"os                : {Environment.OSVersion}");
Console.WriteLine($"runtime           : {Environment.Version}");
Console.WriteLine($"processors        : {Environment.ProcessorCount}");
Console.WriteLine($"gc                : server={GCSettings.IsServerGC} latency={GCSettings.LatencyMode}");
var tiered = AppContext.TryGetSwitch("System.Runtime.TieredCompilation", out var tc) ? tc.ToString() : "?";
var tieredPgo = AppContext.TryGetSwitch("System.Runtime.TieredPGO", out var tp) ? tp.ToString() : "?";
Console.WriteLine($"tiering           : TieredCompilation={tiered} TieredPGO={tieredPgo}");
Console.WriteLine($"lookups           : {lookups}");
Console.WriteLine($"sweeps            : {sweeps} (distinct days per sweep)");
Console.WriteLine();

// ---------- 1. hit cost: what a cache hit costs on the hot path ----------
// The key is built ONCE here, on purpose: it prices the cache, not the key. Key construction is priced in step 2,
// because a hot path builds its key on every call and the report must not let one number hide the other.
{
    var cache = new AstronomyResultCache(capacity: 1024);
    var key = AstronomyCacheKey.For(new JulianDayUt(2451545.0), settings, null, oneDay);
    var seed = BuildResult(2451545.0, settings);
    cache.GetOrAdd(key, () => seed);

    Func<AstronomyResult> mustNotCompute = () => throw new InvalidOperationException("must not compute");

    // warm-up: JIT and the first dictionary shape are not what the report is about
    for (var i = 0; i < 100_000; i++) _ = cache.GetOrAdd(key, mustNotCompute);
    cache.ResetStatistics();

    var sw = Stopwatch.StartNew();
    var sink = 0.0;
    for (var i = 0; i < lookups; i++)
        sink += cache.GetOrAdd(key, mustNotCompute).Time.Value;
    sw.Stop();

    Console.WriteLine($"[hit] GetOrAdd hit         : {sw.Elapsed.TotalMilliseconds / lookups * 1e6,10:N1} ns/op ({lookups} ops, sink {sink:R})");
    var stats = cache.Statistics();
    Console.WriteLine($"[hit] counters             : hits={stats.Hits} misses={stats.Misses} hitRate={stats.HitRate:P2}");
}

// ---------- 2. key construction cost: the tax every call pays ----------
// Sampled rather than timed in a tight loop: a loop that only builds keys is dead-code-eligible and its apparent
// cost swings by an order of magnitude between runs, which is exactly the plausible-but-wrong figure a performance
// report must not publish. The accumulator here is a real dependency on every iteration's result.
{
    var samples = new long[20_001];
    for (var i = 0; i < samples.Length; i++)
    {
        var jd = 2451545.0 + i % 365;

        // Batch of 100 builds per sample: Stopwatch.GetTimestamp() is ~20-30 ns on this host and would otherwise
        // dominate a single sub-microsecond build, which is how a ~1 us operation gets reported as ~12 us.
        const int batch = 100;
        var t0 = Stopwatch.GetTimestamp();
        double checksum = 0;
        for (var b = 0; b < batch; b++)
            checksum += AstronomyCacheKey.For(new JulianDayUt(jd + b), settings, AstronomyCapabilities.SupportedBodies, oneDay).JulianDay;
        samples[i] = (Stopwatch.GetTimestamp() - t0) / batch;
        _ = checksum;
    }

    // drop the warm-up half of the samples
    var measured2 = samples.Skip(samples.Length / 2).OrderBy(x => x).ToArray();
    var medianTicks = measured2[measured2.Length / 2];
    var p95Ticks = measured2[(int)(measured2.Length * 0.95)];
    var perTickNs = 1e9 / Stopwatch.Frequency;
    Console.WriteLine($"[key] key construction     : {(medianTicks * perTickNs),10:N0} ns/op median, p95 {p95Ticks * perTickNs:N0} ns ({measured2.Length} samples)");
}

// ---------- 3. the cost of producing one result, and the cache's share of it ----------
// Engine time cannot be measured here (no CSPICE toolkit, no kernel), so it is DECLARED. What this section measures on
// this host is the part that IS the cache's business: building the result object and running the cache bookkeeping.
{
    const int samples = 2_001;

    // (a) result construction only: the object the cache stores
    var build = new long[samples / 2 + 1];
    for (var i = 0; i < build.Length; i++)
    {
        var t0 = Stopwatch.GetTimestamp();
        _ = BuildResultNoSettle(2451545.0 + i, settings);
        build[i] = Stopwatch.GetTimestamp() - t0;
    }
    Array.Sort(build);
    Console.WriteLine($"[result] build one result   : {TicksToUs(build[build.Length / 2]),8:N2} us median, p95 {TicksToUs(build[(int)(build.Length * 0.95)]):N2} us");

    // (b) cache overhead at a miss: key present-check + store, with construction counted once
    var cache = new AstronomyResultCache(capacity: 1024);
    var missSw = Stopwatch.StartNew();
    for (var i = 0; i < 1_000; i++)
    {
        var jd = 2451545.0 + i * 0.5;   // distinct days: every call misses
        var key = AstronomyCacheKey.For(new JulianDayUt(jd), settings, null, oneDay);
        _ = cache.GetOrAdd(key, () => BuildResultNoSettle(jd, settings));
    }
    missSw.Stop();
    Console.WriteLine($"[miss] miss incl. build+key : {missSw.Elapsed.TotalMilliseconds / 1_000 * 1000,8:N2} us/op (1000 distinct days)");
}

// ---------- 4. declared engine time and what the cache removes ----------
{
    // The declared engine cost is the ONLY part of a hot-path call this host cannot measure, so it is an input, and
    // the arithmetic below is shown rather than asserted.
    Console.WriteLine($"[model] declared engine     : {settleMs * 1000,8:N0} us/call (SETTING)");
    Console.WriteLine("[model] uncached hot call    : declared engine + key + build  (per call)");
    Console.WriteLine("[model] cached hot call      : key lookup only                (measured above)");
}

// ---------- 5. eviction under a working set larger than capacity ----------
{
    // No engine settle here: this section prices the cache's own bookkeeping under churn, so the per-op figure is
    // attributable to the cache rather than to a declared engine cost added on top.
    var cache = new AstronomyResultCache(capacity: 64);
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < 500; i++)
    {
        var jd = 2451545.0 + i % 512;   // 8x the capacity: every pass evicts and re-adds
        _ = cache.GetOrAdd(AstronomyCacheKey.For(new JulianDayUt(jd), settings, null, oneDay),
            () => BuildResultNoSettle(jd, settings));
    }
    sw.Stop();
    var stats = cache.Statistics();
    Console.WriteLine($"[churn] capacity=64, working set=512 over 500 ops");
    Console.WriteLine($"[churn] per-op             : {sw.Elapsed.TotalMilliseconds / 500 * 1000,10:N2} us/op");
    Console.WriteLine($"[churn] hits={stats.Hits} misses={stats.Misses} evictions={stats.Evictions} count={stats.Count} hitRate={stats.HitRate:P2}");
}

// ---------- 6. invalidation cost ----------
{
    var cache = new AstronomyResultCache(capacity: 1024);
    for (var i = 0; i < 1024; i++)
    {
        var jd = 2451545.0 + i;
        cache.GetOrAdd(AstronomyCacheKey.For(new JulianDayUt(jd), settings, null, oneDay), () => BuildResult(jd, settings));
    }

    var sw = Stopwatch.StartNew();
    var removed = cache.InvalidateBefore(2451545.0 + 512);
    sw.Stop();
    Console.WriteLine($"[invalidate] InvalidateBefore, 1024 entries : {sw.Elapsed.TotalMilliseconds * 1000,10:N1} us, removed={removed}");
    Console.WriteLine($"[invalidate] version after                 : {cache.Version}");
}

// ---------- 7. AC-1 (api#75): regression against a recorded baseline, not a recollection ----------
// `dotnet run -- --record` writes the baseline file; a plain run compares against it and EXITS NONZERO when any
// measured operation is slower by more than 10%. The exit code is the point: a regression is a red run with the
// operation named, not a figure someone has to notice in a wall of output.
{
    // Checked in beside the harness, NOT under bin/: a baseline inside the build output is gitignored, so every CI run
    // would compare against a file that does not exist (or worse, one baked at build time). Walking up from the build
    // directory keeps the path correct for both `dotnet run` and a published binary.
    var baselinePath = BaselinePath();
    var record = args.Contains("--record", StringComparer.Ordinal);

    Console.WriteLine();
    Console.WriteLine("=== api#75 AST-10 AC-1 regression ===");
    Console.WriteLine($"workload          : {RegressionComparison.WorkloadDescriptor()}");
    Console.WriteLine($"host              : {Environment.MachineName} / {Environment.ProcessorCount} processors / runtime {Environment.Version}");
    Console.WriteLine($"baseline          : {baselinePath}");

    var (measured, trials) = RegressionComparison.Measure(settings);

    // The spread is printed before any verdict: a 10% gate judged against a single number is a gate on noise.
    foreach (var line in RegressionComparison.DescribeSpread(trials)) Console.WriteLine(line);
    Console.WriteLine($"trials            : {RegressionComparison.Trials} per operation, median of trials reported");

    if (record)
    {
        var baseline = new RegressionComparison.Baseline("astronomy-perf-baseline/v1",
            RegressionComparison.WorkloadDescriptor(),
            measured.ToDictionary(m => m.Operation, m => m.NanosecondsPerOp));
        File.WriteAllText(baselinePath, System.Text.Json.JsonSerializer.Serialize(baseline,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RECORDED          : {measured.Count} operations written");
        foreach (var timing in measured)
            Console.WriteLine($"  {timing.Operation,-16} {timing.NanosecondsPerOp,10:N1} ns/op");
    }
    else if (!File.Exists(baselinePath))
    {
        Console.WriteLine("ERROR no baseline recorded; run with --record to create one. A missing baseline is not a pass.");
        Environment.Exit(2);
    }
    else
    {
        var baseline = System.Text.Json.JsonSerializer.Deserialize<RegressionComparison.Baseline>(
            File.ReadAllText(baselinePath))!;

        if (baseline.Workload != RegressionComparison.WorkloadDescriptor())
        {
            Console.WriteLine($"ERROR workload differs: baseline '{baseline.Workload}' vs now "
                + $"'{RegressionComparison.WorkloadDescriptor()}'. Re-record when that change is intentional.");
            Environment.Exit(2);
        }

        // Exit codes: 0 within tolerance, 1 REGRESSION, 3 INCONCLUSIVE (host too noisy to judge).
        // 3 is distinct on purpose - a busy machine must neither fail a build for a regression that did not happen
        // nor pass one, so a caller can tell "measured and fine" from "could not measure".
        var regressed = new List<string>();
        var inconclusive = new List<string>();

        foreach (var timing in measured)
        {
            if (!baseline.Nanoseconds.TryGetValue(timing.Operation, out var before))
            {
                Console.WriteLine($"ERROR {timing.Operation}: no baseline figure.");
                Environment.Exit(2);
            }

            var (verdict, delta, spread) = RegressionComparison.Judge(before, timing.NanosecondsPerOp,
                trials[timing.Operation]);
            var label = verdict switch
            {
                RegressionComparison.Verdict.Regressed => "REGRESSED",
                RegressionComparison.Verdict.Inconclusive => "INCONCLSV",
                _ => "ok"
            };
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{label,-9} {timing.Operation,-16} before {before,10:N1} ns  after {timing.NanosecondsPerOp,10:N1} ns  "
                + $"delta {delta,7:+0.0%;-0.0%;0.0%}  spread {spread,6:P0}  (tolerance {RegressionComparison.Tolerance:P0})"));

            if (verdict == RegressionComparison.Verdict.Regressed) regressed.Add(timing.Operation);
            if (verdict == RegressionComparison.Verdict.Inconclusive) inconclusive.Add(timing.Operation);
        }

        if (regressed.Count > 0)
        {
            Console.WriteLine($"RESULT            : REGRESSION in {string.Join(", ", regressed)}");
            Environment.Exit(1);
        }
        if (inconclusive.Count > 0)
        {
            Console.WriteLine($"RESULT            : INCONCLUSIVE for {string.Join(", ", inconclusive)} "
                + "(host too noisy; re-run on a quiet machine)");
            Environment.Exit(3);
        }
        Console.WriteLine("RESULT            : within tolerance");
        Environment.Exit(0);
    }
}

static string BaselinePath()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        // The project directory is the ONE home for this file. A copy under bin/ is a leftover from an earlier run and
        // must never be preferred: comparing against stale build output is how a regression gate quietly passes.
        if (File.Exists(Path.Combine(directory.FullName, "MyVedicStar.Astronomy.CacheBench.csproj")))
            return Path.Combine(directory.FullName, "astronomy-perf-baseline.json");
        var candidate = Path.Combine(directory.FullName, "astronomy-perf-baseline.json");
        if (File.Exists(candidate) && !Path.GetFullPath(candidate).Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            return candidate;
        directory = directory.Parent;
    }
    return Path.Combine(AppContext.BaseDirectory, "astronomy-perf-baseline.json");
}

static double TicksToUs(long ticks) => ticks * 1e6 / Stopwatch.Frequency;

AstronomyResult BuildResultNoSettle(double julianDay, AstronomySettings settings)
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

AstronomyResult BuildResult(double julianDay, AstronomySettings settings)
{
    // A stand-in for the engine call with a FIXED, declared cost (SETTLE_TICKS). Using the real SPICE path is not
    // possible here: it needs the native toolkit and a mounted kernel, and this host has neither, so any "engine
    // milliseconds" this harness printed would be invented. A fixed cost makes the cache's share measurable and
    // honest: the composition below reproduces what a real AstronomyResult/Panhandle-scale allocation looks like,
    // and the REPORT multiplies nothing.
    var spinUntil = Stopwatch.GetTimestamp() + settleTicks;
    while (Stopwatch.GetTimestamp() < spinUntil) { }

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
