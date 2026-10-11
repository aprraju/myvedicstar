# ADR-0007: Location-formatted display times alongside ISO 8601

- Status: Accepted
- Date: 2026-09-29

## Context

Users expect dates and times in their local convention: `09/28/2026 09:37:10 CDT` in the US and
`28/09/2026 09:37:10 IST` in India. The API returned only ISO 8601 values, such as `2026-09-28T09:37:10-05:00`.

Replacing ISO values with display strings would conflict with the architecture:

- Clients could no longer sort, compare or do arithmetic reliably.
- Zone abbreviations are ambiguous (IST is India, Ireland or Israel; CST is US Central, China or Cuba).
- Formatting is presentation, not data.

## Decision

- Keep every ISO 8601 value and **add** a `…Display` sibling (`dateDisplay`, `sunriseDisplay`, `startDisplay`,
  `endDisplay`, `localTodayDisplay`). `displayFormat` states the pattern and the country that chose it.
- The country comes from the resolved city, else the time zone. The date pattern is chosen from configured
  overrides (US `MM/dd/yyyy`, IN `dd/MM/yyyy`), then the country's CLDR convention, then a default.
- The time is 24-hour by default (configurable). The zone abbreviation is taken from the tz database for each instant
  (DST-correct); zones with numeric abbreviations show `UTC±hh:mm`.
- Formatting is applied in the Business layer after the cache, with the location's zone and country.

## Consequences

- UIs display strings as-is and keep ISO values for logic.
- Responses grow by one short string per time value.
- A time in September in Texas displays as **CDT**, not CST, because daylight time is in effect. This is correct.
- CLDR data comes from ICU. In invariant-globalization deployments (for example trimmed containers), countries
  without an explicit pattern fall back to `DefaultDatePattern`. Adding explicit country patterns avoids this.
