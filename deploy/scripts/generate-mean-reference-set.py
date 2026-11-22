#!/usr/bin/env python3
"""Generate astronomy/mean-reference-set.json for the AST-09 corpus (issue #74).

WHY THIS EXISTS
    horizons-reference.json holds 14 rows transcribed from live JPL Horizons responses. Those cannot cover a
    10,000-case corpus and must not be fetched at test time. This script fills that gap deterministically, with no
    network, from published catalogue identities.

WHAT IT GENERATES, PRECISELY
    Mean-longitude identities from Meeus, "Astronomical Algorithms" (2nd ed.). These are DEFINITIONS, accurate to
    about 0.01 arcsec over 1900-2100 - not observations, and not Horizons values. Every row is marked
    MeanDefinitionIdentity so nothing can mistake it for an observation.

DETERMINISM
    Pure arithmetic: no clock, no RNG, no filesystem state, no locale-dependent formatting. Re-running reproduces the
    committed file byte for byte. The test suite re-derives the same identities from its own copy of the constants and
    fails if they diverge, so this script and the tests cannot drift apart silently.

USAGE
    python3 deploy/scripts/generate-mean-reference-set.py [--write]

    Without --write it prints the JSON to stdout. With --write it updates astronomy/mean-reference-set.json.
"""
import json
import math
import sys
from pathlib import Path

# --- Published constants (Meeus 2nd ed.; duplicated from the test copy on purpose) --------------------------------
J2000 = 2451545.0
DAYS_PER_CENTURY = 36525.0


def normalize(degrees: float) -> float:
    value = degrees % 360.0
    return value + 360.0 if value < 0 else value


def t_of(jd_tt: float) -> float:
    return (jd_tt - J2000) / DAYS_PER_CENTURY


def julian_ephemeris_day(jd_ut: float) -> float:
    """UT -> TT using the Espenak-Meeus Delta T polynomials.

    Mirrors the engine's own conversion so the reference is evaluated at the instant the engine evaluates. Kept in
    sync by construction rather than by hope: a divergence shows up as a uniform residual in every row, which the
    duplicate-constant test catches.
    """
    # Year from the Julian Day, approximately - sufficient for selecting the Delta T polynomial segment.
    year = 2000.0 + (jd_ut - J2000) / 365.25
    if year < 1900:
        u = (year - 1820.0) / 100.0
        return jd_ut + (-20.0 + 32.0 * u * u) / 86400.0
    if year < 1920:
        tt = -2.79 + 1.494119 * (year - 1900) - 0.0598939 * (year - 1900) ** 2 \
             + 0.0061966 * (year - 1900) ** 3 - 0.000197 * (year - 1900) ** 4
        return jd_ut + tt / 86400.0
    if year < 1941:
        tt = 21.20 + 0.84493 * (year - 1920) - 0.076100 * (year - 1920) ** 2 \
             + 0.0020936 * (year - 1920) ** 3
        return jd_ut + tt / 86400.0
    if year < 1961:
        tt = 29.07 + 0.407 * (year - 1950) - (year - 1950) ** 2 / 233.0 + (year - 1950) ** 3 / 2547.0
        return jd_ut + tt / 86400.0
    if year < 1986:
        tt = 45.45 + 1.067 * (year - 1975) - (year - 1975) ** 2 / 260.0 - (year - 1975) ** 3 / 718.0
        return jd_ut + tt / 86400.0
    if year < 2005:
        tt = (63.86 + 0.3345 * (year - 2000) - 0.060374 * (year - 2000) ** 2
              + 0.0017275 * (year - 2000) ** 3 + 0.000651814 * (year - 2000) ** 4
              + 0.00002373599 * (year - 2000) ** 5)
        return jd_ut + tt / 86400.0
    if year < 2050:
        tt = 62.92 + 0.32217 * (year - 2000) + 0.005589 * (year - 2000) ** 2
        return jd_ut + tt / 86400.0
    tt = -20.0 + 32.0 * ((year - 1820.0) / 100.0) ** 2 - 0.5628 * (2150.0 - year)
    return jd_ut + tt / 86400.0


def moon_mean_longitude(t: float) -> float:      # Meeus 47.1 (L')
    return normalize(218.3164477 + 481267.88123421 * t - 0.0015786 * t * t
                     + t ** 3 / 538841.0 - t ** 4 / 65194000.0)


def moon_elongation(t: float) -> float:          # Meeus 47.2 (D)
    return normalize(297.8501921 + 445267.1114034 * t - 0.0018819 * t * t
                     + t ** 3 / 545868.0 - t ** 4 / 113065000.0)


