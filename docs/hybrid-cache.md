# Shared HybridCache

Standalone MyVedicDay.Api and the combined MyVedicStar.API register one reusable MyVedicStar.Caching module. The host owns one native Microsoft HybridCache instance, one dedicated bounded L1, and one optional PostgreSQL L2. Day's IDistributedObjectCache and Star's ICacheStore are thin compatibility adapters. Superseded Redis options, the bespoke memory store and the old JSON cache provider were removed; their behavior tests were replaced by native-cache and adapter coverage. Legacy CachingOptions is accepted for source/configuration compatibility, but active bounds use ApplicationCacheOptions. User profiles, login results, tokens, authorization decisions, rate-limit state and other security data have no new cache call sites.

## Provider and storage

Verified on net10.0 with Microsoft.Extensions.Caching.Hybrid 10.10.0 and Microsoft.Extensions.Caching.Postgres 1.2.2. The PostgreSQL provider implements IDistributedCache and accepts a caller-owned NpgsqlDataSource; native HybridCache performs the two-level lookup and per-process single-flight. No custom SQL storage implementation, Redis dependency or Azure resource was added.

References: [Microsoft distributed cache documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-10.0), [Microsoft provider source](https://github.com/Azure/Microsoft.Extensions.Caching.Postgres), [HybridCache documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0), [Npgsql metrics](https://www.npgsql.org/doc/diagnostics/metrics.html).

EF migration 20261011000000_AddHybridCacheEntries creates the provider's documented id/value/expiresattime/slidingexpirationinseconds/absoluteexpiration columns in myvedicstar.cache_entries. The primary key provides lookup; ix_cache_entries_expiresattime supports expiry cleanup. The table is LOGGED so normal PostgreSQL restart/crash recovery retains committed rows. Provider CreateIfNotExists is false. The table is infrastructure and is deliberately not an Authorize entity or model snapshot addition.

## Policy and keys

Caching is enabled for local memory by default. Caching:L2:Enabled is true in MyVedicStar.API's Development settings (local PostgreSQL) and in production (deploy/environments/prod.json). MemoryLimitBytes defaults to 32 MiB and MaximumPayloadBytes to 512 KiB. The dedicated L1 does not impose size requirements on other application's IMemoryCache users. Its bound accounts for serialized native payload sizes, not total CLR process memory or per-request deserialized objects. Native invalidation metadata is separately bounded by 4096 hashed tags; Azure memory must still cover both APIs, runtime, GeoNames data and concurrent calculations.

| Cached data | L1 | L2 maximum | Validity |
|---|---|---|---|
| Today / future single-day neutral Panchang | 5 minutes | 30 days maximum | Today expires at the selected location's next local midnight |
| Historical single-day neutral Panchang | disabled | 30 days | Stable historical data |
| Calendar neutral days | 5 minutes | 24 hours | Reuses the same day keys with a distinct lookup policy |
| Good Time neutral evaluations | disabled | 6 hours | Today also clamps to local midnight |
| Raw Panchang including sun events | disabled | 30 days | Stable date/location/timezone/method data |
| Festivals | disabled | 2 days | Today also clamps to local midnight; neighboring days are calculated together |
| Traditions, Panchang traditions, glossary, birth stars | 5 minutes | 2 days | Includes language preferences, tradition and content module fingerprints |

All lifetimes are configurable under Caching:Policies. A zero memory lifetime explicitly disables native L1 reads and writes; it does not use zero as a default expiration. Domain validity and the selected L2 policy both clamp serialized envelope validity. Calendar lookups may reuse L1 entries created by single-day requests (the same neutral day data). L1 and L2 hits deserialize independent copies, avoiding shared mutable DTOs.

Keys are typed, globally versioned with Caching:KeyVersion, and SHA-256 hashed before storage. Request parameters never appear in PostgreSQL keys or application cache logs. Locations use the existing normalization and invariant coordinates; keys identify date, timezone, method, engine/DTO versions. Raw keys also identify TZDB version and preserve nanosecond Instant values in round trips. Good Time keys additionally identify activity, tradition, rules version, a fingerprint of the full activity rules profile, and optional birth Nakshatra. Missing Nakshatra preserves the original evaluation. Festivals cache neutral all-tradition data before filtering/localization. Live fields, language presentation, location labels, GeneratedAt and Now/Next identifiers are rebuilt per request. Change engine versions when calculation behavior changes and bump KeyVersion for incompatible cache-wide changes. Typed RemoveAsync<T> invalidates the exact key. Legacy untyped removal uses a fixed hashed tag bucket and can also invalidate unrelated keys in that bucket; those requests calculate again without changing their responses. The bounded buckets avoid native per-key tag metadata growing without limit.

## PostgreSQL capacity and failures

Reuse Database:ConnectionString from secure environment configuration/Key Vault. No credentials are committed. The cache has a separate pooled NpgsqlDataSource using the same database credentials: MinPoolSize 0, MaxPoolSize 4, MaximumConcurrentOperations 4, UTC session timezone, Enlist false, and application_name myvedicstar-cache. The separate pool isolates cache operations from business transactions. CommandTimeoutSeconds defaults to 2 and also bounds the entire adapter operation including semaphore wait. Caller cancellation propagates; timeout/outage failures become misses or skipped writes. Failures initiate a 10-second backoff. Successful business factories run independently of backend availability; exceptions from business calculations remain unchanged and are never cached. Serialization/oversize failures return the existing factory result without rerunning the factory. Invalid payloads are discarded and recalculated.

Native single-flight is within one process; simultaneous cold misses on different replicas may calculate independently. The provider's advisory-lock API is not used. At four cache connections per replica, total potential cache connections are four times active replicas, in addition to business pools and monitoring. The deployment preserves the existing CPU, memory, network and scale settings; it does not assume or resize the live app to the request's illustrative resource values.

The provider periodically deletes expired rows when cache activity triggers cleanup (30 minutes by default, minimum 5 minutes). Expired rows cannot be returned even before physical deletion. Scale-to-zero stops activity and cleanup resumes with traffic; this is not a guaranteed background schedule. Monitor cache row count/size and database I/O before increasing traffic or replicas.

OpenTelemetry subscribes to MyVedicStar.HybridCaching and Npgsql. Instruments include cache.requests, cache.factory.calls, cache.failures, cache.lookup.duration, cache.factory.duration, cache.serialization.duration, cache.postgres.duration, cache.l1.estimated_size, cache.l1.entries, cache.l1.lookups.hits, cache.postgres.read.hits and cache.postgres.writes. Lookup duration includes factory work on misses. Memory/backend lookup and write counts include native tag metadata operations, so they are not advertised as exact HTTP hit ratios. Factory counts represent business factories. PostgreSQL pool connection metrics come from Npgsql, not a guessed HybridCache statistic. The cache and business data sources use fixed names myvedicstar-cache and myvedicstar-business so pool labels do not contain connection strings. Filter/summarize Npgsql connection metrics by pool and inspect pg_stat_activity application_name=myvedicstar-cache; do not log secure connection strings. Optional cache failures degrade Day/shared readiness during backoff without disabling otherwise healthy calculations.

## Database-first rollout

1. Build/test/publish the combined image normally. (Production enabled L2 on 2026-10-16 through these steps.)
2. Review deploy/sql/hybrid-cache-migration.sql, generated by EF from the immediately preceding migration through the cache migration with --idempotent. Confirm the existing Authorize migration history is at the predecessor. Apply using the existing controlled database migration process before enabling L2. It adds only the cache table/index and migration history entry; it does not mutate user tables. If the database is older, generate/apply the full pending Authorize migration script using the existing process.
3. The runtime database role needs schema USAGE and table SELECT/INSERT/UPDATE/DELETE. Grant through the existing privilege management process. Keep Database:ApplyMigrationsOnStartup=false for production and provider CreateIfNotExists=false.
4. The cache connects with the same database sign-in as the business data source: Database__ConnectionString, or managed identity (Database__UseManagedIdentity with Host, Name, Username and an Entra token, as in production). Enable Caching__L2__Enabled=true in the deployment settings after the schema/permissions are verified; the release pipeline's migration job creates the table and grants the API role DML on the schema. Both APIs share this single host setting.
5. Deploy the combined image to ca-myvedicstar-api-prod using the existing pipeline. Monitor health, factory calls, backend failures, latency, L1 size, process memory, pool connections and database I/O. Roll back L2 by setting the flag false; disabling Caching__Enabled bypasses all application cache operations. Cache rows can remain until normal expiry/cleanup.

No production database migration or Azure deployment was performed while implementing this change.

## Verification

Both independent solutions include the common cache test project. Both CI pipelines provide PostgreSQL 17 and set MYVEDICSTAR_TEST_POSTGRES so real database tests execute. Locally this variable must point to a disposable PostgreSQL server with CREATE DATABASE permission; fixtures create unique databases and drop them on completion. Real migration tests cover logged storage/indexes, independent-instance/restart reuse, expiry/cleanup, unavailable-backend fallback and pool limits. Native tests cover concurrency, waiter cancellation, disabled caching, zero L1, independent DTO instances, absolute expiry, payload size/memory bounds, key privacy/versioning, factory/serialization failures and corrupted distributed bytes.

HybridCacheApiTests compares complete cached/uncached Today and Good Time JSON using a fixed presentation clock, validates raw Panchang reuse between features, concurrent hits and new-host persistence, and compares localized reference contracts. The existing astronomical, restriction/scoring, authentication, authorization, error and concurrency test suites remain enabled. The same test records cold/warm/uncached local TestServer timings in its TRX output, without a flaky speed threshold. Today still calculates neighboring festival days on a cold miss using the existing festival engine; warm festival results are reused. These local measurements do not include Azure networking, cold starts or constrained container CPU and cannot guarantee a production latency target. The bounded L1 stress test is a cache memory budget check; production process RSS and load behavior require observation in the actual deployment.

The cache-only migration SQL can be regenerated with an existing dotnet-ef 10.0.12 tool:

```powershell
dotnet build src/MyVedicStar.Authorize.Data -c Release -p:GenerateRuntimeConfigurationFiles=true
dotnet ef migrations script 20261010071425_ReplaceGivenNameAndSurnameWithName 20261011000000_AddHybridCacheEntries --idempotent --configuration Release --no-build --project src/MyVedicStar.Authorize.Data --startup-project src/MyVedicStar.Authorize.Data --output deploy/sql/hybrid-cache-migration.sql
```

The data project's existing design-time factory requires no live database connection to generate this script. Generation and review do not apply it.

Initial local benchmark (Windows, .NET 10.0.401, PostgreSQL 17 on a loopback test instance, TestServer, five measured requests per mode): Today uncached mean 13.09 ms versus warm mean 6.12 ms; Good Time 3.50 ms versus 3.09 ms. Cold cached requests were 342.18 ms and 16.40 ms respectively, including first-use setup. This small local sample shows calculation reuse and also that PostgreSQL overhead can narrow Good Time's gain on fast hardware. It is not an Azure performance claim. Rerun HybridCacheApiTests and compare its TRX output on the intended deployment hardware before changing policy budgets.
