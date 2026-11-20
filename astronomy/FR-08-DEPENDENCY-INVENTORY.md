# FR-08 dependency inventory - MyVedicStar.Astronomy

Every third-party package, native toolkit and dataset referenced by this solution, with licence, provenance and
attribution requirement. A test asserts that every package reference appears here (`DependencyInspectionTests`), and
that each third-party row names a provenance source.

**Licences on this page are quoted terms, not remembered ones.** The quoted text, the source URL, the fetch date and
the package hashes are in [FR-08-LICENSING-EVIDENCE.md](FR-08-LICENSING-EVIDENCE.md). Where a dependency does not
state a licence cleanly, this page says so instead of rounding it up.

## Runtime packages

| Package | Version | Licence | Why it is present |
| --- | --- | --- | --- |
| *(none)* | - | - | The solution ships no NuGet runtime dependency. Time, angle and frame maths are implemented in-repo, so no result depends on an external library version. |

## Test-only packages

Not redistributed: test projects are `IsPackable=false` and no shipped project references them.

| Package | Version | Licence | Licence stated by |
| --- | --- | --- | --- |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT — "The MIT License (MIT) / Copyright (c) Microsoft Corporation" | nuspec `<license>MIT</license>`; `LICENSE` at tag `v17.14.1`; quotes and hashes in evidence doc §2.3 |
| xunit | 2.9.3 | Apache-2.0 — "Licensed under the Apache License, Version 2.0" | nuspec `<license>Apache-2.0</license>`; `license.txt` at commit `9712244020d385955d33136b3fe3e87de43539cd`; evidence doc §2.3 |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 — same wording | nuspec `<license>Apache-2.0</license>`; `License.txt` at commit `50e68bbb`; evidence doc §2.3 |
| xunit.core / xunit.assert / xunit.extensibility.core | 2.9.3 (transitive) | Apache-2.0 | same nuspec expression and same `license.txt`; evidence doc §2.3 |
| xunit.analyzers | 2.3.0 (transitive) | Apache-2.0 | nuspec expression; repository `github.com/xunit/xunit.analyzers` at commit `49b7e400`; evidence doc §2.3 |
| xunit.abstractions | 2.0.3 (transitive) | **Not stated by the package.** No `<license>` element; only a legacy `licenseUrl` to `raw.githubusercontent.com/xunit/xunit/master/license.txt`. Project-level Apache-2.0 is the evident intent; recorded as unknown-with-rationale. | nuspec read from the downloaded package; evidence doc §2.3 |

## Native toolkit

| Component | Version | Licence | Provenance | Attribution |
| --- | --- | --- | --- | --- |
| NAIF CSPICE | **reported by the image at run time** | No single identifier. NAIF rules: commercial use allowed, no fees, export classification TSPA, AS-IS with recipient indemnity | https://naif.jpl.nasa.gov/naif/rules.html | Encouraged by NAIF; we include it — `THIRD-PARTY-NOTICES.md` and the Acton 1996 reference. |
| Generic kernels (naif0012.tls, pck00011.tpc) | N0067-era | Same NAIF rules | https://naif.jpl.nasa.gov/naif/data_generic.html ("be sure to understand the provenance of any generic kernel") | Same notice. Per-kernel provenance is **not yet read from the kernel files** — they live in the deployment image, not this repository. |

**Toolkit version is not hardcoded.** `SpiceConfiguration.ReportedToolkitVersion` is supplied by the image, and
`SpiceAstronomyEngine.VerifyToolkitMatchesInventory` fails when the reported version differs from this inventory or is
absent. A constant in source would make this claim unfalsifiable.

### Provisioning

The native library and kernels are **baked into the container image**. Kernels are **mounted read-only** at
`/var/spice`. There is **no runtime download** (D1). A missing toolkit or kernel is a deployment fault surfaced as a
dependency failure, never a silent fallback to another numerical route.

## Datasets

| Dataset | Span used | Licence | Provenance | Scientific reference |
| --- | --- | --- | --- | --- |
| JPL DE440 | 1800-01-01 .. 2200-12-31 supported span | NAIF rules (distributed by NAIF); "US Government work" is the reason it is unencumbered, not a term JPL quotes — see evidence doc §2.2 | https://ssd.jpl.nasa.gov/planets/eph_export.html | Park, Folkner, Williams, Boggs (2021), AJ 161(3):105, DOI 10.3847/1538-3881/abd414 |
| JPL DE441 | Extended span; declared from the product floor (DE441 nominally begins before year 1, which `DateOnly` cannot represent) | Same | https://ssd.jpl.nasa.gov/planets/eph_export.html | Park et al. (2021), AJ 161(3):105 — see evidence doc §2.2 |

**Kernel coverage is declared in `SpiceConfiguration` and the supported date span is derived from it** - not asserted
in prose. `SpiceConfiguration.Default` declares both DE440 and DE441, and a test proves the shipped configuration
covers the 1800-01-01..2200-12-31 promise.

## Deliberately absent

| Component | Status |
| --- | --- |
| Swiss Ephemeris / libswe | **Not referenced.** AGPL or paid; excluded by owner decision. |
| SwissEphNet | **Not referenced.** |
| Any .se1 data file | **None in this solution.** |

A test asserts this absence mechanically; see `DependencyInspectionTests`. The independent-development record is in
[FR-08-LICENSING-EVIDENCE.md](FR-08-LICENSING-EVIDENCE.md) §3.

## Review status

The licensing review required by AC-3 has **not** been performed. This inventory is the material for it, not the
outcome of it. AST-12 (#77) remains blocked on that review.
<!-- CI path-filter proof for #86: this astronomy-only edit must trigger MyVedicDay API CI and MyVedicStar API CI. -->