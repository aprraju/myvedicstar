# ADR-0002: Analytical ephemeris (Meeus) behind an adapter

- Status: Accepted
- Date: 2026-09-28

## Context

Panchang elements need solar and lunar longitudes accurate enough for minute-level transition times. The options:

1. **Swiss Ephemeris.** Sub-arc-second accuracy and the de facto standard for Jyotish software. It is AGPL or
   requires a commercial licence, is native C (a P/Invoke wrapper or managed port is needed), and ships data files.
2. **JPL DE440.** Authoritative, but needs large data files and a reader.
3. **Analytical series from Meeus, "Astronomical Algorithms".** Pure C#, no data files, and no licensing questions.
   About 0.01° for the Sun and 10″ for the Moon.

The lunar error dominates: 10″ corresponds to about 20 seconds of Moon motion, and 0.01° of solar error is about
1 minute of Tithi time in the worst case. Published almanacs display times to the minute.

## Decision

Use the Meeus series (`MeeusEphemeris`) behind the internal `IEphemeris` interface. Validate it against the book's
worked examples, published sunrise/sunset times and known new and full moon instants (within ±2 minutes).

## Consequences

- No licensing or deployment constraints; the calculation engine stays a pure library.
- Accuracy is sufficient for a published Panchang but not for arc-second astronomy. Kundali features that need
  precise planetary positions (outer planets, Lagna degrees) will need a higher-precision `IEphemeris`
  implementation. That is a new adapter, not a redesign.
- The Espenak–Meeus ΔT model limits verified accuracy to 1900–2100, so requests outside that range are rejected.
