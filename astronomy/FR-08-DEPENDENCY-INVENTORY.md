# FR-08 dependency inventory - MyVedicStar.Astronomy

Every third-party package, native toolkit and dataset referenced by this solution, with licence, provenance and
attribution requirement. AC-4 asserts that every package reference appears here.

## Runtime packages

| Package | Version | Licence | Why it is present |
| --- | --- | --- | --- |
| *(none)* | - | - | The solution ships no NuGet runtime dependency. Time, angle and frame maths are implemented in-repo, so no result depends on an external library version. |

## Test-only packages

| Package | Version | Licence |
| --- | --- | --- |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |

## Native toolkit

| Component | Version | Licence | Provenance | Attribution |
| --- | --- | --- | --- | --- |
| NAIF CSPICE | N0067 | Public domain (NASA/JPL, US Government work) | https://naif.jpl.nasa.gov/naif/toolkit.html | "SPICE toolkit, courtesy of NASA/JPL-Caltech" in NOTICE. |
| Generic kernels (naif0012.tls, pck00011.tpc) | N0067-era | Public domain | https://naif.jpl.nasa.gov/naif/data_generic.html | Same notice. |

### Provisioning

The native library and kernels are **baked into the container image**. Kernels are **mounted read-only** at
`/var/spice`. There is **no runtime download** (D1). A missing toolkit or kernel is a deployment fault surfaced as a
dependency failure, never a silent fallback to another numerical route.

## Datasets

| Dataset | Span used | Licence | Provenance |
| --- | --- | --- | --- |
| JPL DE440 | 1800-01-01 .. 2200-12-31 supported span | Public domain | https://ssd.jpl.nasa.gov/planets/eph_export.html |
| JPL DE441 | Extended span, selectable via kernel set | Public domain | Same |

## Deliberately absent

| Component | Status |
| --- | --- |
| Swiss Ephemeris / libswe | **Not referenced.** AGPL or paid; excluded by owner decision. |
| SwissEphNet | **Not referenced.** |
| Any .se1 data file | **None in this solution.** |

A test asserts this absence mechanically; see `DependencyInspectionTests`.
