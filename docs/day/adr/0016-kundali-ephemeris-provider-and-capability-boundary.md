# ADR-0016: Kundali ephemeris provider, licence, and the explicit unsupported-capability boundary

- Status: Proposed - awaiting owner ratification (see Owner decision record)
- Date: 2026-11-17
- Supersedes: the Kundali-facing limitation notes in [ADR-0002](0002-analytical-ephemeris.md); ADR-0002 itself remains
  Accepted for Panchang, festivals and Muhurta, which continue to use it unchanged.
- Related: [#30 Reusable validated Kundali calculation engine](https://github.com/aprraju/myvedicstar/issues/30),
  [#40 owner decisions](https://github.com/aprraju/myvedicstar/issues/40)

## Context

ADR-0002 chose the Meeus analytical series behind `IEphemeris` and said plainly that Kundali "will need a
higher-precision `IEphemeris` implementation". That gap is the first thing Kundali has to close, because a North
Indian chart, a dasha timeline and a Shadbala table are all degrees-and-minutes artefacts: a 10-arcsecond lunar
error is invisible in a Panchang minute but moves a Navamsa pada boundary, and a 0.1-0.2 degree Jupiter error is
irrelevant for a combustion orb but decides a Vimshottari sub-period date by days.

The owner has fixed the licence constraint for the whole module (#40): an MIT/free provider only, no paid licence.
That single line removes the default answer, Swiss Ephemeris, whose free path is AGPL and whose non-AGPL path is a
paid commercial licence. It also rules out shipping licensed JPL data files.

What remains are numerically serious, permissively licensed providers that are either pure managed code or
mount-safe native libraries. This ADR picks one, states its accuracy honestly, and - because the owner also
required it - publishes the explicit list of things this engine will not support, rather than quietly
approximating them.

## Why the boundary document matters more than the choice

Two owner requirements collide if the choice is left informal:

- "Never degrade silently" (#40).
- Several classical Kundali outputs need precision or conventions no free provider supplies: D60 (Shashtiamsa)
  boundaries need arc-second Lagna, Shadbala's Chesta Bala needs a true apparent-velocity convention, and pada
  boundaries need a convention the provider does not define.

A provider choice without a capability list invites the implementation to interpolate, guess, or round a little -
which is exactly the silent degradation the owner forbade. So the capability list is a first-class architectural
artefact and an API contract, not a footnote: it is surfaced as data (`KundaliCapabilities`) so the engine,
Business layer, API and UI all read one immutable source of truth.

## Decision

### 1. Provider: a permissively licensed numerical ephemeris behind the existing `IEphemeris` port

Kundali adds a new `IEphemeris` implementation in `MyVedicStar.Kundali.Calculation` - in ADR-0002's own words, "a
new adapter, not a redesign". The Panchang implementation is not modified and remains the only implementation
reachable from Day modules.

Selection criteria, in order: (a) licence compatible with redistribution and commercial hosting without payment,
(b) geocentric apparent ecliptic longitude and latitude of the nine grahas to arc-second ambition, (c) no runtime
network dependency, (d) deterministic output for a given instant, (e) data small enough to ship in a container
image, (f) maintainable from C#.

The engine therefore targets a pure-managed, high-precision VSOP87/ELP-2000 series provider compiled into the
`Calculation` assembly, extended beyond ADR-0002's truncated series:

- Sun-Saturn: full VSOP87D (MPP02-class) heliocentric series plus geocentric reduction, light time and annual
  aberration - not the truncated Earth-only series. Truncation is chosen by tolerance budget and the chosen
  truncation is part of the engine version.
- Moon: the full ELP-2000/82 series at a term count selected to hold the Lagna and pada budgets in section 4.
- Rahu/Ketu: mean lunar node (the standard convention); see section 3.6 for the explicit statement of what is and
  is not supported about the node.
- Lagna: sidereal ascendant from local sidereal time and geographic latitude, using Lahiri ayanamsa as fixed by the
  approved default. No Kundali-specific settings store.
- Ayanamsa: Lahiri only.

All of these are pure functions of instant, observer and ayanamsa. No provider call performs I/O, network access,
caching or persistence.

Rejected alternatives are recorded, not omitted, because "we chose X" without "we did not choose Y, because Z" is how
the Swiss Ephemeris question gets re-opened in six months:

| Candidate | Licence | Why not chosen |
| --- | --- | --- |
| Swiss Ephemeris (libswe) | AGPL-3.0 (free) or paid commercial | Owner forbids paid licences; AGPL is incompatible with the intended distribution and hosting model. |
| JPL DE440/DE441 + reader | Public-domain data, licence-clean | Multiple-hundred-MB data files in the image plus a non-trivial reader to audit; permissive series reach the required accuracy for the supported capability set. |
| SwissEphNet and other managed wrappers | inherited | Inherit the parent licence problem. |
| ADR-0002's truncated Meeus + Standish Keplerian set | MIT-style, already present | Demonstrably insufficient: Moon 10 arcseconds, planets 0.1-0.2 degrees (ADR-0002's own measurements). Cannot deliver the supported list in section 3. |

Exact package identity, its version, and the corresponding rule-set version are pinned in `KundaliEngineVersions`
and stamped on every persisted result, so a future provider swap is a versioned data migration rather than a silent
change of past results.

### 2. The provider is not asked to answer questions it cannot answer

A capability is supported only when all of the following hold: the numerical budget is met against independent
fixtures, the convention is unambiguous, and the result is reproducible from a snapshot. Everything else is listed
as unsupported in section 3 and is refused explicitly.

The engine refuses by returning a typed `UnsupportedCapability` outcome for that section: it does not substitute an
approximation, does not silently narrow, and does not omit the section without a reason code. The API carries the
list through to the client so the UI can render a clear "not supported, and why" affordance. Where an unsupported
capability sits inside an otherwise supported section (for example Chesta Bala inside Shadbala), the section is
returned with that component explicitly marked unsupported and is never quietly dropped from the totals.

### 3. Unsupported capability list (the contract)

This list is the owner-mandated publication. It is authoritative for KUN-20 and is referenced - not restated - by
later Kundali stories.

1. D60 (Shashtiamsa) and divisional boundaries below D9 are not produced as a definitive fact. Each D-n boundary is
   a 30-degrees/n cut; D60 is a half-degree cut and a full D60 chart additionally needs an arc-second Lagna. At the
   supported Lagna budget (section 4) a D60 placement can be wrong for births near a boundary. We therefore (a) do
   not claim a D60 chart as correct; (b) do return the placement together with a computed boundary distance in
   arcseconds, marked provisional when that distance is inside the engine tolerance. This is the
   "never degrade silently" rule applied literally: the value is either supported or visibly marked.
2. D30/D24/D20/D16/D12/D10/D7 boundaries are supported, because their cuts are at least 1.5 degrees and the section
   4 Lagna budget is smaller. D9 is supported and is the smallest definitively supported division.
3. Shadbala Chesta Bala is not supported and is returned as an explicit unsupported component. Chesta needs a true
   apparent-velocity convention per graha, including the classical retrograde re-scaling, that this provider set does
   not define to the classical formula's satisfaction. The other five balas (Sthana, Dig, Kala, Naisargika, Drik) are
   supported; the Shadbala total is reported as partial with the unsupported component named, and is not presented as
   a classical required-rupas verdict.
4. Tight pada boundaries are not asserted. A pada (D9) boundary claim carries the same provisional marking rule as
   3.1 whenever the birth instant is within engine tolerance of the cut.
5. No arc-second astronomy, no eclipse prediction, no topocentric parallax, no atmospheric refraction, and no
   satellite or asteroid positions.
6. Mean node only. Rahu/Ketu are the mean lunar node; true-node positions are not produced, and the choice is
   carried as an explicit rule-set fact on every result.
7. No timezone-database authoring. The engine converts using the profile's stored IANA id; if the id cannot be
   resolved on the running host that is a dependency failure (503), never a silent UTC fallback.
8. No rectification, no prashna, no muhurta-style electional output. Out of module scope by design.
9. Dates outside 1800-2200 are refused rather than extrapolated, extending ADR-0002's delta-T honesty to the
   supported range of the chosen series.

Anything not on this list and not in the supported set is a gap, not a licence to approximate. Discovering one means
a new ADR revision.

### 4. Accuracy budgets (acceptance tolerances, not aspirations)

These are the tolerances the independent reference fixtures must hold. They are validation gates in KUN-20;
exceeding them fails the build.

| Quantity | Budget | Basis |
| --- | --- | --- |
| Sidereal longitude, Sun-Saturn | <= 1 arcsecond | Fixtures against an independent high-precision reference at fixed instants |
| Sidereal longitude, Moon | <= 3 arcseconds | Same |
| Lagna (ascendant) | <= 5 arcseconds | Same; drives the 3.1 provisional rule |
| Ayanamsa (Lahiri) | <= 1 arcsecond | Published Lahiri values at sampled epochs |
| Nakshatra / pada assignment | exact, or provisional inside the Lagna/Moon budget | Derived; no invented tie-breaks |
| Vimshottari period dates | <= 60 s at sub-period level, 365.25-day years | Owner default; compared against a published worked example |

Budgets are gates. KUN-31's latency budgets are separate acceptance gates and are never traded against these.

### 5. What does not change

- `IEphemeris` and the Day modules' implementations are untouched; no Panchang, festival or Muhurta output changes.
- No new persistence in `Calculation`; the engine stays pure (ADR-0001/0015 dependency direction).
- Ayanamsa stays Lahiri-only from the existing application Settings - no Kundali settings store.
- Chart style remains presentation-only and never triggers recalculation.

## Consequences

- Positive. Licence-clean, deterministic, container-friendly, no runtime network, and a written capability boundary
  that later stories cite instead of re-deriving. The provider can be replaced behind one port with a versioned
  result migration.
- Cost. Engine version becomes customer-visible provenance: the ephemeris and rule-set versions must be persisted
  with every result and must be part of the cache key and staleness rules (KUN-21/KUN-22). A truncation change is a
  new version, not an edit.
- Known limitation, stated up front. D60 and Chesta Bala are visibly unsupported; the product shows a
  "not supported" affordance for them. That is the intended outcome of the owner's decision, not a regression.
- Risk. A full ELP-2000/82 series is meaningfully more code than ADR-0002's truncation; its term tables and
  versioning must be reviewed and fixture-tested, and the fixture set must be independent of the implementation
  (never generated by it).

## Owner decision record

Per #40, already ruled final:

- Ephemeris: MIT/free provider only, no paid licence - reflected in section 1 and the rejected-candidates table.
- An explicit unsupported-capability list must be published - section 3.
- Never degrade silently - section 2 and the provisional-marking rule.
- Lahiri-only ayanamsa and 365.25-day dasha years - referenced in section 4, not re-litigated here.

Not re-opened by this ADR: none of the 14 approved defaults, the latency gates, or the remedies scope.

## Verification

- Fixtures for the section 4 tolerances, produced from an independent reference and checked into the repository.
- A regression test asserting the section 3 list is returned verbatim by the capability contract, so removing an
  item requires an ADR change.
- A test that an out-of-range date and an unresolvable timezone produce refusals (400/503), never values.

