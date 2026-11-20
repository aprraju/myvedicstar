# Packaging the astronomy engine for CI/CD (AST-11, api#76)

The engine is shipped as **five versioned NuGet packages** and proven usable by a consumer that never sees this
repository. This document covers what the artefact is, how it is built and verified, how it is configured, what it
does and does not cover, and which limits are real rather than documented away.

## 1. What is packaged

| Package | Contains | Ships native code |
| --- | --- | --- |
| `MyVedicStar.Astronomy.Core` | `IAstronomyEngine`, value types, capability boundary, parity fixtures | no |
| `MyVedicStar.Astronomy.Coordinates` | angle maths, frame conversion, sidereal time, ayanamsa | no |
| `MyVedicStar.Astronomy.Events` | altitude-crossing solver, rise/set/transit, ingress | no |
| `MyVedicStar.Astronomy.Houses` | ascendant and house geometry | no |
| `MyVedicStar.Astronomy.Jpl.Provider` | `SpiceAstronomyEngine`, CSPICE P/Invoke surface, `SpiceConfiguration` | **no** - see 2.2 |

All five are packed at one version (`-p:PackageVersion=`), so a consumer pins a single number. The version is the
only thing CI changes; nothing else about a release is hand-edited.

Nothing in the product (`src/MyVedicStar.*`, `src/MyVedicDay.*`) is packaged. The product consumes the engine; the
engine knows nothing about the product. `astronomy/SolutionDependencies.md` and
`MyVedicStar.Astronomy.Tests/DependencyInspectionTests.cs` enforce that direction mechanically.

## 2. Build and prove

### 2.1 What is verified, and by what

`.github/workflows/astronomy-package.yml` runs two jobs:

| Job | Proves | Fails when |
| --- | --- | --- |
| `package-consumer` | the packed artefact restores, builds and runs **outside the checkout** | the artefact needs the repository, a project reference, or a path that only exists in the build tree |
| `astronomy-runtime` | the artefact runs in a container that has nothing, and **refuses** cleanly when the toolkit is absent | the engine silently degrades, or the image only "builds" |

The consumer is `astronomy/MyVedicStar.Astronomy.PackageCheck`. It is **not** in `MyVedicStar.Astronomy.slnx`, has
**no** `ProjectReference`, and resolves its packages from a folder feed of the `dotnet pack` output only. CI copies
it to `$RUNNER_TEMP/outside-checkout` and asserts that its working directory is outside `$GITHUB_WORKSPACE`
before running it. That assertion is the difference between a proof and a formality: a probe that runs beside the
repository can pass on files the package does not contain.

The probe checks, in `package` mode: the provider assembly loads **from its package**; construction works with no
native library; capabilities are readable; the supported span is the intersection of the declared kernels and the
product floor; an out-of-span date is refused with a reason; provenance names the toolkit source and each kernel.
In `--expect-no-toolkit` mode it additionally asserts that a calculation raises `SpiceUnavailableException`
naming `cspice`, rather than answering with a substitute.

### 2.2 Toolkit and kernels are not in the package

The CSPICE native library and the SPICE kernels are **not** in these packages, and that is deliberate:

- **Kernels are data, not code.** DE440 is ~114 MB and DE441 is ~3.2 GB. Shipping them in a NuGet package inflates
  every restore for every consumer, including ones that never calculate.
- **The provider must not be able to fetch them.** `SpiceAstronomyEngine` raises
  `SpiceUnavailableException` when `cspice` is missing; it has no download path. A package that "helpfully"
  fetched a kernel at start-up would make an unversioned network dependency part of a numerical result.
- **A deployment image supplies both.** The host image installs the NAIF toolkit and mounts the kernels at the
  configured paths; `astronomy/runtime/Dockerfile` shows the shape of that image and, in CI, the honest empty case.

**Consequence, stated plainly:** a consumer who installs the packages and does nothing else gets a working types,
configuration and capability surface, and a provider that refuses every calculation with a message naming the missing
dependency. That is the intended behaviour, not an incomplete package.

### 2.3 What "the artefact runs outside the build tree" means here

