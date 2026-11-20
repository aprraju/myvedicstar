# FR-08 licensing and independence evidence (AST-08, api#73)

This document exists because a licence claim asserted from memory is the same class of defect as a numeric claim
asserted from memory. Every statement below is either a **quotation** from the dependency's own file or page, fetched
over the network at the time of writing (2026-11-20), with the source named, or it is explicitly marked as a claim with
no evidence yet.

Three things are kept apart throughout: **what the licence says**, **what we do with it**, and **what has been
approved**. Nothing here is approved. Approval is AC-3 and it is a human act, recorded by a named approver with a date;
this document is the material they review, not a substitute for their review.

## 1. What actually ships

The distinction matters legally and is the first thing a reviewer needs:

| Layer | Content | Licence question? |
| --- | --- | --- |
| **Runtime — owned code** | `MyVedicStar.Astronomy.Core`, `.Coordinates`, `.Events`, `.Houses` | No third-party code. All arithmetic is ours. |
| **Runtime — our P/Invoke surface** | `SpiceNative` declares 3 CSPICE signatures | **No CSPICE code is copied.** The declarations are our own source; the implementation is the deployed native library. |
| **Runtime — native library** | NAIF CSPICE `libcspice`, supplied by the image, not packaged (see `PACKAGING.md` §2.2) | Yes — NAIF rules, §2.1 below. |
| **Runtime — data** | DE440/DE441 `.bsp`, `naif0012.tls`, `pck00011.tpc`, mounted read-only at `/var/spice` | Yes — NAIF rules, §2.1 below. |
| **Test-only packages** | `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` (+ their transitive xunit packages) | Yes, but **not redistributed** — they never enter a product artefact. |

`astronomy/runtime/Dockerfile` ships **no** toolkit and **no** kernel: the CI image is a consumer proof, not a
release image. A deployment image adds them; that image's contents are the deployment pipeline's evidence, not this
story's.

## 2. Quoted licences

### 2.1 NAIF SPICE toolkit and kernels

**Source:** `https://naif.jpl.nasa.gov/naif/rules.html`, "Rules Regarding Use of SPICE", fetched 2026-11-20
(HTTP 200, 23,487 bytes). Reached from `https://naif.jpl.nasa.gov/naif/toolkit.html` → "Rules".

The page does not carry a single `LICENSE` heading. It states the licence contract and the warranty disclaimer as two
separate passages, so both are quoted in full below rather than paraphrased into one sentence.

**The licence contract:**

> "IMPORTANT ! Please read in its entirety this list of rules regarding use of SPICE components. Included in these
> rules, although not specifically called out as such, are provisions that collectively provide a \"SPICE Software
> License.\""
>
> "**Commercial Use of SPICE** Use of SPICE components in commercial products is allowed, subject to the substantial
> restrictions on user support noted under \"Getting Help from NAIF\" above. No fees or licensing are required.
> Acknowledgement of the use of NASA's SPICE system is encouraged."
>
> "Export SPICE software has been designated \"Technology and Software Publicly Available\" (TSPA) for export control
> purposes. As a result the export of SPICE software and related documentation is not restricted, and is independent of
> and not limited by export agreements for specific flight projects."

**The disclaimer (the liability and warranty terms):**