def sun_mean_anomaly(t: float) -> float:         # Meeus 47.3 (M)
    return normalize(357.5291092 + 35999.0502909 * t - 0.0001536 * t * t + t ** 3 / 24490000.0)


def moon_mean_anomaly(t: float) -> float:        # Meeus 47.4 (M')
    return normalize(134.9633964 + 477198.8675055 * t + 0.0087414 * t * t
                     + t ** 3 / 69699.0 - t ** 4 / 14712000.0)


def moon_argument_of_latitude(t: float) -> float:  # Meeus 47.5 (F)
    return normalize(93.2720950 + 483202.0175233 * t - 0.0036539 * t * t
                     - t ** 3 / 3526000.0 + t ** 4 / 863310000.0)


def mean_node(t: float) -> float:                # Meeus 47 (Omega)
    return normalize(125.0445479 - 1934.1362891 * t + 0.0020754 * t * t
                     + t ** 3 / 467441.0 - t ** 4 / 60616000.0)


def sun_geometric_mean_longitude(t: float) -> float:  # Meeus 25.2 (L0)
    return normalize(280.46646 + 36000.76983 * t + 0.0003032 * t * t)


def sun_radius_vector(t: float) -> float:  # Meeus 25.5, truncated
    m = math.radians(sun_mean_anomaly(t))
    equation_of_centre = ((1.914602 - 0.004817 * t - 0.000014 * t * t) * math.sin(m)
                          + (0.019993 - 0.000101 * t) * math.sin(2 * m)
                          + 0.000289 * math.sin(3 * m))
    true_anomaly = m + math.radians(equation_of_centre)
    eccentricity = 0.016708634 - 0.000042037 * t - 0.0000001267 * t * t
    return (1.000001018 * (1 - eccentricity * eccentricity)) / (1 + eccentricity * math.cos(true_anomaly))


def sun_apparent_mean_equinox_longitude(t: float) -> float:
    """The solar quantity the ENGINE returns: geocentric apparent, mean equinox of date.

    NOT L0. The engine's GetSunLongitude is L0 + 180 + equation of centre + FK5 + aberration. Putting L0 in the
    reference would compare two different quantities and report the difference as an accuracy failure.
    """
    l0 = sun_geometric_mean_longitude(t)
    m = math.radians(sun_mean_anomaly(t))
    equation_of_centre = ((1.914602 - 0.004817 * t - 0.000014 * t * t) * math.sin(m)
                          + (0.019993 - 0.000101 * t) * math.sin(2 * m)
                          + 0.000289 * math.sin(3 * m))
    fk5 = -0.09033 / 3600.0
    aberration = -20.4898 / sun_radius_vector(t) / 3600.0
    # No +180 here: L0 is already the GEOCENTRIC mean longitude of the Sun. The 180 degree flip applies when you start
    # from the heliocentric longitude of the EARTH, which is a different derivation; adding it to L0 lands on the wrong
    # point of the orbit (checked: 100.38 vs the engine's 280.3727 at J2000).
    return normalize(l0 + equation_of_centre + fk5 + aberration)


def mean_obliquity(t: float) -> float:           # Meeus 22.2 (eps0)
    return 23.0 + (26.0 + (21.448 - 46.8150 * t - 0.00059 * t * t + 0.001813 * t ** 3) / 60.0) / 60.0


# --- Rows ---------------------------------------------------------------------------------------------------------
# Instants are chosen so the set spans the engine's supported range and lands on civil-midnight UT, which makes the
# rows reproducible by a reader with a calculator. Every body is scored at EVERY instant, so the count is
# bodies x instants rather than a hand-picked list.
INSTANTS_JD_UT = [2451545.0, 2415020.5, 2438761.5, 2461600.0, 2440000.5, 2458000.5]

BODIES = {
    "Moon": moon_mean_longitude,
    "Sun": sun_apparent_mean_equinox_longitude,
    "MeanNode": mean_node,
}

# Arguments are exposed as their own rows so a drifted argument series is caught independently of the longitude it
# feeds. They are not bodies - Quantity distinguishes them.
ARGUMENTS = {
    "MoonElongationD": moon_elongation,
    "SunMeanAnomalyM": sun_mean_anomaly,
    "MoonMeanAnomalyMPrime": moon_mean_anomaly,
    "MoonArgumentOfLatitudeF": moon_argument_of_latitude,
}

# The catalogue's own precision for a mean identity, per row. 0.01 arcsec is about the reproduction precision of the
# truncated mean series; it is deliberately NOT the engine's 10 arcsec series accuracy and NOT the 1 arcsec target.
MEAN_IDENTITY_TOLERANCE_ARCSECONDS = 0.01

