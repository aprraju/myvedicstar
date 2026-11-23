#!/usr/bin/env bash
# Provision the NAIF SPICE toolkit and ephemeris kernels (api#173).
#
# Owner decision (recorded on the issue): fetch at build. Nothing is vendored into git, and nothing is fetched at
# RUN time - the artefacts are downloaded and verified while the image is built, then baked into the layer.
#
# Why a script and not inline Dockerfile RUN steps: the same three rules apply to the CI test image, the deploy image
# and any future host, and a copy of them in each Dockerfile is how one of them quietly drifts to "latest".
#
#   1. PINNED. N0067 (released 2022-01-03) is the version. "Latest" is not a version and must not appear here.
#   2. VERIFIED. Every artefact is checked against a SHA-256 recorded below. A mismatch is a hard failure.
#   3. VERIFIED-OR-NOTHING. If a checksum is UNVERIFIED, this script fails rather than downloading an unverified
#      artefact. That branch exists because the initial commit could not fetch the hashes from the build host; it is
#      not a placeholder to be removed - it is the honest state, and the operator fills it in.
#
# Usage: provision-spice.sh <destination-dir>
#   Destination is where cspice's shared library and the kernels land; the caller copies them into the image.
set -euo pipefail

readonly SPICE_VERSION="N0067"
readonly SPICE_RELEASED="2022-01-03"
readonly BASE="https://naif.jpl.nasa.gov/pub/naif"

# --- Pinned artefacts ---------------------------------------------------------------------------------------------
# path on disk name in the image | source URL | SHA-256
#
# The toolkit tarball is the PC_Linux_GCC_64bit C package; its listing timestamp (2022-01-03) is the N0067 release
# date, which is the cross-check that this path and this version actually describe the same artefact.
TOOLKIT_URL="${BASE}/toolkit/C/PC_Linux_GCC_64bit/packages/cspice.tar.Z"
TOOLKIT_SHA256="60a95b51a6472f1afe7e40d77ebdee43c12bb5b8823676ccc74692ddfede06ce"

KERNELS=(
  "${BASE}/generic_kernels/spk/planets/de440.bsp|de440.bsp"
  "${BASE}/generic_kernels/lsk/naif0012.tls|naif0012.tls"
  "${BASE}/generic_kernels/pck/pck00011.tpc|pck00011.tpc"
)
# SHA-256 per kernel, in the same order as KERNELS, computed from the fetched artefacts on 2026-11-22.
#
# CROSS-CHECK, and this is the part that matters: NAIF publishes aa_checksums.txt beside the planetary kernels, and
# it lists MD5, not SHA-256. The MD5 of the de440.bsp these hashes describe is c9d581bfd84209dbeee8b1583939b148,
# which matches NAIF's published value exactly. Two independent digests agreeing on the same artefact is evidence;
# our SHA-256 alone would only be a record of what we happened to receive.
KERNEL_SHA256=(
  "a4ce9bf9b3282becc9f4b2ac3cebe03a2ae7599981aabd7265fd8482fff7c4b5"
  "678e32bdb5a744117a467cd9601cd6b373f0e9bc9bbde1371d5eee39600a039b"
  "3dff7b1dbeceaa01f25467767d3fa25816051c85d162d1edf04acb310ee28bb1"
)

die() { echo "provision-spice: $*" >&2; exit 1; }

verify() {
  local file="$1" expected="$2"
  if [ "${expected}" = "UNVERIFIED" ]; then
    die "no SHA-256 recorded for $(basename "${file}"). Refusing to bake an unverified artefact into an image.
     Record the hash from the NAIF listing (or a trusted mirror) and re-run. This is a deliberate stop, not a bug:
     a provisioning step that trusts whatever the network returned is how an unverified binary reaches production."
  fi
  local actual
  actual="$(sha256sum "${file}" | cut -d' ' -f1)"
  [ "${actual}" = "${expected}" ] || die "checksum mismatch for $(basename "${file}"): expected ${expected}, got ${actual}"
}

main() {
  local dest="${1:?usage: provision-spice.sh <destination-dir>}"
  mkdir -p "${dest}/spice"
  # Work dir is cleaned by a trap that references a GLOBAL name. A local here would be out of scope by the time
  # the trap fires, which under `set -u` aborts cleanup with an unbound-variable error - a spurious failure
  # printed AFTER a successful run, the worst kind of noise to leave in a provisioning step.
  SPICE_WORK="$(mktemp -d)"
  trap "rm -rf ${SPICE_WORK:-}" EXIT

  echo "provision-spice: fetching SPICE ${SPICE_VERSION} (released ${SPICE_RELEASED}) - pinned, not latest"
  curl --fail --silent --show-error --location --retry 3 -o "${SPICE_WORK}/cspice.tar.Z" "${TOOLKIT_URL}"
  verify "${SPICE_WORK}/cspice.tar.Z" "${TOOLKIT_SHA256}"

  # cspice.tar.Z is a compress(1) archive of a POSIX tar. uncompress is not on every slim base image, so the
  # extraction runs on the build stage, never in the chiseled runtime.
  ( cd "${SPICE_WORK}" && uncompress -c cspice.tar.Z | tar -xf - )

  # IMPORTANT, and this cost a run to learn: the N0067 PC_Linux_GCC_64bit package ships ONLY STATIC archives
  # (cspice/lib/cspice.a, csupport.a). There is no shared object in it. The product P/Invokes "cspice" as a shared
  # library, so the archive has to be relinked into one. --whole-archive is required: without it the linker drops
  # every CSPICE object the archive does not already reference, and the resulting .so loads but exports nothing.
  local archive="${SPICE_WORK}/cspice/lib/cspice.a"
  [ -f "${archive}" ] || die "cspice/lib/cspice.a not found in the extracted toolkit - the pinned URL no longer matches the layout it was pinned against"
  cc -shared -fPIC -o "${dest}/spice/libcspice.so" -Wl,--whole-archive "${archive}" -Wl,--no-whole-archive -lm
  [ -s "${dest}/spice/libcspice.so" ] || die "relinking cspice.a produced no shared object; the build stage has no working C compiler"

  local i=0
  for entry in "${KERNELS[@]}"; do
    local url="${entry%%|*}" name="${entry##*|}"
    curl --fail --silent --show-error --location --retry 3 -o "${dest}/spice/${name}" "${url}"
    verify "${dest}/spice/${name}" "${KERNEL_SHA256[${i}]}"
    i=$((i + 1))
  done

  # The version is printed in a machine-readable form so the CONSUMERS do not have to state it. A workflow that
  # hardcodes "N0067" in its own assertion keeps a second copy of the pin, and that copy is the one that goes stale
  # when this file changes. Callers capture this line instead (api#173).
  echo "provision-spice: toolkit=${SPICE_VERSION} released=${SPICE_RELEASED}"
  echo "provision-spice: ${dest}/spice now holds libcspice.so and ${#KERNELS[@]} kernels"
  ls -l "${dest}/spice"
}

main "$@"