> "THIS SOFTWARE AND ANY RELATED MATERIALS WERE CREATED BY THE CALIFORNIA INSTITUTE OF TECHNOLOGY (CALTECH) UNDER A
> U.S. GOVERNMENT CONTRACT WITH THE NATIONAL AERONAUTICS AND SPACE ADMINISTRATION (NASA). THE SOFTWARE IS TECHNOLOGY AND
> SOFTWARE PUBLICLY AVAILABLE UNDER U.S. EXPORT LAWS AND IS PROVIDED \"AS-IS\" TO THE RECIPIENT WITHOUT WARRANTY OF ANY
> KIND, INCLUDING ANY WARRANTIES OF PERFORMANCE OR MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE OR PURPOSE (AS SET
> FORTH IN UNITED STATES UCC§2312-§2313) OR FOR ANY PURPOSE WHATSOEVER, FOR THE SOFTWARE AND RELATED MATERIALS, HOWEVER
> USED. IN NO EVENT SHALL CALTECH, ITS JET PROPULSION LABORATORY, OR NASA BE LIABLE FOR ANY DAMAGES AND/OR COSTS,
> INCLUDING, BUT NOT LIMITED TO, INCIDENTAL OR CONSEQUENTIAL DAMAGES OF ANY KIND, INCLUDING ECONOMIC DAMAGE OR INJURY
> TO PROPERTY AND LOST PROFITS, REGARDLESS OF WHETHER CALTECH, JPL, OR NASA BE ADVISED, HAVE REASON TO KNOW, OR, IN
> FACT, SHALL KNOW OF THE POSSIBILITY. RECIPIENT BEARS ALL RISK RELATING TO QUALITY AND PERFORMANCE OF THE SOFTWARE AND
> ANY RELATED MATERIALS, AND AGREES TO INDEMNIFY CALTECH AND NASA FOR ALL THIRD-PARTY CLAIMS RESULTING FROM THE
> ACTIONS OF RECIPIENT IN THE USE OF THE SOFTWARE"

**Where the inventory was looser than the source.** The inventory said "Public domain (NASA/JPL, US Government work)"
and "Public domain" for the kernels. The rules page does not use the words "public domain". What it says is that
commercial use is allowed, no fees or licensing are required, the software is TSPA for export purposes, and it is
provided AS-IS with an indemnity from the recipient to Caltech and NASA. The practical conclusion — free to use
commercially, no fee — is the same, but a reviewer is entitled to the actual terms, so the inventory now quotes them
instead of substituting a term the source does not use.

**There is no unique "standard licence identifier" for this.** It is not MIT, Apache-2.0, or BSD; scanning tools will
report "unknown/custom". Any future automated licence gate must treat unknown as **review required**, not as failure
and not as pass (see §5).

**Attribution.** `https://naif.jpl.nasa.gov/naif/credit.html` (fetched 2026-11-20) says:
> "Referencing your use of SPICE or NAIF/PDS resources in publications, tools and findings is encouraged"
and names the reference:
> "Acton, C.H.; \"Ancillary Data Services of NASA's Navigation and Ancillary Information Facility;\" Planetary and Space
> Science, Vol. 44, No. 1, pp. 65-70, 1996. DOI 10.1016/0032-0633(95)00107-7"

Acknowledgment is *encouraged*, not required. We do it anyway, in `astronomy/THIRD-PARTY-NOTICES.md`.

**Kernel provenance is not uniform.** `https://naif.jpl.nasa.gov/naif/data_generic.html` (fetched 2026-11-20) states:
> "A wide assortment of source data are used in producing these generic kernels. Be sure to understand the provenance
> of any generic kernel you may consider using."
That page does not itself grant rights in the kernels. The applicable terms are the NAIF rules quoted above, which are
the terms NAIF applies to "SPICE components" including kernels — but the sentence above is why the inventory names
each kernel individually rather than treating "kernels" as one licensed blob. **Unresolved, stated as such:** we have
not yet read the internal comment block of each mounted kernel file (the kernels are not present in this repository,
so that check belongs to the deployment image). Until then, kernel provenance is asserted from the NAIF page that
distributes them, not from the files themselves.

### 2.2 DE440 / DE441

**Source:** `https://ssd.jpl.nasa.gov/planets/eph_export.html`, fetched 2026-11-20 (HTTP 200, 29,979 bytes).

> "The latest JPL ephemeris with fully consistent treatment of planetary and lunar laser ranging data is DE440 (Park
> et al., 2021)."
>
> "In order to cover a longer time span, the ephemeris DE441 was integrated without the lunar core/mantle damping term.
> The positions of the planets for DE441 agree with the positions on DE440 to within one meter over the time covered by
> DE440."

