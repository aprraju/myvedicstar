# api#72 (AST-07) — data, caching and performance: measurement report

Branch: `feature/api72-ast07-cache-measure`
Status: implementation complete, verified in a state-isolated copy of `astronomy/`; **not merged**.

Everything below is either (a) measured on this host with the command named, or (b) declared as a setting and labelled.
No figure in this report is an estimate presented as a measurement.

---

## 1. What is cached

`astronomy/MyVedicStar.Astronomy.Core/ResultCache.cs`

| Item | Value |
|---|---|
| Cached value | A finished `AstronomyResult` (bodies + provenance) — the engine's answer, not an intermediate |
| Cache key | `AstronomyCacheKey` = quantised instant + resolution + zodiac + ayanamsa + node convention + observer (lat, lon, elevation) + body-set token |
| Instant quantisation | The key buckets the Julian Day to the caller's `Resolution` (Panchang/Kundali work per civil day → `TimeSpan.FromDays(1)`). A raw-JD key would never hit, because successive calls carry seconds of drift |
| Body set | Order-insensitive (`Sun,Moon` == `Moon,Sun`), and "all bodies" is its own distinct token (`*`) — never conflated with an explicit subset |
| Bound | Fixed capacity, least-recently-used eviction (default 512), safe for concurrent callers |
| Concurrent misses | Computed outside the lock; if two callers race, the first stored value wins for both, so one key maps to one result instance (determinism) |

## 2. What invalidates it

Nothing ambient. Every invalidation is explicit and **returns a count**:

| Method | Drops | Use |
|---|---|---|
| `Clear()` | everything | shutdown / wholesale reset |
| `InvalidateBefore(earliestServed)` | keys before a new served floor | ephemeris kernel coverage moved forward |
| `InvalidateBetween(earliest, latest)` | keys outside the inclusive window | kernels remounted as a different span |
| `InvalidateBodySet(body)` | keys whose request includes that body (including `*`) | one body's calculation changed |

**The #159 shape, and how this cache avoids it.** `TtOffsets.Install` is install-once behind a latch, so a second
install is a silent no-op and `ResetForTests` cannot restore anything if another component installed first. The trap is
an invalidation that *appears* to run while changing nothing. `AstronomyResultCache` has no latch and no first-writer
rule: every invalidation applies unconditionally to whatever currently matches, returns how many entries it removed,
and bumps a public `Version` counter. `ResultCacheTests.Second_invalidation_is_as_effective_as_the_first_no_silent_no_op`
seeds, invalidates, seeds again, invalidates again, three times, and asserts each call removed exactly one entry — a
regression to the latch shape fails that test.

The engine version is deliberately **not** in the key: a new engine is a new process, and an in-process key cannot
detect an out-of-process upgrade. Claiming otherwise would be untestable.

## 3. The hot path, with numbers

Harness: `astronomy/MyVedicStar.Astronomy.CacheBench` (console app, in the astronomy solution, so the numbers are
reproducible from the repo rather than from a laptop session).

Run:

    dotnet run -c Release --project astronomy/MyVedicStar.Astronomy.CacheBench

Three consecutive runs on the host below (numbers are per-run; the spread is reported because it is real):

Host: `raju-PowerEdge-R710`, Linux 7.0.0-38, .NET runtime 10.0.15, 16 processors, workstation GC, Interactive latency.
(Note: the repo's `global.json` pins SDK 10.0.400 / `latestFeature`; this host has 10.0.115 only, so the harness was
built in a copy with `rollForward: disable, 10.0.115`. Treated as a known environment gap, not a fix in this branch.)

| Measurement | Run 1 | Run 2 | Run 3 | Method |
|---|---|---|---|---|
| Cache hit, `GetOrAdd` (key pre-built) | 353 ns/op | 324 ns/op | 325 ns/op | 200 000 hits after 100 000-op warm-up, dictionary+LRU touch |
| Cache hit, cold-attributed clean rebuild | 400 ns/op | — | — | same, fresh build and first process |
| Key construction | 2.7 µs median (p95 4.4 µs) | 2.7 µs | 2.8 µs | 10 001 batched samples (100 builds/sample), median of the warm half |
| Building one `AstronomyResult` (12 bodies) | 4.6–6.1 µs median | 4.9 µs | 4.6 µs | 1 001 single-sample timings, median |
| One cache miss incl. key + build | 6.0–7.2 µs/op | 6.1 µs | 6.0 µs | 1 000 distinct days, every call misses |
| Churn: capacity 64, working set 512 | 9.3–10.0 µs/op | 9.3 µs | 9.3 µs | 500 ops, hits=0, evictions=436, no re-hit (see caveat) |
| `InvalidateBefore` over 1 024 entries, 512 removed | 2.9–4.0 ms | 3.8 ms | 3.7 ms | single call, linear scan under the lock |

