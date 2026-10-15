# ADR-0002: Analytical ephemeris (Meeus) behind an adapter

- Status: Accepted
- Date: 2026-09-28

## Context

Panchang elements need solar and lunar longitudes accurate enough for minute-level transition times. The options:

1. **Swiss Ephemeris.** Sub-arc-second accuracy and the de facto standard for Jyotish software. It is AGPL or
   requires a commercial licence, is native C (a P/Invoke wrapper or managed port is needed), and ships data files.
2. **JPL DE440.** Authoritative, but needs large data files and a reader.
3. **Analytical series from Meeus, "Astronomical Algorithms".** Pure C#, no data files, and no licensing questions.
   About 1″ for the Sun (the truncated VSOP87 Earth series, chapter 32 and appendix III) and 10″ for the Moon.

10″ of lunar error corresponds to about 20 seconds of Moon motion. Published almanacs display times to the minute.

The Sun's error matters most for Sankrantis, because the Sun moves only about 1° a day. The low-accuracy solar
formula of Meeus chapter 25 (about 0.01°) put equinoxes up to 11 minutes off and Sankrantis 10–20 minutes off. That is
enough to move a Sankranti across sunset or noon, and so move Makar Sankranti, Puthandu, Vishu or a solar-month
boundary by a day. The VSOP87 series reaches the published equinox and solstice instants within half a minute.

## Decision

Use the Meeus series (`MeeusEphemeris`) behind the internal `IEphemeris` interface: VSOP87 (truncated) for the Sun
and ELP-2000/82 (truncated) for the Moon. Validate them against the book's worked examples, published equinox and
solstice instants (±1.5 minutes), published sunrise/sunset times and known new and full moon instants (±2 minutes).

## Consequences

- No licensing or deployment constraints; the calculation engine stays a pure library.
- Accuracy is sufficient for a published Panchang but not for arc-second astronomy. Kundali features that need
  precise planetary positions (outer planets, Lagna degrees) will need a higher-precision `IEphemeris`
  implementation. That is a new adapter, not a redesign.
- **Moonrise** (`IMoonEventCalculator`) uses the Moon's latitude and distance (Meeus table 47.B and the distance column
  of 47.A, validated against example 47.a) and the standard altitude h₀ = 0.7275·π − 0°34′. It matches published
  New Delhi moonrise times within 3 minutes. The search samples hourly, so near-polar grazing moonrises can be missed.
- **Planets** (`IPlanetEphemeris`) come from Standish's JPL Keplerian elements (table 1, fitted to 1800–2050), not from
  VSOP87: 0.1–0.2° for Jupiter and Saturn, better for the inner planets. This is ample for combustion orbs of 8–17°;
  published 2023–2026 conjunctions and oppositions are reproduced within hours. Longitudes are in the J2000 frame, so
  sidereal planet positions for Kundali still need precession and a more precise adapter. After 2050 the elements
  degrade slowly.
- The Espenak–Meeus ΔT model limits verified accuracy to 1900–2100, so requests outside that range are rejected.
- The VSOP87 series costs about 0.6 ms more per Panchang day than the chapter 25 formula (about 1.3 ms in all). This is
  paid only on a cache miss.
- Sankranti instants still come out about 6–11 minutes earlier than the moments published by Drik Panchang (for example
  Makar Sankranti 2026: 15:07 vs 15:13 IST), although equinoxes match. This points to a difference of about 20″ in the
  Lahiri ayanamsa definition rather than to the Sun. It has not moved any festival date in the 2021–2026 backtest. It
  needs confirming against an authoritative ayanamsa value before the ayanamsa is changed.
