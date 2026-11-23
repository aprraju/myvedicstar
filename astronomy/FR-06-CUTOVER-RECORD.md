# FR-06 provider migration - cutover record (AST-12, #77)

## Status: preconditions pinned; composition complete; default flip DEFERRED

The SPICE provider is registered, reachable, wired into both product hosts, and pinned by a named test. The
**absent-key default remains `analytical`**. This file states what has been **proven**, with the run or file that
proves it, and what has **not**.

## 1. The guarantee that must not break: a toolkit-free host serves every request

`ProductProviderSelection.DefaultKey` is `ProviderSelectionParser.AnalyticalKey`
(`src/MyVedicDay.Astronomy.Calculation/ProductProviderSelection.cs:29`; resolution at line 43 treats absent, empty
and whitespace-only configuration alike).

This was prose until now. It is a named test as of #77 preconditions:
`tests/MyVedicDay.Api.Tests.Integration/Configuration/ProviderCutoverGuardTests.cs` asserts (a) an absent key, an
empty key and an explicit `analytical` key all resolve to `ProviderSelection.Analytical`; and (b) a host composed with
no `Astronomy:Provider` and no `Astronomy:Spice` section, on a machine where `SpiceAstronomyEngine.IsNativeToolkitAvailable`
is **false**, still returns `200` for `/health/live` and for `/api/v1/panchang/today`, with the DI-resolved
`IEphemeris` being a non-SPICE implementation.

That test refuses to pass vacuously: on a host that mounts CSPICE it fails with a message saying the distinction cannot
be observed there (`ProviderCutoverGuardTests.cs:55-57`), so a provisioned runner cannot turn a non-observation
into a green.

**Why it matters, measured:** with the default flipped and no toolkit present, `MyVedicDay.Api.Tests.Integration`
went to **40 failed / 130 passed / 171**, all 40 sharing one `SpiceUnavailableException` at
`SpiceAstronomyEngine.EnsureAvailable`. That is the outage the deferred flip avoids.

## 2. What HAS been proven about provisioning

| Claim | Evidence |
| --- | --- |
| The native toolkit is built from pinned sources and its version is read back from the library | `.github/workflows/spice-provisioning.yml:56,69,72,136-140` |
| The deployment-image provisioning script pins toolkit URL and SHA-256 | `deploy/scripts/provision-spice.sh:29,73-74` |
| The deploy Dockerfile runs that script at build time | `deploy/docker/Dockerfile:29,32` |
| A provisioned run of the astronomy package workflow completed successfully | GitHub Actions run **45078530912** (`MyVedicStar Astronomy Package`, event `pull_request`, head `3f212b5`, branch `feat/issue74-ast09-reference-growth`, 2026-11-22T22:35Z): jobs *"Image builds and the engine refuses without a toolkit"* and *"Package builds, publishes, and runs outside the build tree"* both **success** |
| The engine distinguishes "toolkit missing" from "numerics not written" | `tests/MyVedicDay.Api.Tests.Integration/Api/SpiceProvisioningTests.cs` |

The statement in earlier revisions of this file - that `cspice` "appears nowhere under `.github/` or `deploy/`" - was
true when the flip was deferred and is **now false**. Toolkit installation landed in #184/#187; the greps above are the
current state.

## 3. What has NOT been proven

1. **The deployment image still does not build.** `az acr build` rejects `--build-context`, tracked as **api#193**
   (`Deploy image build fails on main: az acr build rejects --build-context`, open). The provisioning script is wired
   into `deploy/docker/Dockerfile` but no image containing the toolkit has been produced and inspected. **A provisioned
   CI runner is not a provisioned deployment image**, and this file does not claim otherwise.
2. **No post-flip request path has been executed against a provisioned host.** The guard test above asserts the
   *unflipped* state; the SPICE path is exercised only up to the named `NotImplementedException` (AST-04/AST-05) that
   `SpiceProvisioningTests` asserts.
3. **SPICE body numerics are incomplete** (AST-04/AST-05): `GetTropicalLongitude` throws a typed
   `NotImplementedException` naming those stories rather than returning a substitute value.
4. **Equatorial/parallax on SPICE refuses rather than approximates** (AST-05): the validated surface publishes ecliptic
   coordinates only, so the adapter raises rather than returning a zero - a zero would move moonrise/moonset with no
   evidence trail.

## 4. Why the default does not flip here

Flipping `ProductProviderSelection.DefaultKey` to `spice` today would point every Panchang, Festivals and Muhurta
request at an engine that (a) throws `SpiceUnavailableException` on any host without the toolkit and (b) throws
`NotImplementedException` for body numerics on any host with it. Those are the same failure, from both sides. The flip
belongs to the story that lands the numerics, with AC-1/AC-3 evidence from #75 attached.

## 5. What this story delivers

| Change | Where |
| --- | --- |
| Absent-key default declared once | `ProductProviderSelection` |
| Both product hosts read that default and supply the SPICE engine factory | `PanchangBusinessServiceCollectionExtensions:71-79`, `MyVedicStarApiModule:53-62` |
| SPICE engine construction from configuration | `SpiceProviderComposition` |
| `IEphemerisEngine` satisfied by a production type | `ProviderEngineAdapter` over `SpiceAstronomyEngine` |
| **The unchanged guarantee pinned by a named test** | `ProviderCutoverGuardTests` (this change) |
| Opt-in and rollback remain configuration-only | `Astronomy:Provider` = `analytical` (default), `spice`, `compare` |

No consumer project was edited; Panchang element, sun/moon event, lunisolar calendar and Kundali resolve `IEphemeris`
through DI.
