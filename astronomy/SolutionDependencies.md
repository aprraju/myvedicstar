# MyVedicStar.Astronomy solution boundaries

Implements AST-01 (#66), FR-01. This solution is deliberately **independent of the product**: it references no
`MyVedicStar.*` or `MyVedicDay.*` project, so it builds and tests from a clean checkout with no product APIs.

## Projects and responsibilities

| Project | Responsibility | May reference |
| --- | --- | --- |
| `MyVedicStar.Astronomy.Core` | Contracts (`IAstronomyEngine`), value types (`JulianDayUt`, `CelestialBody`, `EclipticPoint`, `EquatorialPoint`, `ObserverLocation`), provenance, capability boundary. | Nothing in this solution. |
| `MyVedicStar.Astronomy.Coordinates` | Angle maths, frame conversion, sidereal time. | Core |
| `MyVedicStar.Astronomy.Events` | Altitude-crossing root finding for rise/set/transit and ingress. | Core, Coordinates |
| `MyVedicStar.Astronomy.Houses` | Ascendant and house geometry. | Core, Coordinates |
| `MyVedicStar.Astronomy.Jpl.Provider` | CSPICE P/Invoke surface and the SPICE-backed `IAstronomyEngine`. | Core |
| `MyVedicStar.Astronomy.Tests` | Solution-level tests, including the boundary and independence assertions. | All of the above |

## Enforced rules

1. **No upward references.** Core never references a provider or the calculation projects. A provider never
   references another provider. `ProjectBoundaryTests` fails if any of these edges appears.
2. **No product reference.** No project in this solution references a `MyVedicStar.*` or `MyVedicDay.*` product
   project. The test asserts this from the project files themselves, so it cannot drift.
3. **No Swiss component.** No package reference, source file or asset named Swiss Ephemeris, SwissEphNet or `.se1`
   exists. The test scans every `.csproj` and every file name in the solution.
4. **Capability boundary as data.** `AstronomyCapabilities` declares supported bodies, node conventions, ayanamsa
   kinds, the date span and an explicit unsupported list. Callers do not infer support from absence.

## What this story does not do

- No body-position numerics: `SpiceAstronomyEngine` loads and validates nothing yet; it refuses cleanly when the
  native toolkit is absent and raises `NotImplementedException` for position mapping, which lands in AST-02..AST-07.
  This is stated rather than stubbed with plausible-looking numbers.
- No consumer change. `IEphemeris` in `src/MyVedicDay.Astronomy.Calculation/Ephemeris.cs` is untouched and remains
  the only surface Panchang/festivals consume; the adapter is AST-06.
- No default-provider flip; that is AST-12 (#77), after the parity gate passes.
