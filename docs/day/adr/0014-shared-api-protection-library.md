# ADR-0014: Edge protection from the shared MyVedicStar.ApiProtection library

- Status: Accepted
- Date: 2026-10-09

## Context

The API had its own per-IP fixed-window rate limiter (one global limit and a stricter `ai-explanation` policy), a
Kestrel body-size limit and a correlation-ID middleware. It never read `X-Forwarded-For`. Behind the Container Apps
ingress, every request therefore appeared to come from the ingress proxy, so all callers shared one rate-limit
bucket.

`MyVedicStar.ApiProtection` (repository `myvedistar-common`, published to GitHub Packages) is the platform's shared
edge protection for MyVedicStar APIs. It provides:

- trusted-proxy handling
- named sliding-window rate-limit policies that partition by authenticated user or by trusted client IP
- health-path bypass
- request-size limits
- correlation IDs
- Problem Details rejections

It also has in-memory caching, circuit breakers, timeouts, bulkheads and an SSRF handler.

## Decision

Use version 1.5.0 of the library for edge protection, authentication and integration resilience:

- **Trusted proxies:** `ApiProtection:TrustedProxies` drives `UseForwardedHeaders()`. Production trusts the Container
  Apps infrastructure subnet (`10.2.0.0/23`).
- **Rate limits:** the global sliding-window limit, plus one named policy per endpoint group:
  - `public-read`: Panchang, festivals, locations, traditions, glossary
  - `good-time`: `/muhurta/*`
  - `astrology-expensive`: the AI explanation, replacing `ai-explanation`
- **Health probes:** the paths in `ApiProtection:HealthCheckPaths` are never limited.
- **Request size:** `ApiProtection:RequestSize`; Kestrel's default body limit uses the same value.

Our own rate limiter and the `RateLimiting` and `RequestLimits` settings are removed.

The local correlation middleware and AI timeout/circuit-breaker pipeline are removed. The package now owns:

- Correlation headers and logging scopes. API Problem Details read the ID from the package's response header.
- Authentication and normalized claims. MVC endpoints require `ApiScope`, except the explicitly anonymous
  GET actions for today's Panchang and location resolution. Health probes and enabled documentation remain anonymous.
- Per-user rate-limit partitions using `mvs_user_id` with the validated issuer, rather than Entra-only `oid`.
- Concurrency leases and an expensive-calculation deadline for the explanation endpoint.
- AI integration deadlines, per-provider circuit breakers, and the `ai-provider` bulkhead.
- SSRF-protected SDK HTTP transports with automatic redirects disabled. A retry-only HTTP policy retries 5xx;
  SDK retries are disabled so 4xx responses are never retried.
- Protection metrics exported alongside application metrics through OpenTelemetry.

Retained domain-specific infrastructure:

- **Redis cache** (ADR-0003). The library's cache is per process, and the architecture requires a distributed cache.
- **Daily cache keys, location-midnight expiry and distributed cache coalescing** (ADR-0003). These support daily
  reuse across date ranges and domain versioning; replacing them with the package's fixed-lifetime memory cache
  would violate the repository architecture. The library's cache remains registered for optional process-local use.

Packages resolve through `nuget.config`. `MyVedicStar.*` comes only from GitHub Packages and everything else only
from nuget.org (package source mapping). The image build receives a read-only token as a secret build argument.

## Consequences

- **Per-client limits:** each client now has its own limit behind the ingress. Limits are per replica: the
  library's limiters are process-local.
- **Authentication:** controller actions require a validated provider token and the configured delegated API scope
  (`access_as_user` by default), except `GET /api/v1/panchang/today` and `GET /api/v1/locations/resolve`.
- **API Management:** if API Management is placed in front of the ingress, add its addresses to `TrustedProxies` and
  check the forward limit. Otherwise the gateway's IP becomes the client IP.
- **Building the image:** the build needs `packages: read` in CI. Locally it needs a `GITHUB_TOKEN` with
  `read:packages`.
- **Configuration changes:** all `ApiProtection` settings require an application restart. Provider secrets must
  come from user-secrets or Key Vault; provider IDs and enabled flags remain configurable.
- **Framework:** the consuming application remains on .NET 10; the package targets .NET 8 and is compatible.
