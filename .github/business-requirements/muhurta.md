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
