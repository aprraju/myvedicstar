# Traditions Business Requirements

## Module scope
This module provides the supported tradition catalog and the convention metadata consumed by Panchang and Muhurta features.

## Business objectives
The system shall let users select a regional tradition and ensure each feature applies its own configured convention consistently without conflating tradition with geographic location.

## API requirements
### `GET /api/v1/traditions`
1. Return the supported tradition keys, localized display names, and region metadata needed to populate a picker.
2. Accept an optional language preference and return the selected/fallback language.
3. Keep stable keys suitable for API requests and saved preferences; localized labels are display values, not identifiers.

### `GET /api/v1/panchang/traditions`
1. Return the configured Panchang display profile for each supported tradition, including which timings are shown and their display order.
2. Treat the response as authoritative; clients shall not hard-code separate period lists that can diverge from backend configuration.
3. Accept language negotiation for descriptions and labels while keeping tradition keys and rule semantics stable.

## Business rules
1. Tradition controls interpretive/display conventions; location, coordinates, and timezone control astronomical and local-date calculations.
2. A tradition selected for one feature shall be passed explicitly to that feature; its catalog does not imply identical rule behavior across Panchang, festivals, or Muhurta.
3. An unsupported tradition shall be rejected or handled by the documented default, never silently mapped to a different tradition.
4. User-selected temporary tradition and account-saved default tradition are separate; only an explicit Settings save persists the default.

## UI consumer requirements
1. Web and mobile shall populate their tradition selectors from the API catalog and present localized names while retaining canonical keys.
2. Location and tradition selection shall be independent. For example, a user may use a regional tradition with coordinates in another country.
3. The UI shall apply the account default on sign-in when present, otherwise the application/default selection, without letting a late settings response replace a newer temporary selection.
4. API errors loading the catalog shall be visible and shall not be represented as an empty successful list. Settings shall not allow saving an unknown catalog key.

## Non-functional requirements
1. Catalog and profile reference responses shall be stable, versioned, bounded, and cacheable according to their language/configuration inputs.
2. New traditions should be addable through configuration/data and feature-specific rule support without requiring clients to ship a copied list of rule details.
