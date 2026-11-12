# Muhurta / Good Time Business Requirements

## Module scope
This module covers the calculation of auspicious windows, activity-specific suitability, and daily summaries used by the Good Time Finder experience.

## Business objectives
The system shall help users identify favorable time windows for planned activities while respecting rule conflicts, tradition settings, and user-selected context.

## Functional requirements
### 1. Activity-based evaluation
1. The system shall allow the user to choose an activity and a date range for a Good Time search.
2. The system shall support activity lists such as general planning, business, travel, property, vehicle, agreement, education, puja, wedding, housewarming, and other custom descriptions.
3. The system shall evaluate candidate windows based on the selected activity and its rules profile.

### 2. Good Time result generation
1. The system shall classify intervals as Highly Favorable, Favorable, Mixed, Caution, or Avoid based on activity-specific rules and conflict matrices.
2. The system shall only treat `isGoodTime = true` as a valid Good Time when the interval is favorable and free of conflicts, with minimum duration bounds.
3. The system shall return empty good-time results with a reason when no qualifying windows exist.
4. The system shall preserve overlapping favorable and unfavorable intervals instead of silently deleting conflicts.

### 3. Explanations and summaries
1. The system shall provide narratives that explain why a result is favorable or mixed.
2. The system shall include day summaries for date-based tiles and calendar views.
3. The system shall return overlap messages, conflict explanations, and rule reasoning for each result.

### 4. Support for optional inputs
1. The system shall support optional Tara Bala input based on birth Nakshatra where applicable.
2. If birth Nakshatra is omitted, the system shall provide generic results without Tara Bala adjustment.
3. The system shall avoid calculating unsupported values when required birth context is missing.

## Business rules
1. The system shall not present a universal “good day” without considering the selected activity, date, location, and tradition.
2. Good Time results shall be activity-specific; one result set shall not be reused across unrelated activity decisions.
3. Conflicting windows shall remain visible to the user rather than being silently suppressed.
4. A favorable result shall require the underlying rule engine to confirm both merit and conflict absence.

## Non-functional requirements
1. Good Time evaluation shall reuse the same Panchang calculation foundation and tradition data as the base daily data.
2. The system shall support API response formats that are structured and versioned.
3. The system shall support caching of neutral result sets and filtered views without caching user-specific, sensitive profile data.

## External dependencies
- Panchang calculation engine
- tradition-rule data and activity rule catalogs
- localization and explanation text resources
- shared caching and result invalidation services

## UI consumer requirements
1. The authenticated Find a Good Time experience shall obtain its activity list and birth-Nakshatra choices from the API rather than maintaining a second hard-coded catalog. It shall request a day evaluation for the selected local date, activity, location, and tradition.
2. The client shall offer day and month planning views. Month/calendar tiles shall use the API day-summary statuses; selecting a date shall load that date's full evaluation before showing detailed periods.
3. Recommended-time cards shall show only segments the API marks as qualifying Good Times. Favorable periods excluded because of a conflict, and Mixed/Caution/Avoid periods, shall remain visible with the API's reasons and conflict information; the client shall never infer or override status from translated labels.
4. The "next best times" view shall display the API-ranked future options and their dates. Empty results shall display the API's no-good-time reason/message rather than substitute a recommendation.
5. A "why this time" explanation is a separate, optional request. It shall not delay or alter the structured recommendation and shall be rendered with its source/fallback state when provided.
6. When the user supplies a supported birth Nakshatra, the client shall pass it as evaluation context. Omission shall leave generic results; the client shall not invent or infer birth information.
7. Date, activity, optional custom activity text, view, and relevant selection state should be linkable through the UI route where supported. The server remains authoritative for validation, statuses, ranking, and calculation.
8. Mobile layouts may use a selection-then-results flow, but must preserve the same request inputs, result meaning, and access requirements as web.
9. Loading, throttling, calculation timeout, dependency failure, and no-qualifying-time outcomes shall remain distinct and actionable. Optional explanation or AI-service failure shall not hide a successful evaluation.
