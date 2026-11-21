# FR-06 provider migration - cutover record (AST-12, #77)

The provider default is now **`spice`** (JPL/SPICE over NAIF CSPICE + DE440/DE441). The superseded ADR-0016
analytical (VSOP87D/ELP-2000) implementation is no longer the default; it remains **registered and reachable** as
`analytical`, and `compare` still runs both.

## What actually changed

| Change | Where |
| --- | --- |
| Absent `Astronomy:Provider` now resolves to `spice` | `ProductProviderSelection.DefaultKey` - declared **once**, in the product assembly |
| Both product hosts read that single default and supply the SPICE engine factory | `PanchangBusinessServiceCollectionExtensions`, `MyVedicStarApiModule` |
| SPICE engine construction from configuration (`Astronomy:Spice`) | `SpiceProviderComposition` |
| `IEphemerisEngine` is satisfied by a production type | `ProviderEngineAdapter` (product side) over `SpiceAstronomyEngine` |
| `SpiceAstronomyEngine` answers tropical longitude through the FR-02 planetary surface | `SpiceAstronomyEngine.GetTropicalLongitude` |

**No consumer project was edited.** `PanchangElementCalculator`, `SunEventCalculator`, `MoonEventCalculator`,
`LunisolarCalendar` and the Kundali engine all resolve `IEphemeris` through DI, which is what story AC-1 and the
"no Kundali story changes" constraint require.

## The defect this story found and fixed

Before this change **no production type implemented `IEphemerisEngine`**. The `spice` and `compare` keys could only
be composed with a test double, so the default provider was unreachable in production and the "cutover" could not have
happened by configuration alone. The port now extends `IAstronomyEngine` and `ProviderEngineAdapter` closes it; a test
(`The_default_provider_is_constructible_from_the_production_engine_type`) fails if that regresses.

## Rollback (story AC-2)

Set `Astronomy:Provider` to `analytical` (old provider) or `compare` (both, emits parity deltas) and restart. No
code change, no redeploy beyond configuration. Both keys remain parsed and registered, and
`An_unknown_provider_key_fails_at_registration` still guarantees a typo fails at composition rather than silently
degrading.

## Verification performed

- `MyVedicStar.slnx` builds clean (warnings-as-errors on).
- `MyVedicDay.Astronomy.Tests.Calculation`: **104/104 pass**, including four new AST-12 tests (default resolution,
  configured key precedence, unknown-key refusal, production-type port satisfaction).
- `MyVedicStar.Astronomy.Tests`: **136/136 pass**, including the FR-08 inventory and no-Swiss-artefact gates.

## Outstanding, stated rather than implied

1. **Equatorial/parallax on the SPICE provider is refused, not approximated.** `ProviderEngineAdapter.GetEquatorial`
   throws `AstronomyNotSupportedException` because the validated surface publishes ecliptic coordinates only and
   horizontal parallax is not part of it. Sun/Moon **longitude** consumers (tithi, nakshatra, yoga, karana, month
   boundaries) route through SPICE; sunrise/sunset and moonrise/moonset need the AST-05 equatorial surface before this
   gap closes. A zero parallax was rejected deliberately: it would move moonrise/moonset with no evidence trail (D1).
2. **SPICE body numerics are still `NotImplementedException`** in `GetPlanetaryPosition` (AST-04/AST-05). The port
   now routes into them, so a SPICE-selected host fails loudly at first calculation on a machine without the numerics
   rather than returning the analytical series. That is the intended D1 behaviour, and it is why the cutover's
   remaining dependency is AST-05, not a configuration detail.
3. **Kernel paths and the reported toolkit version are deployment configuration** (`Astronomy:Spice:Kernels[]`,
   `ReportedToolkitVersion`). The image must mount `/var/spice` and report its CSPICE version, or
   `VerifyToolkitMatchesInventory` fails as designed.
4. **No production deployment or promotion was performed** by this change; that requires explicit authorization.
