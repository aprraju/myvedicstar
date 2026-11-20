# Third-party notices

This file is the attribution surface for everything the astronomy engine depends on that we did not write. It is
checked into the repository so an artefact can carry it verbatim; the evidence for each licence — quoted text, source
URL, fetch date, and package hashes where applicable — is in `FR-08-LICENSING-EVIDENCE.md`.

## NASA/JPL NAIF SPICE toolkit and kernels

Portions of this product use the SPICE system, provided by the Navigation and Ancillary Information Facility (NAIF),
NASA/JPL-Caltech.

    The SPICE system is implemented and maintained by Caltech/Jet Propulsion Laboratory under contract to the
    National Aeronautics and Space Administration. It is sponsored by the Planetary Science Division of NASA's
    Science Mission Directorate.

    THIS SOFTWARE AND ANY RELATED MATERIALS WERE CREATED BY THE CALIFORNIA INSTITUTE OF TECHNOLOGY (CALTECH) UNDER A
    U.S. GOVERNMENT CONTRACT WITH THE NATIONAL AERONAUTICS AND SPACE ADMINISTRATION (NASA). THE SOFTWARE IS TECHNOLOGY
    AND SOFTWARE PUBLICLY AVAILABLE UNDER U.S. EXPORT LAWS AND IS PROVIDED "AS-IS" TO THE RECIPIENT WITHOUT WARRANTY OF
    ANY KIND ... RECIPIENT BEARS ALL RISK RELATING TO QUALITY AND PERFORMANCE OF THE SOFTWARE AND ANY RELATED
    MATERIALS, AND AGREES TO INDEMNIFY CALTECH AND NASA FOR ALL THIRD-PARTY CLAIMS RESULTING FROM THE ACTIONS OF
    RECIPIENT IN THE USE OF THE SOFTWARE.

    Source: https://naif.jpl.nasa.gov/naif/rules.html (full text, unelided, in FR-08-LICENSING-EVIDENCE.md §2.1)

Recommended reference for SPICE:

    Acton, C.H.; "Ancillary Data Services of NASA's Navigation and Ancillary Information Facility;"
    Planetary and Space Science, Vol. 44, No. 1, pp. 65-70, 1996. DOI 10.1016/0032-0633(95)00107-7

Ephemeris kernels DE440 and DE441 are products of the JPL Solar System Dynamics group and are distributed by NAIF:

    Park, R.S., Folkner, W.M., Williams, J.G., Boggs, D.H. (2021), "The JPL Planetary and Lunar Ephemerides DE440
    and DE441", The Astronomical Journal 161(3):105, DOI 10.3847/1538-3881/abd414

## Runtime NuGet dependencies — none

No third-party runtime library is redistributed in the engine packages; the native toolkit and the kernels are
supplied by the deployment image (see `PACKAGING.md` §2.2).

## Test-only

`Microsoft.NET.Test.Sdk` (MIT, © Microsoft Corporation) and `xunit`/`xunit.runner.visualstudio` and their transitive
xunit packages (Apache-2.0, © .NET Foundation and Contributors) are used to test and are not redistributed in any
product artefact.