### What is NOT measured here, and why

**The engine call itself.** This host can measure **neither** provider: the analytical engine is not what the ratio is about (and now that the default is analytical since 850323d4, a reader might assume the declared 500 us is a Meeus figure - it is not a figure at all); real SPICE needs the native CSPICE toolkit and a mounted DE440/DE441 kernel. This host has
neither (the provider's own `ProbeNativeLibrary` would return `false`), so any "engine milliseconds" produced here
would be invented. The harness therefore takes the engine cost as a **declared setting** (`MVS_SETTLE_MS`, default
0.5 ms, printed with the run and labelled "SETTING, not a measurement"). With that setting the hot-path arithmetic is:

    uncached call = declared engine (500 µs) + key (2.7 µs) + build (4.6 µs)  ≈ 507 µs   [engine part DECLARED]
    cached call   = key lookup only (≈ 0.33 µs measured hit + key build if the caller rebuilds it)

So the cache's measured share of a cached call is ~0.3 µs, and its measured share of an uncached call is ~7 µs on top
of engine time this host cannot produce. **Replace the declared 500 µs with a real engine measurement on a host that
mounts the toolkit and kernel before any planning decision depends on the ratio.**

Two earlier harness versions produced wrong numbers and were discarded rather than published:
a tight-loop key benchmark that was dead-code-eliminated (reported 5.8–11.6 µs where batched sampling gives 2.7 µs),
and an uncached-vs-cached sweep that compared a JIT-cold path against a warmed one (produced a negative "cache
overhead"). Both are recorded here because a plausible wrong figure surviving into planning is the failure mode this
issue is about.

### Caveat on churn

With capacity 64 and working set 512 the harness visits days in ascending order, so by the time a day recurs it has
already been evicted: hits = 0, evictions = 436 over 500 ops. That measures **eviction cost**, not the benefit of a
larger cache. A workload with real temporal locality (repeated Panchang calls on the same day) is covered by the hit
row: 325–400 ns.

## 4. Scaling implication (stated, not measured)

Eviction is O(1) per entry (dictionary + linked list). Invalidation is O(n) over the entries, under the lock, measured
at ~3–4 ms for 1 024 entries / 512 removals. Kernel remounts are rare (start-up or operator action), so this was not
optimised further; if remount frequency ever becomes real, the fix is an index by day bucket, not a faster scan.

## 5. Tests

`astronomy/MyVedicStar.Astronomy.Tests/ResultCacheTests.cs` — 10 tests: key quantisation, body-set order-insensitivity,
"all bodies" ≠ subset, hit accounting, LRU eviction order, `InvalidateBefore` count + `Version` bump, the repeat-
invalidation (no silent no-op) test, windowed invalidation, body-set invalidation, and observer affecting the key.

Verified: `dotnet test` on `MyVedicStar.Astronomy.Tests` in a copy of `astronomy/` → **136 passed, 0 failed** (126
pre-existing + 10 new).

## 6. Files

- `astronomy/MyVedicStar.Astronomy.Core/ResultCache.cs` (new)
- `astronomy/MyVedicStar.Astronomy.Tests/ResultCacheTests.cs` (new)
- `astronomy/MyVedicStar.Astronomy.CacheBench/` (new: `Program.cs`, `.csproj`)
- `astronomy/MyVedicStar.Astronomy.slnx` (bench project registered under `/bench/`)

## 7. Not done / open

- No wiring of the cache into `SpiceAstronomyEngine` or `IEphemeris` yet: that changes call semantics and needs the
  AST-04/AST-05 numeric surface, which is still `NotImplementedException` in this branch.
- Real engine timing requires a host with CSPICE + kernel. None exists yet: api#173 (owner decision: fetch CSPICE N0067 + DE440/DE441 from NAIF at build time) provisions the test, CI and deploy images, owned by api-developer8. Until that image runs a spice-selected calculation, the 500 us engine term stays a declared setting and **must not** be read as a measurement of either provider.
- `global.json` SDK pin (10.0.400) does not match this host's 10.0.115; the branch leaves `global.json` untouched.