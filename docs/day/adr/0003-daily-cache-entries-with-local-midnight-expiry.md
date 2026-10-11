# ADR-0003: Daily cache entries keyed by local date, expiring at local midnight

- Status: Accepted
- Date: 2026-09-28

## Context

Requests cover today, tomorrow, 10 days, a month or a year for arbitrary locations worldwide. Users sit in different
time zones from each other and from the servers. Caching whole responses per view would duplicate data (today is
inside the 10-day, month and year views), and a single global expiry would serve the wrong "today" in most time
zones.

## Decision

- Cache one entry per **location + local date + tradition + calculation version**:
  `panchang:{lat}:{lon}:{zone}:{localDate}:{tradition}:{version}`. Coordinates are rounded to 2 decimals.
- Compose multi-day responses from daily entries, reading keys concurrently and calculating only misses.
- An entry for local date *D* expires at the start of *D + 1* in the location's IANA zone, computed with NodaTime
  (`AtStartOfDay`) and converted to UTC. Dates already over get a 1-hour lifetime, and all entries are capped at
  35 days.
- Coalesce concurrent misses per key within an instance, with no distributed lock.
- Treat cache failures as misses.

## Consequences

- Today's entry disappears at the location's midnight. After midnight a different key is used anyway, so a stale
  day cannot be served even if expiry lags.
- DST transitions and zones that skip midnight are handled by the time zone database, not by offset arithmetic.
- A year request with a cold cache performs 365 small writes. That is acceptable, and later requests for any
  sub-range are served from cache.
- Changing algorithms or rule tables requires bumping `PanchangCalculationVersion.Current`.
