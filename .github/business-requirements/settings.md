# Settings Business Requirements

## Module scope
This module covers user settings, profile preferences, and configuration-related behavior that is separate from the core calculation engines.

## Business objectives
The system shall allow the user to configure personal preferences and maintain a persisted settings model without breaking the existing API contracts.

## Functional requirements
### 1. Settings retrieval and persistence
1. The system shall support retrieval of user and application settings values.
2. The system shall support updates to settings values, including location, display name, and user preference data.
3. The system shall persist these changes in a consistent and versioned data model.

### 2. Configuration behavior
1. The system shall support environment-scoped defaults that are explicitly overridden by environment variables and deployment configuration.
2. The system shall preserve production deployment behavior and keep existing app configuration keys and names without unnecessary breaking changes.
3. The system shall allow infrastructure configuration to differ between Day and Star environments while keeping code-level contracts stable.

### 3. Preference integrity
1. The system shall store authoritative user preferences in the database rather than deriving them from transient HTTP input alone.
2. The system shall avoid storing or exposing secrets in settings responses.
3. The system shall ensure that user-edited values remain authoritative when syncing external identity/state changes later.

## Business rules
1. App configuration shall be separated from user-specific settings and shall not be stored in the same domain object model unless explicitly required.
2. User preferences shall be safe to update without affecting core calculation behavior unless the user explicitly changes calculation-related inputs.
3. Settings updates shall follow validation rules and allow for future multi-language or multi-region configuration without a breaking contract change.

## Non-functional requirements
1. Setting values shall be accessible through API endpoints and business-layer abstractions.
2. The system shall support safe rollout and backward compatibility for existing settings contracts.
3. Settings changes shall be auditable where they affect user identity or account state.

## External dependencies
- configuration providers and environment variables
- database-backed settings storage
- shared authorization and profile contracts
- future localization and user-preference services

## UI consumer requirements
1. After sign-in or account restoration, load that account's defaults and saved locations. Apply defaults to the existing location/tradition state only once; a background refresh or late response shall not overwrite a user's newer temporary selection.
2. Temporary location and tradition selections shall affect the active experience immediately but shall not change saved defaults. Only an explicit Settings save may update default tradition or default location.
3. Adding a location from the authenticated location chooser shall save it to the account without silently changing the default. Existing saved locations shall not be re-inserted on repeated selection.
4. Settings shall offer the supported traditions and the user's saved locations, with explicit choices to use application/default fallback. It shall not present device-local recent locations as if they were account-saved.
5. The saved-location list shall not be silently truncated to the small device-recent limit. Users shall be able to remove an owned saved location; removing the default shall clear it and apply the existing location fallback, while deleting another location shall preserve the active/default selection.
6. Show loading, empty, save-success, validation, conflict, unavailable, and retry states. Do not report a save or delete as complete before the authoritative API mutation succeeds.
7. Settings and saved-location reads/writes shall be account-scoped, no-store, and excluded from persistent public calculation caches. Logout and account changes shall clear private query state and discard late responses from the previous account.
8. Refresh from the server on a new device/session so account preferences are portable. Cross-device changes may appear on next login or explicit refresh; realtime synchronization is not required.