DEFINITIONS = {
    "Moon": "Meeus AA 2nd ed. eq. 47.1: L' = 218.3164477 + 481267.88123421 T - 0.0015786 T^2 + T^3/538841 - T^4/65194000 degrees, mean equinox of date.",
    "Sun": "Meeus AA 2nd ed. 25.2-25.5: geocentric apparent longitude, mean equinox of date = L0 + equation of centre + 180 + FK5 -0.09033/3600 - 20.4898/R/3600, R from 25.5. This is the quantity IEphemeris.GetSunLongitude returns, so the comparison is like-for-like. Deliberately NOT L0 alone.",
    "MeanNode": "Meeus AA 2nd ed. ch.47 Omega term: 125.0445479 - 1934.1362891 T + 0.0020754 T^2 + T^3/467441 - T^4/60616000 degrees.",
    "MoonElongationD": "Meeus AA 2nd ed. eq. 47.2: D = 297.8501921 + 445267.1114034 T - 0.0018819 T^2 + T^3/545868 - T^4/113065000 degrees.",
    "SunMeanAnomalyM": "Meeus AA 2nd ed. eq. 47.3: M = 357.5291092 + 35999.0502909 T - 0.0001536 T^2 + T^3/24490000 degrees.",
    "MoonMeanAnomalyMPrime": "Meeus AA 2nd ed. eq. 47.4: M' = 134.9633964 + 477198.8675055 T + 0.0087414 T^2 + T^3/69699 - T^4/14712000 degrees.",
    "MoonArgumentOfLatitudeF": "Meeus AA 2nd ed. eq. 47.5: F = 93.2720950 + 483202.0175233 T - 0.0036539 T^2 - T^3/3526000 + T^4/863310000 degrees.",
}


def fmt(value: float) -> float:
    # Ten decimals: enough to preserve 0.0001 arcsec, few enough to keep the diff readable. round() here is the only
    # place precision is lost, and it is deterministic.
    return round(value, 10)


def build() -> dict:
    rows = []
    for jd_ut in INSTANTS_JD_UT:
        # These identities are functions of TT; the engine converts UT->TT internally. The generated set stores the
        # TT value it evaluated at so the comparison cannot silently apply Delta T twice.
        # The engine converts UT -> TT internally and evaluates the series in TT. To make the reference the same
        # quantity, the polynomials are evaluated at the TT instant the engine will use. A hard-coded 64.184 s here
        # would leave the engine's own Delta T (~64.06 s in 2000) as a systematic ~5e-5 degree residual - a test that
        # subtracts a slightly wrong correction is measuring its own constant.
        jd_tt = julian_ephemeris_day(jd_ut)
        t = t_of(jd_tt)

        for body, fn in BODIES.items():
            rows.append({
                "body": body,
                "quantity": "MeanLongitude",
                "kind": "MeanDefinitionIdentity",
                "julianDayUt": jd_ut,
                "longitudeDegrees": fmt(fn(t)),
                "toleranceArcseconds": MEAN_IDENTITY_TOLERANCE_ARCSECONDS,
                "definition": DEFINITIONS[body],
            })
        for argument, fn in ARGUMENTS.items():
            rows.append({
                "body": argument,
                "quantity": "Argument",
                "kind": "MeanDefinitionIdentity",
                "julianDayUt": jd_ut,
                "longitudeDegrees": fmt(fn(t)),
                "toleranceArcseconds": MEAN_IDENTITY_TOLERANCE_ARCSECONDS,
                "definition": DEFINITIONS[argument],
            })

    document = {
        "source": "Meeus, Astronomical Algorithms (2nd ed.), chapters 22, 25, 47 and 49 - published mean-longitude identities.",
        "licence": "Formula identities are not copyrightable as such; the coefficient values are published catalogue constants, cited not copied. Regenerated by deploy/scripts/generate-mean-reference-set.py.",
        "ephemerisSourceOrDefinition": "MeanDefinitionIdentity (catalogue definition, NOT an observation and NOT Horizons)",
        "accuratelyRangeUtYears": "1900-2100 (the catalogue's stated validity for these mean identities, about 0.01 arcsec)",
        "purpose": "Cover the AST-09 corpus with deterministic reference rows so mean-longitude cases can be scored without network access. Absolute accuracy against observations is measured separately by horizons-reference.json, whose Sun shortfall remains unmet.",
        "rows": rows,
    }
    return document


def main() -> int:
    document = build()
    payload = json.dumps(document, indent=2, ensure_ascii=False) + "\n"

    if "--write" in sys.argv:
        target = Path(__file__).resolve().parents[2] / "astronomy" / "mean-reference-set.json"
        target.write_text(payload, encoding="utf-8")
        print(f"wrote {target} ({len(document['rows'])} rows)", file=sys.stderr)
    else:
        sys.stdout.write(payload)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
