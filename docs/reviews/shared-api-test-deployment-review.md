# Test and deployment review

Reviewed the migrated MyVedicDay and MyVedicStar solution boundaries, test projects, shared-host regression tests, CI workflow dependencies, deployment configuration, PowerShell release scripts and runtime Dockerfile.

## Validation results

| Scope | Passed | Skipped | Result |
| --- | ---: | ---: | --- |
| MyVedicDay: 11 test projects | 606 | 0 | All passed |
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

No image was pushed and no live app, identity, DNS record, role assignment or secret was changed. Local Docker is unavailable, so the chiseled Linux image itself has not been executed here. The actual portable publish output was started and smoke-tested locally. Eight PostgreSQL tests still need a database locally; CI is configured to run them.

Preserving the old Day hostname requires its custom domain/certificate/DNS or forwarding arrangement to route to the Star app before the old app is retired. The source migration preserves API routes and successful contracts; it does not change consumer DNS.