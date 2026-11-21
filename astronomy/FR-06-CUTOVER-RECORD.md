# FR-06 provider migration - cutover record (AST-12, #77)

## Status: composition complete, default flip DEFERRED to AST-05 (#70)

The SPICE provider is registered, reachable, wired into both product hosts, and covered by tests. The **absent-key
default remains `analytical`** and does not flip in this story. That is a ruling, not an oversight, and the reason is
below.

## Why the default does not flip here

Selecting `spice` requires the **native NAIF CSPICE toolkit** plus the DE440/DE441 kernels. Neither exists in the
test host, the CI image, or the deployment image: `cspice` appears nowhere under `.github/` or `deploy/`. Making
`spice` the absent-key default therefore turns every Panchang, Festivals and Muhurta request into a dependency on an
unprovisioned native library - an accuracy migration converted into an outage.

Measured, not assumed: with the default flipped, `MyVedicDay.Api.Tests.Integration` went to **40 failed / 130 passed /
171**. All 40 share one exception:

```
SpiceUnavailableException: The native SPICE toolkit ('cspice') could not be loaded.
  at SpiceAstronomyEngine.EnsureAvailable
  at SpiceAstronomyEngine.GetPositions
  at ProviderEngineAdapter.GetEquatorial
  at AstronomyEngineEphemerisAdapter.GetSunEquatorial
  at SunEventCalculator.Refine -> CalculateSunset
  at PanchangCalculator.CalculateSunEvents -> FestivalCalculator.CalculateDays
```

A competing hypothesis - that the equatorial path was the cause - was tested in code by deriving equatorial
coordinates from the validated ecliptic surface. Failures stayed at 40, so the hypothesis was wrong and those edits
were reverted. The blocker is provisioning, upstream of any calculation route.

## What this story does deliver

| Change | Where |
| --- | --- |
| Absent-key default declared once, in one place | `ProductProviderSelection` |
| Both product hosts read that default and supply the SPICE engine factory | `PanchangBusinessServiceCollectionExtensions`, `MyVedicStarApiModule` |
| SPICE engine construction from configuration (`Astronomy:Spice`) | `SpiceProviderComposition` |
| `IEphemerisEngine` satisfied by a production type | `ProviderEngineAdapter` over `SpiceAstronomyEngine` |
| `SpiceAstronomyEngine.GetTropicalLongitude` via the FR-02 surface | provider assembly |
| Tests pinning the default, the opt-in keys and the port satisfaction | `ProviderMigrationTests` |

**No consumer project was edited.** Panchang element, sun/moon event, lunisolar calendar and Kundali consumers all
resolve `IEphemeris` through DI.

## Opt-in and rollback (story AC-2)

`Astronomy:Provider` accepts `analytical` (default), `spice` and `compare` (both, emitting parity deltas). A host
that mounts the toolkit and kernels sets `spice`; a host that does not gets `analytical` without any code change.
An unknown key still fails at composition rather than degrading.

## Deferred, named

1. **Default flip to `spice`** - blocked on CSPICE + kernel provisioning (its own issue), then AST-05 (#70).
2. **Equatorial/parallax on the SPICE provider** - the validated surface publishes ecliptic coordinates only and no
   lunar parallax; the adapter refuses rather than returning a zero, because a zero moves moonrise/moonset with no
   evidence trail (D1). Closes with the AST-05 equatorial surface.
3. **SPICE body numerics** - still `NotImplementedException` (AST-04/AST-05). Opting into `spice` today therefore fails
   loudly on first calculation rather than silently reverting to the analytical series.
4. **No production deployment or promotion** is performed by this change.

## Known count discrepancy

Local runs report **40** failures with the default flipped; the CI rollup reported **41**. One test differs between the
two environments and has not been isolated. This is recorded rather than rounded, because a count that moved by one is
an unexplained difference, not a rounding artifact.

## Provisioning follow-up

The CSPICE/kernel provisioning need is filed as #173 and linked from here so the deferral above has an owner rather
than a hope. #77 and AST-05 (#70) both depend on it before the default can flip.
