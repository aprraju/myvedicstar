# Authorization and Identity Business Requirements

## Module scope
This module covers login, logout, user registration, profile synchronization, and identity-related server-side enforcement.

## Business objectives
The system shall securely authenticate users, maintain user identity records, and preserve a verifiable audit trail of sign-in and sign-out activity.

## Functional requirements
### 1. Authentication and user identity
1. The system shall authenticate requests using the existing bearer-token model and assigned API scopes.
2. The system shall resolve the user identity from validated claims and the configured identity provider.
3. The system shall treat the provider-issued subject or stable identifier as the canonical identity source for account matching.

### 2. Registration and profile upsert
1. The system shall create or update a user record on first valid login or userinfo request.
2. The system shall preserve internal user identity values and creation timestamps while synchronizing updates to name and email fields.
3. The system shall reject inactive accounts with a structured authorization failure response.
4. The system shall never clear a stored display name when the current token contains no usable value.

### 3. Profile synchronization
1. The system shall allow profile update requests for display name and other user metadata.
2. The system shall trim and validate profile values before storing them.
3. The system shall apply pending synchronization flags when values are updated to the external identity provider.
4. The system shall retry synchronization safely without overwriting user-edited values.

### 4. Session and audit logging
1. The system shall log login and logout events with timestamps, user reference, session hash, client IP, and event type.
2. The system shall reject duplicate audit writes across concurrent requests or replicas through unique constraints and transactional handling.
3. The system shall store only a session identifier hash and not raw access tokens, refresh tokens, or passwords.

### 5. Logout behavior
1. The system shall support explicit logout handling via the logout API.
2. The system shall append a logout audit event once per session and return success for idempotent retry calls.
3. The system shall not treat browser closure or token expiration as a logout event by itself.

## Business rules
1. The system shall not rely on caller-supplied identity values to establish a trusted user identity.
2. The system shall maintain database-backed authoritative user data rather than reconstructing user identity from untrusted input.
3. Authorization failures shall be reported as business-level outcomes rather than technical exceptions to callers.
4. The system shall protect sensitive session and identity data from being stored in plain text.

## Non-functional requirements
1. The system shall support PostgreSQL-backed persistence for user and login-audit data.
2. The system shall support managed identity and secure connection handling for database access.
3. The system shall preserve audit history and enforce referential integrity for user records.
4. The system shall support performance-safe operations for concurrent login and profile events.

## External dependencies
- identity provider validation and claims issuance
- PostgreSQL database for user and audit tables
- managed identity or secret-backed database connectivity
- security middleware and token-validation services

## UI consumer requirements
1. Every application API request shall carry a valid bearer token with the required API scope, except explicitly anonymous operations such as the documented contact-submission endpoint and operational health/readiness probes. A client shall not treat a rate-limit policy name as anonymous access.
2. Signed-out web and mobile users may view informational landing/content pages but shall not receive API-backed Today, location, settings, profile, calendar, festival, or Muhurta data. Protected-route navigation shall preserve only a validated same-origin return path through the hosted identity-provider sign-in flow.
3. Sign-in shall use the configured hosted identity provider and established OIDC/OAuth flow; the client shall never collect or persist provider passwords or client secrets. Registration, when offered, is an identity-provider flow rather than a caller-supplied account-creation API.
4. The frontend shall use the login endpoint as the account-registration/profile synchronization and session-audit entry point. It shall not make a second UserInfo call solely to retrieve the same login payload. Opening Profile must be read-only and must not create an account or login audit.
5. The Profile experience shall read and edit the supported fields: first name, last name, display name, street address lines, country, state/province, city, postal code, email, and phone number. It shall submit only changed, validated fields with the current revision; it shall preserve the user's edits on validation, network, or concurrency failure.
6. A profile save is successful when the database update succeeds even if external name synchronization is pending. The UI shall clearly distinguish saved-with-sync-pending from failed-save and provide a safe retry for pending synchronization.
7. Account identity and private data shall be isolated by the validated authenticated account. No client-supplied user ID may select which profile is read or changed. Inactive-account and scope errors shall be presented as access failures, not silently converted to guest mode.
8. Login, session restoration, profile, and logout states shall remain consistent across web/mobile. Logout shall clear account-scoped client state and credentials after the explicit audit request, while explaining that provider sign-out is a separate client operation.