It means: with the repository absent, the package's assemblies load, types resolve, configuration derives a span,
and failure modes are the designed ones. Numerical correctness is **not** claimed by this story - the parity gate
against JPL Horizons covers that (`astronomy/parity-fixtures.json`, `ParityGate`), and its validation evidence is
AST-09 (#74).

Linear and CI-verified:

```bash
# 1. select the SDK global.json names - a mismatched SDK fails locally before it fails in CI
dotnet --version

# 2. pack all five at one version
feed=/tmp/astronomy-packages && mkdir -p $feed
for p in Core Coordinates Events Houses Jpl.Provider; do
  dotnet pack "astronomy/MyVedicStar.Astronomy.$p" -c Release -o $feed -p:PackageVersion=0.2.0
done

# 3. consume them from somewhere that is not this repository
work=/tmp/outside-checkout && rm -rf $work && mkdir -p $work
cp -r astronomy/MyVedicStar.Astronomy.PackageCheck $work/
# write a nuget.config with only "$feed" plus nuget.org (CI does this; see the workflow)
cd $work/MyVedicStar.Astronomy.PackageCheck
dotnet build -c Release -p:ProbeFeed=$feed -p:ProbePackageVersion=0.2.0
dotnet bin/Release/net10.0/MyVedicStar.Astronomy.PackageCheck.dll
dotnet bin/Release/net10.0/MyVedicStar.Astronomy.PackageCheck.dll --expect-no-toolkit
```

The toolkit-free expectations (`--expect-no-toolkit`) hold on any machine without CSPICE, so that half is runnable
locally; the package half needs a clean package-only restore and is what CI guarantees.

### 2.4 Publishing

CI publishes the packages as a workflow artefact (`astronomy-packages`) so release engineering can push them. It does
**not** push to GitHub Packages on every build: a version that ships should be a decision, not a side effect of a
merge. To publish, push the artefact from an approved release job with the repository `GITHUB_TOKEN`.

## 3. Configuration

`SpiceConfiguration` is the whole contract with the host.

| Member | Purpose | Default |
| --- | --- | --- |
| `ToolkitSource` | provenance string for the toolkit | NAIF, public domain |
| `ReportedToolkitVersion` | version the image reports; null means unverifiable | null |
| `Kernels` | mount points with the span each really covers | `SpiceConfiguration.Default` |
| `SupportedDateRange` | intersection of kernel coverage and the product floor | derived |
| `IsServiceable(date)` | the single serviceability gate both public gates delegate to | - |

`SpiceConfiguration.Default` declares `/var/spice/de440.bsp` (1550-01-01..2650-01-01), `/var/spice/de441.bsp`
(`DateOnly.MinValue`..`MaxValue`, the representable window of a much wider kernel), `/var/spice/naif0012.tls`
(leap seconds) and `/var/spice/pck00011.tpc` (planetary constants). Change the paths if the host mounts elsewhere;
nothing else needs to change, and the derived span follows automatically.

`VerifyToolkitMatchesInventory(inventoryVersion)` is how a host checks that the image's CSPICE is the version
`astronomy/FR-08-DEPENDENCY-INVENTORY.md` claims. It **throws** on mismatch; an unverifiable licence claim is exactly
what FR-08 exists to prevent.

## 4. Supported date range

The supported span is **derived, never asserted**: `SupportedDateRange` intersects the declared kernel coverage with
`AstronomyCapabilities.MinimumSupportedDate`/`MaximumSupportedDate` (1800-01-01..2200-12-31). With the default
kernel set the derived span is exactly that product window.

Dates outside it are refused by `EnsureDateIsCovered`, which says which gate refused - the product floor or the
mounted kernels - because the two need different fixes. The probe asserts both a serviceable date and a refusal, so a
future change that widens coverage without a kernel cannot pass silently.

## 5. Limitations - stated, not omitted

1. **No kernels in the package or in the CI runtime image.** Every calculation refuses until a deployment mounts them.
2. **No numerical validation in this story.** AST-11 proves the artefact runs and its failure modes are right; it does
   not prove the numbers. AST-09 (#74) carries the accuracy matrix against JPL Horizons.
3. **DE441's declared span is narrowed to what `DateOnly` can express.** The kernel is wider; the type is not, and the
   declaration was narrowed to reality rather than an invented range.
4. **No default provider flip.** `IEphemeris` still resolves to the current provider in production. Switching is
   AST-12 (#77), after the parity gate passes and with approval.
5. **Package push is manual by design** (see 2.4); the CI artefact is the release candidate, not the release.
6. **The runtime image is a proof image.** It demonstrates absence-of-toolkit behaviour; it is not the deployment
   image. The deployment image adds the toolkit and kernels and is owned by the deployment pipeline.
7. **Toolkit licence and provenance are unverified at run time unless the host reports a version.** The engine returns
   null and `VerifyToolkitMatchesInventory` refuses, rather than pretending.

## 6. Migration and rollback

**Adoption.** Consumers add the five `PackageReference`s at one version and supply a `SpiceConfiguration` from
their host. Nothing else changes: the engine implements `IAstronomyEngine`, and the product's `IEphemeris` stays the
only surface Panchang, festivals and Kundali (#11-#34) call.

**Rollback.** The packages are additive. Rollback is re-pinning to the previous version, or not referencing them at
all: no product project references an astronomy package after this story, so removing the references restores the
prior state with no data migration. Kernel files live outside the package, so a package rollback never needs a kernel
change - and a kernel change never needs a package change, which is the point of separating them.

Provider-level rollback (which engine `IEphemeris` resolves to) is AST-12's decision, not this story's.

## 7. Where the evidence lives

| Acceptance criterion | Evidence |
| --- | --- |
| Deliverable - versioned artefact for CI/CD | `.github/workflows/astronomy-package.yml` `package-consumer` job; `astronomy-packages` workflow artefact |
| AC-1 - builds and tests independently of product APIs | `.github/workflows/astronomy-ci.yml` plus the probe's package-only restore |
| Deployment - no runtime kernel download; size and start-up recorded | `astronomy-runtime` job: image built, run, size and wall time written to the job summary |
| AC-12 partial - docs | this file; sections 1-6 |

Issue comment on api#76 carries the run IDs and measured numbers for a specific commit.
