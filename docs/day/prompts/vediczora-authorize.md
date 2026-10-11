# Task: Build `VedicZora.Authorize`, the authentication and user-registration service, and secure `MyVedicDay.Api` with it

> Agent prompt. Paste it into Claude Code or GitHub Copilot, working in this repository.

Read and follow `.github/copilot-instructions.md`, `docs/architecture/README.md` and every ADR in `docs/adr`. Apply the same standards to the new service: layering, facades, repositories, the Options pattern, Problem Details, structured logging, OpenTelemetry, tests and ADRs. Do not weaken any existing boundary in `MyVedicDay`.

## Division of responsibility

| | `VedicZora.Authorize` (new, separate service) | `MyVedicDay.Api` (existing) |
|---|---|---|
| Sign-in | Integrates with **Microsoft Entra External ID** (Google, Apple, Facebook and application email accounts, all federated inside External ID). Validates the signed-in user's token. | Nothing. |
| First-time login | **Registers the user** (creates the profile) on first login. | Nothing. |
| User info | Returns **minimal user info** (name, email), plus full profile endpoints. | Nothing. |
| Authorization | **Owns authorization data** (roles, permissions, account status) and answers authorization questions. | **Only validates access tokens and enforces authorization policies** on its endpoints. |
| Activity | Records login, logout, device, IP and location events. | Logs requests as it does today (no PII). |
| Data | Owns its **PostgreSQL** database. | No user database. |

- **No code dependency.** `MyVedicDay.Api` must not reference any `VedicZora.Authorize` project, and vice versa. They share only a documented **token-claims contract**.
- **Anonymous endpoints stay anonymous.** Existing Panchang, location, tradition and glossary endpoints keep working without a token. Authentication applies only to endpoints that need it.

## Identity platform

- **One issuer.** Entra External ID (CIAM tenant, `ciamlogin.com` authority) is the only token issuer. Google, Facebook, Apple and email accounts (password or one-time passcode) are configured inside External ID user flows. Neither service ever accepts raw Google, Apple or Facebook tokens.
  - Verify Apple's availability in the tenant and record it as a decision.
  - Handle Apple's private-relay email and its behaviour of sending the name only on the first sign-in.
