# Panchang Business Requirements

## Module scope
This module covers the daily Panchang calculations and related outputs used by the MyVedicDay and MyVedicStar API surfaces.

## Business objectives
The system shall provide location-aware Panchang data for a user-selected date and place, with timezone-aware, rule-aware, and tradition-aware calculations.

## Functional requirements
### 1. Date and date-range support
1. The system shall support daily Panchang queries for today, tomorrow, a specific date, a range of dates, a calendar month, and a calendar year.
2. The system shall resolve daily values from the user's local date and the selected location's timezone.
3. The system shall support requested ranges that include today, prior-day spillover, and next-day spillover when events cross midnight.

### 2. Location-aware input handling
1. The system shall accept a location using validated latitude/longitude, timezone, place name, city/state/country, postal code, or a saved location reference.
2. The system shall validate latitude within [-90, 90] and longitude within [-180, 180].
3. The system shall validate timezone identity and coordinate-to-zone consistency.
4. The system shall support opt-in geolocation and fallback order: device location, saved default, or explicit fallback selection.

### 3. Daily Panchang data
1. The system shall calculate and return sunrise, sunset, Tithi, Nakshatra, Yoga, Karana, Rahu Kalam, Yamagandam, Gulika Kalam, Durmuhurtam, Varjyam, Abhijit Muhurtam, Brahma Muhurta, Amrit Kalam, and similar configured periods when applicable.
2. The system shall compute these values using astronomical inputs, sunrise/sunset conventions, ayanamsa, and the selected tradition rules.
3. The system shall include favorable and unfavorable periods, overlap information, and supporting explanations where relevant.

### 4. Event precision and correctness
1. The system shall treat events as time intervals with start and end instants and explicit timezone metadata.
2. The system shall return both local and UTC timing values for each occurrence.
3. The system shall support multiple transitions in a single local day and shall not assume one fixed Tithi or Nakshatra per date.
4. The system shall handle DST changes, ambiguous local times, invalid local times, and cross-midnight periods correctly.

### 5. Tradition and rule handling
1. The system shall separate astronomical facts from interpretation layers.
2. The system shall support configurable tradition strategies and rule versions.
3. The system shall include calculation version, tradition, ayanamsa, sunrise definition, and rule provenance in results where required for auditability.
4. The system shall allow output differences between traditions without losing the original calculation provenance.

### 6. Data contract and output hygiene
1. The system shall produce explicit response DTOs with consistent field naming and timestamp semantics.
2. The system shall expose calculation version and provenance information in outputs.
3. The system shall include quality flags and interval metadata where applicable.
4. The system shall return RFC 7807-style problem details for invalid or unsupported requests.

## Business rules
1. The daily Panchang shall be computed from astronomy and location, not from UI-side assumptions.
2. The system shall not infer a single daily Panchang interpretation without considering location, timezone, and tradition.
3. Good-time or guidance calculations shall be distinct from the base astronomical event values.
4. Transition events shall be computed numerically rather than by coarse daily sampling.

## Non-functional requirements
1. The system shall support API-based access with versioned contracts and OpenAPI documentation.
2. The system shall support cancellation and bounded timeouts for long-running queries.
3. The system shall use cache-backed performance optimization while preserving result correctness.
4. The system shall support future extension for additional traditions and calculation engines without changing the core calculation pipeline.

## External dependencies
- astronomical calculation libraries and ephemeris data
- location resolution and GeoNames data
- timezone and locale resolution services
- shared caching infrastructure

## UI consumer requirements
1. Authenticated Today and dashboard experiences shall use the selected location's timezone and the API's local-date fields. The client shall refresh Today when that location crosses local midnight; it shall not use the device or server date as a substitute.
2. Calendar views shall support loading daily and month data for the selected location and tradition. A selected calendar date shall remain a local calendar date when sent to the API.
3. Dashboard timelines shall render the Panchang day from sunrise to the next sunrise, including midnight and daylight-saving transitions. They shall preserve overlapping intervals and conflicts, and expose the underlying event details when expanded.
4. Summary cards, ratings, Panchang limbs, event transitions, and favorable/unfavorable periods shall be derived from the API response. The UI shall not independently calculate a Panchang, infer a Tithi transition, or promote a period to a favorable recommendation.
5. Calendar cells may use API-provided compact or short Tithi names where available, but detail views shall retain the full name and older responses shall have a safe full-name fallback.
6. API loading, validation, unavailable-location, and dependency failures shall be shown as unavailable/error states with appropriate retry behavior. The UI shall not fabricate calendar values or return a success-shaped empty day.
7. Guest home screens are informational. API-backed Panchang views require the authenticated application access described in `authorization.md`.