The page gives the scientific citation and the distribution point; the rights statement comes from the NAIF rules
above (kernels are distributed by NAIF; DE ephemerides are US Government works). **Stated limit of this evidence:**
JPL's page is a distribution and documentation page, not a licence page. The rights position for the `.bsp` files
rests on the NAIF rules, and a reviewer should treat "US Government work, no copyright in the US" as the *reason* it
is unencumbered, not as a quoted term — because JPL does not quote it on that page. This is exactly the kind of
distinction that should be settled in the licensing review rather than asserted by me.

**Scientific reference, now recorded** (it was missing from the inventory):
Park, R.S., Folkner, W.M., Williams, J.G., Boggs, D.H. (2021), "The JPL Planetary and Lunar Ephemerides DE440 and
DE441", *The Astronomical Journal* 161(3):105, DOI 10.3847/1538-3881/abd414 — cited by the JPL page as "(Park et al.,
2021)" and traceable from it.

### 2.3 Test-only NuGet packages

Method: each `.nupkg` was downloaded from `https://api.nuget.org/v3-flatcontainer/` and **its own `.nuspec` read**;
where the nuspec names a licence expression, the licence text was then fetched from the repository commit the nuspec
itself records. Nothing below is from memory.

| Package | nuspec `<license>` | Repository commit recorded in the nuspec | Licence file quoted |
| --- | --- | --- | --- |
| `Microsoft.NET.Test.Sdk` 17.14.1 | `MIT` (`https://licenses.nuget.org/MIT`) | `github.com/microsoft/vstest @ 490850ae` | `LICENSE` at tag `v17.14.1`: "The MIT License (MIT) / Copyright (c) Microsoft Corporation" |
| `xunit` 2.9.3 | `Apache-2.0` | `github.com/xunit/xunit @ 9712244020d385955d33136b3fe3e87de43539cd` | `license.txt` at that commit — quoted below |
| `xunit.assert` 2.9.3 | `Apache-2.0` | same | same file |
| `xunit.core` 2.9.3 | `Apache-2.0` | same | same file |
| `xunit.extensibility.core` 2.9.3 | `Apache-2.0` | same | same file |
| `xunit.analyzers` 2.3.0 | `Apache-2.0` | `github.com/xunit/xunit.analyzers @ 49b7e400` | declares the same expression |
| `xunit.runner.visualstudio` 3.1.4 | `Apache-2.0` | `github.com/xunit/visualstudio.xunit @ 50e68bbb` | `License.txt` at that commit — same wording |
| `xunit.abstractions` 2.0.3 (transitive) | **no `<license>` element**; only `licenseUrl = raw.githubusercontent.com/xunit/xunit/master/license.txt` | none recorded | see note below |

**Quoted licence text (xunit, at commit `9712244020d385955d33136b3fe3e87de43539cd`, file `license.txt`):**