- **Clients** (web SPA and mobile) use the authorization code flow with PKCE via MSAL.
- **Two options for how `MyVedicDay.Api` authorizes. Evaluate both in an ADR and recommend one:**
  - **A. Token enrichment (preferred for statelessness).** `VedicZora.Authorize` exposes an Entra External ID **custom authentication extension** endpoint (token-issuance-start event) that adds claims at sign-in: `vz_user_id`, `vz_roles`, `vz_status` (Active, Incomplete, Suspended) and `vz_profile_complete`. `MyVedicDay.Api` then authorizes from the token alone. Secure the extension endpoint as Microsoft specifies (Entra-issued tokens for the extension's app registration).
  - **B. Authorization check endpoint.** `MyVedicDay.Api` calls `VedicZora.Authorize` (`POST /api/v1/authorize/check`) for decisions that need live state, such as immediate suspension. Use a typed resilient HttpClient (timeout, retry on transient errors only, circuit breaker), a short-lived cache, and a documented fail-closed/fail-open policy.
  - A hybrid (A for claims, B only for sensitive operations) is acceptable if justified.

## `VedicZora.Authorize` solution structure

Place it in `services/VedicZora.Authorize/` as its own solution (`VedicZora.Authorize.sln`), independently buildable and deployable. Confirm this placement with me.

```
VedicZora.Authorize.Api                 HTTP only: token validation, controllers, Problem Details, CSP/security headers, OpenAPI
VedicZora.Authorize.Business            Facades: IUserRegistrationFacade, IUserInfoFacade, IAuthorizationFacade,
                                        ISessionActivityFacade; provisioning, account linking, validation
VedicZora.Authorize.Data                EF Core + Npgsql (PostgreSQL), DbContext internal, repositories, migrations
VedicZora.Authorize.Integration         Adapters: location resolution, Entra/Microsoft Graph if needed
VedicZora.Authorize.Shared              Only if genuinely needed (exceptions, primitives)
VedicZora.Authorize.Tests.Unit
VedicZora.Authorize.Tests.Integration   WebApplicationFactory + Testcontainers PostgreSQL
```

Dependency rules match MyVedicDay's: the API references only Business facades; Business references Data and Integration; and there is an architecture test that enforces this.

## First-time login: registration

- **Stable internal identifier.** Every user gets a new GUID `UserId`. External identities link to it through a separate table keyed by `(Issuer, Subject)` (External ID `oid`/`sub`), recording the upstream `idp` (google.com, facebook.com, apple.com, local). One user can have several linked identities.
- **Registration is idempotent.** It happens on the first authenticated call (`GET /api/v1/userinfo`), or in the token-issuance extension under option A.
  - Create the user inside a transaction.
  - Survive concurrent first requests through unique constraints and retry, never creating duplicates.
  - Return **201** on creation and **200** afterwards.
- **Profile fields:**

  | Field | Rules |
  |---|---|
  | FirstName | Required. 1–100 characters, Unicode letters, space, `'`, `-`, `.`. From `given_name` when present. |
  | LastName | Required. Same rules. From `family_name` when present. |
  | Email | Required and **unique in the system**. From the token; not editable by the client. Store the display form and a normalized form (trimmed, lower-cased), with a **unique index on the normalized form**. PostgreSQL: use a normalized column, or `citext`; justify the choice. |
  | PhoneNumber | Optional. Validate and normalize to E.164 with `libphonenumber-csharp`. |
  | Location | City, state, country code, IANA time zone, and coordinates rounded to about 1 km. Never store raw precise coordinates. |
  | Status | Active, Incomplete (name missing, common with Apple) or Suspended. Expose `missingFields`. |
  | CreatedAtUtc, UpdatedAtUtc, LastSignInAtUtc | Timestamps. |

- **Location resolution.** Resolve location behind an `ILocationResolver` adapter in `VedicZora.Authorize.Integration`, calling `MyVedicDay.Api` `GET /api/v1/locations/resolve` (accepts `place=City, State, Country` and/or `latitude`/`longitude`; returns city, state, country, IANA time zone). Use a resilient typed HttpClient, and store only the resolved result. If `MyVedicDay.Api` is unavailable, save the profile and mark location `Pending` rather than failing registration.
- **Same email from a different provider.** Auto-link only when both identities have `email_verified = true`. Otherwise return **409** Problem Details with code `account_exists_different_provider`, and design an explicit linking flow for a later phase. Apple private-relay addresses are ordinary unique emails.

## Endpoints (`VedicZora.Authorize.Api`)

| Method | Path | Auth | Behaviour |
|---|---|---|---|
| `GET` | `/api/v1/userinfo` | User | **Minimal user info**: `userId`, `firstName`, `lastName`, `displayName`, `email`, `status`, `profileComplete`. Registers the user on first call. |
| `GET` | `/api/v1/me` | User | Full profile (adds phone, location, timestamps). |
| `PATCH` | `/api/v1/me` | User | Update first name, last name, phone and location. Email is immutable. |
| `POST` | `/api/v1/sessions` | User | **Login event**, called by the app right after sign-in. Body: device info, app version, platform, the **device-reported IP**, optional location. Returns `sessionId`. |
| `DELETE` | `/api/v1/sessions/{sessionId}` | User | **Logout event.** Document the External ID sign-out endpoint for clients. |
| `GET` | `/api/v1/me/activity` | User | The user's own sign-in history, paged. |
| `POST` | `/api/v1/authorize/check` | Service (client-credentials token for `MyVedicDay.Api`) | Option B: `{ userId or subject, permission }` → `{ allowed, reason }`. |
| `POST` | `/api/v1/entra/token-issuance` | Entra extension | Option A: returns the custom claims. |
| `POST` | `/api/v1/security/csp-reports` | Anonymous, rate-limited | CSP violation reports. |
| `GET` | `/health/live`, `/health/ready` | Anonymous | Readiness includes PostgreSQL. |

- All errors use Problem Details: 400 validation, 401, 403, 404, 409 conflict, 429, 503.
- Full OpenAPI and Swagger documentation with an **OAuth2 security scheme** against External ID, in the same style as `MyVedicDay.Api`.

## Changes to `MyVedicDay.Api` (validation and authorization only)

- **Token validation.** Add JWT bearer validation with `Microsoft.Identity.Web` (`AddMicrosoftIdentityWebApi`): issuer, audience, signing keys, lifetime and the required scope (for example `access_as_user`). All values come from validated `EntraExternalId` options; nothing is hard-coded.
- **Policies.** Add policy-based authorization (`AuthenticatedUser`, `ActiveUser` requiring `vz_status = Active`, `Admin` for the future), built only from token claims (and option B if adopted). No database, no user tables, no registration logic.
- **Scope.** Protect only endpoints that need it; existing public endpoints stay anonymous. Add one small authenticated endpoint (for example `GET /api/v1/panchang/preferences/defaults`, returning the user's location and tradition defaults from claims) to prove the setup end to end.
- **Controllers stay thin.** Authorization uses attributes and policies only.
- **Swagger.** Add the OAuth2 security scheme.
- **Tests.** Extend `LayerDependencyTests` to assert that `MyVedicDay.Api` never references a `VedicZora.*` assembly.

## Login, logout and activity logging (designed for a future social application)

- **`UserActivityEvent` fields** in PostgreSQL:
  - EventId, UserId (nullable for failures), EventType, OccurredAtUtc;
  - SessionId, CorrelationId, TraceId;
  - IdentityProvider;
  - ClientType (web, iOS, Android), AppVersion, OS, device model, UserAgent (parsed and raw, truncated);
  - **DeviceReportedIp**: the IP sent by the UI device, as requested. Store it as untrusted, client-supplied data.
  - **ObservedIp**: taken from the connection, or from `X-Forwarded-For` only via configured trusted proxies (`ForwardedHeadersOptions`). Use it for rate limiting and abuse detection;
  - location (resolved city and country), Result and FailureReason.

  Event types: SignIn, SignOut, UserRegistered, ProfileUpdated, IdentityLinked, AuthorizationDenied, TokenValidationFailed.
- **Privacy:**
  - IP storage is configurable (`Full`, `Truncated` (IPv4 /24, IPv6 /48), or `HashedAndTruncated` using HMAC with a Key Vault secret).
  - Configurable retention, enforced by a background job.
  - Document the lawful basis and privacy notice (GDPR/DPDP-minded).
  - Never log tokens, Authorization headers, cookies or precise coordinates. Structured logs use `UserId` only.
- **Reusable events.** Write activity events through an **outbox table**, so future social features (activity feed, "last active", new-device alerts, notifications) can consume them later without touching the auth flow. Document the future publisher (for example Azure Service Bus); don't build it now.
- **Identity-provider logs.** Document exporting Entra External ID sign-in and audit logs to Log Analytics for authoritative identity-provider history. Don't copy them into PostgreSQL.
- **Metrics.** OpenTelemetry counters for registrations, sign-ins, sign-outs, auth failures, 409 conflicts and CSP reports, with no PII in tags.

## Content Security Policy and security headers (both services)

- **Configuration-driven headers.** Give `VedicZora.Authorize.Api` a `SecurityHeadersMiddleware` driven by validated `SecurityHeaders` options, and refactor `MyVedicDay.Api`'s middleware to the same configuration-driven model:
  - per-path policies: a strict JSON API policy (`default-src 'none'; frame-ancestors 'none'`), and a Swagger UI policy allowing Swagger's assets plus the External ID authority in `connect-src`/`frame-src` for the OAuth redirect;
  - **enforce** mode and **report-only** mode;
  - `report-to`/`report-uri` pointing at `/api/v1/security/csp-reports`, plus the `Reporting-Endpoints` header.
- **Other headers:** HSTS (outside Development), `X-Content-Type-Options`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`, `Cross-Origin-Resource-Policy`.
- **Report endpoint:** accepts `application/csp-report` and `application/reports+json`, with a small body limit and rate limiting. It logs a sanitized summary (directive, blocked origin, document path without query) and never echoes input.
- **Frontend guidance.** Document the recommended frontend CSP: nonce-based `script-src`, no `unsafe-inline`, and External ID and both API origins in `connect-src`.

## Configuration

Use strongly typed options validated at startup:

| Options | Contents |
|---|---|
| `EntraExternalId` | instance, tenant, client ID or audience, scopes, extension app ID |
| `Database` | PostgreSQL connection string from Key Vault or user secrets; never committed |
| `Registration` | account-linking policy, required fields |
| `ActivityLogging` | IP storage mode, retention days, HMAC key reference |
| `MyVedicDayApi` | base URL and timeouts for location resolution |
| `SecurityHeaders` | per-path CSP policies, report-only mode |
| `ForwardedHeaders` | trusted proxies and networks |
| `RateLimiting` | per-endpoint limits |

Also add a `compose.yaml` entry for local PostgreSQL.

## Tests

- **Unit:**
  - registration: new user, returning user, concurrent first calls, missing names, Apple private relay, same email from another provider with and without verified email;
  - email normalization and uniqueness; phone validation;
  - location adapter fallback;
  - IP handling: device-reported vs observed, trusted proxies, truncation and hashing;
  - claims enrichment; policy evaluation; CSP policy building.
- **Integration:**
  - `VedicZora.Authorize` with **Testcontainers PostgreSQL** and a **test authentication handler** issuing External ID-shaped claims; no real identity providers. Cover `/userinfo` 201 then 200, `/me` PATCH, session start and end events persisted, 401/403/409 Problem Details, headers per path, the CSP report endpoint, and migrations applying cleanly.
  - `MyVedicDay.Api`: anonymous endpoints unchanged; the protected endpoint returns 401 without a token, 403 with `vz_status = Suspended`, and 200 with a valid test token.
- **Architecture:** dependency tests in both solutions, including "no cross-references between `MyVedicDay.*` and `VedicZora.*`".

## Documentation

- ADRs for:
  - `VedicZora.Authorize` as a separate service and its claims contract;
  - Entra External ID as the single issuer with federated social providers;
  - token enrichment vs authorization check (option A vs B);
  - PostgreSQL schema and email uniqueness;
  - registration and account linking;
  - device-reported vs observed IP, privacy and retention;
  - the outbox for activity events;
  - the CSP strategy.
- Update `docs/architecture/README.md` in MyVedicDay (authentication section, system diagram showing both services, phases), and write a README for `VedicZora.Authorize` covering run, configuration, endpoints and tests.

## Process (mandatory, per §35 of the instructions)

1. **Design first; wait for my approval before implementing.** Produce:
   - requirements analysis;
   - a system diagram (clients, Entra External ID, `VedicZora.Authorize`, `MyVedicDay.Api`, PostgreSQL);
   - sequence diagrams (first social sign-in with registration; returning sign-in; logout; `MyVedicDay.Api` request authorization under option A and option B; same email from a second provider);
   - the project and dependency diagram;
   - the PostgreSQL schema (tables, keys, unique indexes, retention);
   - the claims contract; API contracts;
   - the configuration, security and CSP design;
   - the testing strategy;
   - NuGet packages (`Microsoft.Identity.Web`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `libphonenumber-csharp`, `Testcontainers.PostgreSql`, others as justified);
   - implementation phases.
2. **List the open decisions for me:** option A, B or hybrid; repository placement; IP storage mode and retention; the auto-link policy; Apple availability in our tenant.
3. **After approval, implement phase by phase:** contracts → implementation → DI registration → configuration and validation → logging → exception mapping → tests → build. After every phase, confirm both solutions build and all existing MyVedicDay tests still pass.
4. **If a request conflicts with the architecture,** say so, explain why, and implement the compliant alternative. Never weaken boundaries to make something compile.
