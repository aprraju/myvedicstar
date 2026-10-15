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
  of 47.A, validated against example 47.a). It follows Drik Panchang's definition: the centre of the disc, seen from
  the observer, on the geometric horizon (h₀ = +π, no refraction). This reproduces Drik's twelve 2025 Sankashti
  moonrise times for New Delhi within half a minute. The almanac definition (upper limb with refraction) would be
  about 4 minutes earlier. The search samples hourly, so near-polar grazing moonrises can be missed.
- **Planets** (`IPlanetEphemeris`) come from Standish's JPL Keplerian elements (table 1, fitted to 1800–2050), not from
  VSOP87: 0.1–0.2° for Jupiter and Saturn, better for the inner planets. This is ample for combustion orbs of 8–17°;
  published 2023–2026 conjunctions and oppositions are reproduced within hours. Longitudes are in the J2000 frame, so
  sidereal planet positions for Kundali still need precession and a more precise adapter. After 2050 the elements
  degrade slowly.
- The Espenak–Meeus ΔT model limits verified accuracy to 1900–2100, so requests outside that range are rejected.
- The VSOP87 series costs about 0.6 ms more per Panchang day than the chapter 25 formula (about 1.3 ms in all). This is
  paid only on a cache miss.
- **Calibration to Drik Panchang.** Drik Panchang (`drik` tradition) is the reference, and two of its conventions
  differ from the bare astronomy:
  - Its Lahiri ayanamsa is 24.1″ larger than the Government of India 1956 definition (it publishes 24.224241° for
    2025-10-20). `LahiriAyanamsa` adds that constant, so Sankrantis are no longer about 10 minutes early.
  - Its Tithi, Karana and Nakshatra times correspond to a lunar longitude 28″ ahead of ELP-2000/82, about a minute
    earlier. The series itself reproduces USNO's 2025 new and full moons with a mean error of 0.6 s. `MeeusEphemeris`
    adds the offset: with it, 24 of 26 published Drik tithi minutes for 2025 are reproduced exactly, against 0
    without. Lunar phases therefore come out about 55 seconds before the astronomical instant, by design.
  - Verified against Drik Panchang data (2025): the full Panchang of 20 October in New Delhi (22 timings, all to the
    minute), every Ekadashi with its Gauna date in New Delhi (25 + 5) and Georgetown, Texas (25 + 4; one Smarta date
    differs, where the Ekadashi begins 54 seconds before sunrise), and every Sankashti Chaturthi with its moonrise.
  - Calibration changed 8 of about 4,450 festival occurrences in a 2021–2026 backtest of four cities. Each was a
    tithi or Sankranti boundary within about a minute of the deciding moment.
