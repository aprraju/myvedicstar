# Test and deployment review

Reviewed the migrated MyVedicDay and MyVedicStar solution boundaries, test projects, shared-host regression tests, CI workflow dependencies, deployment configuration, PowerShell release scripts and runtime Dockerfile.

## Validation results

| Scope | Passed | Skipped | Result |
| --- | ---: | ---: | --- |
| MyVedicDay: 11 test projects | 607 | 0 | All passed |
| MyVedicStar: 3 test projects | 87 | 8 | Available tests passed; PostgreSQL unavailable locally |
| Shared host | 9 | 0 | All passed |
| Deployment contract checks | — | — | Passed against a fake Azure CLI |
| Published host smoke checks | — | — | Passed against a real local ASP.NET Core process |
| Workflow YAML, job dependencies and reusable workflow targets | — | — | Valid |
| Deployment PowerShell and environment JSON | — | — | Valid |
| Combined publish and package restore/audit | — | — | Succeeded |

Day tests cover API contracts, access rules, protection and concurrency, exception mappings, dependency architecture, configuration, caching, localization, locations, astronomical calculations, Panchang, festivals, Muhurta and AI-provider behavior. Star tests cover signed token validation, scopes and multiple schemes, user-info contracts, persistence disabled/enabled, security, rate limiting, health, documentation, business logic, caching and PostgreSQL persistence/migrations.

Shared-host tests use signed JWTs with a test key. They verify both protected APIs accept the same audience, reject wrong audiences and missing scopes, preserve routes and successful response fields, count global rate limits across APIs, expose both APIs in OpenAPI, register both enabled dependency checks, and keep Day functional after a Star dependency exception. Errors retain trace/correlation IDs and security headers without leaking dependency details.

## Findings addressed

- Star's inherited analyzer/XML-documentation settings initially changed Day's compilation behavior. Per-project settings preserve each API's original policy.
- Star PostgreSQL tests previously skipped in CI because no database was configured. Its independent CI now starts PostgreSQL and supplies the test connection string; the fixture creates a unique database per run.
- Combining executable projects introduced colliding appsettings filenames. Publish output now separates API settings under `apis/day` and `apis/star`; local Development settings are excluded.
- Shared-host configuration now preserves environment, command-line and test-host overrides instead of clearing all configuration sources.
- Exception handling cleared correlation response headers. Both APIs now compile the same policy/middleware source that retains the validated correlation ID for the header and Problem Details body.
- Release automation now publishes `MyVedicStar.Host.dll` and builds `myvedicstar-api:<commit>` after both independent CI pipelines and shared-host validation succeed.
- Deployment contract checks exercise missing-image and unsupported-revision-mode failures before mutations, as well as preservation of CPU, memory, scale rules, unrelated environment variables, secret references, networking/configuration and other containers.
- Star cache telemetry and its Cross-Origin-Opener-Policy header are retained by the combined host.

## Azure checks and remaining verification

Read-only Azure inspection confirmed the target `ca-myvedicstar-api-prod` in `rg-myvedicstar-prod-app-eus2` exists, uses Single revision mode and port 8080, and currently runs the standalone Star image. It has 0.25 CPU, 0.5 GiB memory and a maximum of one replica. The release code retains those existing settings.

No image was pushed and no live app, identity, DNS record, role assignment or secret was changed. Local Docker is unavailable, so the chiseled Linux image itself has not been executed here. The actual portable publish output was started and smoke-tested locally. PostgreSQL 17 was subsequently tested locally using an isolated temporary server; both CI pipelines now supply PostgreSQL as well.

Preserving the old Day hostname requires its custom domain/certificate/DNS or forwarding arrangement to route to the Star app before the old app is retired. The source migration preserves API routes and successful contracts; it does not change consumer DNS.
## CI concurrency regression follow-up

The concurrency test now uses the same principal factory as the test authentication handler and verifies both "oid" and "mvs_user_id" settings. This prevents the held lease and API request from using different identity partitions. The migrated integration suite passes all 146 tests. The original Day solution passes 607 tests, producing 11 separate TRX reports. Both repositories use the logger's generated filenames instead of the shared results.trx filename.

## HybridCache follow-up

The common HybridCache module replaces registered Redis/in-process-only providers with bounded L1 and optional existing PostgreSQL L2. Both APIs retain their adapters and routes. Native cache behavior, real migration/provider restart/expiry/cleanup/outage tests, exact raw Panchang round trips, complete cached/uncached HTTP contracts and local performance measurements are covered. See [hybrid-cache.md](../hybrid-cache.md) for policies, measured results and the database-first rollout. Production L2 remains disabled; no production migration or deployment was performed.

Final HybridCache validation: 627 Day solution tests, 109 Star solution tests and 9 shared-host tests passed with zero skips. The 23 shared cache tests execute in both independent solutions, including six tests against real PostgreSQL. The combined portable publish, published-host authentication/routes smoke check, deployment resource-preservation contract and workflow/configuration validation passed. No live Azure deployment or container-level load test was performed.
