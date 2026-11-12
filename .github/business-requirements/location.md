# Location Business Requirements

## Module scope
This module covers geographic resolution, timezone context, coordinate validation, and saved location behavior used by Panchang, Muhurta, and related features.

## Business objectives
The system shall resolve a user's location accurately enough for time-sensitive Vedic calculations while preserving privacy and supporting user-controlled fallback choices.

## Functional requirements
### 1. Location resolution
1. The system shall resolve locations by place name, city/state/country, postal code, coordinates, or saved location values.
2. The system shall support browser/device geolocation only when the user opts in.
3. The system shall allow explicit place selection to override ambiguous input.

### 2. Data validation
1. The system shall validate latitude and longitude ranges before use in calculations.
2. The system shall validate timezone identifiers and coordinate-to-zone consistency.
3. The system shall reject invalid or inconsistent location parameters with structured validation errors.

### 3. Fallback and selection rules
1. The system shall use the detected location when permission exists and the source is valid.
2. If no permission is available, the system shall use the saved/default location or a deliberate fallback choice.
3. The system shall distinguish user-selected, device-derived, saved, and fallback-derived values.

### 4. GeoNames integration
1. The system shall use GeoNames as a data source for place lookup and geocoding support.
2. The system shall protect upstream provider messages and credentials from exposure in API results.
3. The system shall surface configuration-specific errors when the provider rejects a username or the service is unavailable.

## Business rules
1. The system shall not assume a precise location without explicit user input or valid geolocation consent.
2. The system shall keep location calculation independent from the UI layer and prohibit client-side-only validation as the sole enforcement mechanism.
3. Location selection shall be consistent across all downstream calculations and not vary silently by endpoint.

## Non-functional requirements
1. The system shall design API responses to represent location failures without exposing provider internals.
2. The system shall support caching of localized or normalized location metadata where safe.
3. The system shall maintain auditable computation provenance for timed events derived from selected locations.

## External dependencies
- GeoNames services
- timezone libraries
- geolocation inputs and saved user preferences
- shared cache and validation services

## UI consumer requirements
1. API-backed location lookup and calculation routes require the authenticated application access described in `authorization.md`; visitors may see informational content but shall not issue protected location requests.
2. The user shall explicitly choose the current-location action before the UI requests device geolocation. Permission denial or lookup failure shall leave manual place search and coordinate entry available; late geolocation results shall not overwrite a newer manual selection.
3. Location choices shall retain their source (such as current/device, place search, or coordinates) and use the resolved timezone consistently for date-based requests. A nearby-city label shall not silently replace the selected coordinates.
4. GeoNames lookup shall be performed through the API, not directly from the browser or native application. Search suggestions shall be bounded, debounced, cancellable, deduplicated, and selectable by keyboard or touch as appropriate.
5. Resolve a timezone after selecting a place when needed. Do not silently substitute a timezone when lookup fails; permit selection only when the location has a usable timezone for downstream calculations.
6. On GeoNames or quota failure, keep manual entry available and present a safe, retryable error. A failed reverse-geocoded label may be omitted if valid selected coordinates and timezone remain available.
7. Anonymous recent locations and account-saved locations are different data sets. Recents remain device-local; account-saved locations are retrieved and managed through Settings, are scoped to the signed-in account, and must not be uploaded merely because a user signs in.
8. Disclose that lookup terms and selected coordinates are sent through the API to the location provider when that feature is enabled. Do not include precise location or search text in telemetry or ordinary logs.
