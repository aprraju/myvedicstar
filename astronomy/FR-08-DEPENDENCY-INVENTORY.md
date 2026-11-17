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
| NAIF CSPICE | **reported by the image at run time** | Public domain (NASA/JPL, US Government work) | https://naif.jpl.nasa.gov/naif/toolkit.html | "SPICE toolkit, courtesy of NASA/JPL-Caltech" in NOTICE. |

**Toolkit version is not hardcoded.** `SpiceConfiguration.ReportedToolkitVersion` is supplied by the image, and
`SpiceAstronomyEngine.VerifyToolkitMatchesInventory` fails when the reported version differs from this inventory or is
absent. A constant in source would make this claim unfalsifiable.
| Generic kernels (naif0012.tls, pck00011.tpc) | N0067-era | Public domain | https://naif.jpl.nasa.gov/naif/data_generic.html | Same notice. |

### Provisioning

The native library and kernels are **baked into the container image**. Kernels are **mounted read-only** at
`/var/spice`. There is **no runtime download** (D1). A missing toolkit or kernel is a deployment fault surfaced as a
dependency failure, never a silent fallback to another numerical route.

## Datasets

| Dataset | Span used | Licence | Provenance |
| --- | --- | --- | --- |
| JPL DE440 | 1800-01-01 .. 2200-12-31 supported span | Public domain | https://ssd.jpl.nasa.gov/planets/eph_export.html |
| JPL DE441 | Extended span; declared from the product floor (DE441 nominally begins before year 1, which `DateOnly` cannot represent) | Public domain | Same |

**Kernel coverage is declared in `SpiceConfiguration` and the supported date span is derived from it** - not asserted
in prose. `SpiceConfiguration.Default` declares both DE440 and DE441, and a test proves the shipped configuration
covers the 1800-01-01..2200-12-31 promise.

## Deliberately absent

| Component | Status |
| --- | --- |
| Swiss Ephemeris / libswe | **Not referenced.** AGPL or paid; excluded by owner decision. |
| SwissEphNet | **Not referenced.** |
| Any .se1 data file | **None in this solution.** |

A test asserts this absence mechanically; see `DependencyInspectionTests`.
