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
