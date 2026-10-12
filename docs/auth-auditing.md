# User registration and session auditing

`POST /api/v1/auth/login` and the existing `POST /api/v1/auth/userinfo` use the same business workflow. Both require the existing bearer authentication and API scope, and return exactly `id`, `name`, and `email`. Identity comes from the validated issuer plus the provider's stable ID: the configured directory ID claim for Entra, and `sub` for Google, Apple, and Facebook. Provider enablement and token validation policies are unchanged.

The first request registers an active account with a database-generated UTC creation time. Later requests synchronize name and email from the same resolved claims used by UserInfo, while preserving internal ID, creation time, and active status. Changes update UpdatedUtc; identical profile requests leave it unchanged. Inactive accounts receive 403 and no profile update or login audit event. The response keeps the existing token-derived profile fields. An unavailable real name resolves to null; email can fall back to an email-shaped preferred_username. Existing column length limits still apply when saving unusually long claims.

EF Core handles registration and auditing in one PostgreSQL transaction. Unique indexes prevent duplicate accounts under concurrent registration and duplicate audit events across requests or API replicas. `myvedicstar.loginaudit` contains `AuditId`, `UserId`, `Time` (UTC), and `Type` (1 for login, 99 for explicit logout), plus the existing client IP and a session hash. Audit history is preserved, and foreign keys prevent deleting referenced users.

## Session identity

If supplied, `X-Auth-Session-Id` must be a UUID generated once for each login session. Send the same value to login, UserInfo, and logout, including after refreshing tokens. It is an idempotency key scoped to the authenticated account, never a source of user identity. Otherwise, the API uses the validated token's `sid`, then `auth_time`, then `iat`. Only the hash of that identifier is stored; access tokens, refresh tokens, and passwords are never stored in user or audit rows.

For tokens containing `sid` or stable `auth_time`, existing clients need no changes to avoid duplicate UserInfo audits across refreshes. If neither is available, the `iat` fallback deduplicates repeated calls using the same token, but renewed tokens may create another login event. Those clients should use the optional session header to maintain accurate session history.

Call `POST /api/v1/auth/logout` with a valid bearer token before clearing client credentials. It returns 204 and appends one logout event per session; retries return 204 without another event. Unknown registered accounts receive 404. This endpoint audits explicit logout; the client must continue its existing provider sign-out and credential cleanup. It does not revoke bearer tokens or infer logout from token expiration, browser closure, or lost connectivity.

## Database configuration and migration

`FeatureFlags:UserPersistence` is enabled in Star defaults and the production deployment manifest. Production enables `Database:UseManagedIdentity`, builds its connection from `Database:Host`, `Name`, `Username`, `Port` (default 5432), and `SslMode`, and uses Entra tokens as connection passwords. `Database:ManagedIdentityClientId` selects a user-assigned identity, falling back to the existing `AZURE_CLIENT_ID`; without either, the system-assigned identity is used. TLS is required. The identity must be registered as a PostgreSQL Entra principal and granted schema/table access. Tokens are acquired only when opening physical connections and cached/refreshed by the Azure SDK; they are never stored in configuration or user/audit tables.

For local password authentication, leave `Database:UseManagedIdentity=false` and supply `Database:ConnectionString` through user secrets or `Database__ConnectionString` through a secret. Development applies pending EF migrations on startup. Production must apply them before deploying this version. The following command is for the password-based migration path; the managed-identity migration job must also acquire an Entra token and use an identity with migration permissions.

```powershell
dotnet ef database update --project src/MyVedicStar.Authorize.Data --connection $env:Database__ConnectionString
```

The migration renames `UserLogin` to `loginaudit`, preserving its rows and timestamps. Existing accounts are assigned the current production Entra tenant issuer, `https://cbece845-e124-4de7-89d3-96a71abb7b83.ciamlogin.com/cbece845-e124-4de7-89d3-96a71abb7b83/v2.0`. Deployments with existing accounts from a different tenant must correct that backfill before applying the migration. Downgrading after adding accounts with matching external IDs across providers requires resolving those identities first, because the old schema supported only globally unique external IDs.

MyVedicDay source, controllers, and contracts are unchanged. Web/mobile UserInfo callers retain the same URL, method, authentication, and JSON response. No web or mobile source projects are present here for device/browser end-to-end testing.

## Validation

Run the Authorize unit/integration suites and the shared-host integration suite. Set `MYVEDICSTAR_TEST_POSTGRES` to an admin connection for a disposable PostgreSQL instance to exercise migrations, upgrade preservation, foreign keys, UTC timestamps, inactive accounts, transaction rollback, and concurrent registration/audit idempotency. Database tests create and remove their own databases.