> "Unless otherwise noted, the source code here is covered by the following license:
>
> Copyright (c) .NET Foundation and Contributors
> All Rights Reserved
>
> Licensed under the Apache License, Version 2.0 (the \"License\"); you may not use this file except in compliance with
> the License. You may obtain a copy of the License at
>
> http://www.apache.org/licenses/LICENSE-2.0
>
> Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on
> an \"AS IS\" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the
> specific language governing permissions and limitations under the License."

**`xunit.abstractions` 2.0.3 — a real gap, found by reading rather than assuming.** Its nuspec has no `<license>`
element at all, only a legacy `licenseUrl` pointing at `master/license.txt` of the xunit repository, which is a moving
target. The implication is not doubtful in practice (the same project, the same Apache-2.0 file), but **the package
does not state its licence**, and a machine check that trusts `<license>` will report it as unknown. Recorded as
unknown-with-rationale rather than silently rounded up to "Apache-2.0 like the rest".

**SHA-256 of the packages actually inspected** (so the reading can be reproduced against the same bytes):

```
04fae949b8e5201ecfa07fa8702baca8c0eb3198114194b3793789cc72bf23d7  xunit.2.9.3.nupkg
5b54674b105a5f2cbd5e6e0e67956a1b67e082d94eaa7fddbc803110e6eff2d4  xunit.runner.visualstudio.3.1.4.nupkg
9995330c5bc5a7bc759caadc9d17748616cdbb983c11062df122a74607614ff0  microsoft.net.test.sdk.17.14.1.nupkg
```

**Redistribution.** All of these are test-only and `IsPackable=false`; none is referenced by a shipped project, so no
attribution obligation attaches to the product artefact. Azure DevOps/GitHub-hosted runners download them at build
time. If a future release ever ships a test runner, that changes and this section must be revisited.

## 3. Independence: no copied, translated or derived Swiss Ephemeris code

The mechanical part is already asserted by tests (`No_swiss_ephemeris_package_source_or_asset_exists`,
`No_project_references_a_product_project`): no package reference, no source file name, no `.se1` asset, in any project
of the astronomy solution.

That is **absence of debris**, not proof of independent development. What makes the second claim is the process record:

| Component | Where the numbers come from | Independent of Swiss Ephemeris? |
| --- | --- | --- |
| Time scales (`TimeScales`), precession/nutation (`PrecessionNutation`), sidereal time, ayanamsa | Published IAU/Meeus formulae, cited in source | Yes — formulas are public scientific literature |
| Altitude crossing solver (`AltitudeCrossingSolver`) | Root finding written in-repo | Yes |
| House geometry (`HouseGeometry`) | Definitions written in-repo | Yes |
| Body positions | CSPICE `spkezr_c` against DE440/DE441 | Yes — a different provider, not a re-implementation |

The boundary that matters legally: **no Swiss Ephemeris source was read in order to write this code**. The parity
fixtures (`astronomy/parity-fixtures.json`) carry reference values from JPL Horizons, not from Swiss Ephemeris output;
that is the independent cross-check AST-09 (#74) depends on.

**Declared limitation:** this table is a statement of process by the implementer, and a process claim is exactly the
kind of claim that a reviewer, not the implementer, has to accept. It is written to be falsifiable: if anyone finds a
translated Swiss Ephemeris routine, the table above names where it would have to live.

## 4. What has to be decided, not by me

1. **Is the NAIF SPICE terms-of-use position acceptable for a commercial product?** The quoted text says yes, with no
   fee, no support obligation from NAIF, an AS-IS disclaimer, and a recipient indemnity to Caltech and NASA. A
   lawyer may want the indemnity acknowledged explicitly.
2. **Does "US Government work" for the `.bsp` files need its own statement?** JPL's page does not state it. If the
   reviewer requires a quoted term rather than a conclusion, the kernel files' internal comment blocks are the next
   place to look — and they can only be read from a deployment image, not from this repository.
3. **`xunit.abstractions` has no `<license>`.** Accept the project-level Apache-2.0, or pin a package that states it.

## 5. Mechanical gate, and what it can and cannot claim

`DependencyInspectionTests` gained two checks in this story:

- every `PackageReference` in the astronomy solution appears in the inventory (existing); and
- the inventory must state the **provenance source** for each third-party item, so an entry cannot be reduced to a bare
  licence name.

What this gate **cannot** do is fetch a licence, verify a licence expression, or decide whether a licence is
acceptable. Those need network access and a human. A gate that claimed to do them would be a green check with no
evidence behind it — the same defect as an unverified number. The honest shape is: the gate fails the build on an
unlisted dependency, and the review named in AC-3 covers the rest.

## 6. Status

| AC | State |
| --- | --- |
| 1 — inventory covers every dependency with provenance, licence, attribution, reference | **Evidence gathered and quoted.** Gaps stated (kernel provenance, `xunit.abstractions`). |
| 2 — independent-development process documented; no copied Swiss code | **Documented; mechanically asserted for absence of debris; process claim awaiting review.** |
| 3 — licensing review approved, approver and date named | **Not done. This is a human act and this document cannot self-approve it.** |
| 4 — review precedes production release; blocks AST-12 (#77) | **Gate intact: nothing here unblocks #77.** |
