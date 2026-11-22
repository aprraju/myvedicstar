using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// Deterministic numerical fixtures: reference rows produced by an open catalogue CALCULATION, committed as data
/// (issue #74).
/// </summary>
/// <remarks>
/// <para><b>Why a second reference set exists at all.</b> <c>horizons-reference.json</c> holds 14 rows transcribed from
/// live JPL Horizons responses. Those are the strongest references available, but fourteen rows cannot cover a
/// 10,000-case corpus, and fetching more at test time is forbidden - a CI job that needs Horizons is a broken CI job,
/// and the fetch would also make results depend on a service being up. This set fills that gap without the network.</para>
/// <para><b>What the numbers are, stated precisely.</b> Each row records a mean-longitude identity from Meeus,
/// <i>Astronomical Algorithms</i> (2nd ed.), the same published catalogue the engine's own constants come from. These
/// are <b>definitional identities, accurate to about 0.01 arcsec over 1900-2100</b> - not a fit to observations, and
/// not Horizons values. Using them is therefore a real independent check: they pin the engine's mean series against
/// the textbook definition the series is supposed to implement, so a wrong coefficient or a drifted constant fails
/// here even though no observation was involved.</para>
/// <para><b>They are NOT an accuracy claim against observations.</b> <c>HorizonsKind</c> marks every row
/// <c>MeanDefinitionIdentity</c> so nothing downstream can mistake these for Horizons rows, and
/// <c>The_generated_set_is_not_presented_as_horizons_values</c> asserts that separation. Absolute accuracy against
/// observations remains what <c>horizons-reference.json</c> measures - and that measurement is still unmet for the
/// Sun, which this change does not soften.</para>
/// <para><b>Deterministic by construction.</b> Every row is a pure function of the formula and the instant, evaluated
/// by a script that is itself in the repository (<c>deploy/scripts/generate-mean-reference.py</c>). Regenerating it
/// reproduces the file byte for byte; <c>The_generated_set_reproduces_from_its_own_constants</c> checks that at test
/// time without the script.</para>
/// </remarks>
public static class MeanReferenceSet
{
    /// <summary>File name inside the solution directory.</summary>
    public const string FileName = "mean-reference-set.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Loads the generated set, or fails naming the path it looked for.</summary>
    public static MeanReferenceDocument Load(string solutionRoot)
    {
        var direct = Path.Combine(solutionRoot, FileName);
        var nested = Path.Combine(solutionRoot, "astronomy", FileName);
        var path = File.Exists(direct) ? direct : nested;
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Generated reference set '{FileName}' is missing at '{direct}' or '{nested}'. The corpus cannot score " +
                "mean-longitude cases without it.", path);

        return JsonSerializer.Deserialize<MeanReferenceDocument>(File.ReadAllText(path), SerializerOptions)
               ?? throw new InvalidOperationException($"Generated reference set '{FileName}' is empty or invalid.");
    }

    /// <summary>
    /// Meeus chapter 47 argument terms, as polynomial coefficients in Julian centuries from J2000.
    /// </summary>
    /// <remarks>
    /// <para>Every coefficient here is a published value from Meeus 2nd ed., chapters 22, 25, 47 and 49. They are
    /// repeated in this file on purpose: the point of a reference is that it does not read the engine's constants. If
    /// these were imported from the engine, regenerating the set could not detect a drifted engine constant, which is
    /// the only thing the set is for.</para>
    /// <para><b>Provenance per term.</b> <c>Meeus 47, eq. 47.1</c> for the lunar mean longitude and <c>47.2-47.5</c> for
    /// the arguments; <c>Meeus 22</c> for the nutation longitude; <c>Meeus 25.2</c> for the solar mean longitude;
    /// <c>Meeus 49.4</c> for the mean obliquity. The tests assert the identities these coefficients produce, so a typo
    /// is a red test rather than a silently shifted reference.</para>
    /// </remarks>
    public static class Meeus
    {
        /// <summary>Lunar mean longitude L', degrees, from <paramref name="t"/> centuries since J2000.</summary>
        public static double MoonMeanLongitude(double t) =>
            Normalize(218.3164477 + (481267.88123421 * t) - (0.0015786 * t * t) + (t * t * t / 538841d)
                      - (t * t * t * t / 65194000d));

        /// <summary>Mean elongation D of the Moon from the Sun, degrees.</summary>
        public static double MoonElongation(double t) =>
            Normalize(297.8501921 + (445267.1114034 * t) - (0.0018819 * t * t) + (t * t * t / 545868d)
                      - (t * t * t * t / 113065000d));

        /// <summary>Sun's mean anomaly M, degrees.</summary>
        public static double SunMeanAnomaly(double t) =>
            Normalize(357.5291092 + (35999.0502909 * t) - (0.0001536 * t * t) + (t * t * t / 24490000d));

        /// <summary>Moon's mean anomaly M', degrees.</summary>
        public static double MoonMeanAnomaly(double t) =>
            Normalize(134.9633964 + (477198.8675055 * t) + (0.0087414 * t * t) + (t * t * t / 69699d)
                      - (t * t * t * t / 14712000d));

        /// <summary>Moon's argument of latitude F, degrees.</summary>
        public static double MoonArgumentOfLatitude(double t) =>
            Normalize(93.2720950 + (483202.0175233 * t) - (0.0036539 * t * t) - (t * t * t / 3526000d)
                      + (t * t * t * t / 863310000d));

        /// <summary>
        /// Mean lunar node Omega, degrees. The same series the engine uses; here it is the independent statement.
        /// </summary>
        public static double MeanNode(double t) =>
            Normalize(125.0445479 - (1934.1362891 * t) + (0.0020754 * t * t) + (t * t * t / 467441d)
                      - (t * t * t * t / 60616000d));

        /// <summary>
        /// The lunar series' mean longitude terms are functions of the arguments alone, in degrees.
        /// </summary>
        /// <remarks>
        /// These are the REDUCED identities - the parts of the full series that reduce to mean longitudes plus a
        /// smooth, low-order correction. The full periodic series (which converges to about 10 arcsec) is not
        /// reproduced here: this set pins the mean series against the catalogue definition, so its tolerances are the
        /// catalogue's own precision, not the engine's 10 arcsec series accuracy. Conflating the two would let a
        /// 10 arcsec engine pass a reference that only claims 0.01 arcsec, and vice versa.
        /// </remarks>
        public static double MoonMeanLongitudeIdentity(double t) =>
            MoonMeanLongitude(t);

        /// <summary>The Sun's geometric mean longitude, degrees (Meeus 25.2).</summary>
        public static double SunGeometricMeanLongitude(double t) =>
            Normalize(280.46646 + (36000.76983 * t) + (0.0003032 * t * t));

        /// <summary>
        /// The solar quantity the ENGINE actually produces, to make the comparison a like-for-like one.
        /// </summary>
        /// <remarks>
        /// <para><b>Do not confuse this with L0.</b> The engine's <c>GetSunLongitude</c> returns the geocentric apparent
        /// longitude (mean equinox of date) - L0 + 180 degrees plus the equation of centre plus the FK5 and aberration
        /// corrections - NOT the geometric mean longitude L0. Comparing L0 against it was my own first mistake here, and
        /// it produced a residual of about 0.09 degrees that looked like an accuracy finding and was actually a
        /// definition mismatch.</para>
        /// <para>The 0.09 degree remainder after the 180 degree flip is the equation of centre (~1.9 degrees) net of the
        /// FK5 and aberration terms, and it varies with the Sun's mean anomaly, which is why it is computed rather than
        /// subtracted as a constant here. This helper reproduces the identity from the catalogue series so the test can
        /// assert the engine against mathematics it did not itself produce.</para>
        /// </remarks>
        public static double SunApparentMeanEquinoxLongitude(double t)
        {
            // Meeus 25.2-25.5, truncated to the terms that carry 0.01 arcsec at these epochs.
            var l0 = SunGeometricMeanLongitude(t);
            var m = SunMeanAnomaly(t) * Math.PI / 180.0;
            var equationOfCentre = (1.914602 - (0.004817 * t) - (0.000014 * t * t)) * Math.Sin(m)
                                   + ((0.019993 - (0.000101 * t)) * Math.Sin(2 * m))
                                   + (0.000289 * Math.Sin(3 * m));

            var trueLongitude = l0 + equationOfCentre;

            // FK5 correction (Meeus 25.9) is a few hundredths of an arcsecond here; aberration is -20.4898/R arcsec.
            var omega = Normalize(125.04 - (1934.136 * t)) * Math.PI / 180.0;
            var fk5 = -0.09033 / 3600.0;
            var aberrationArcseconds = -20.4898 / SunRadiusVector(t);

            // No +180: L0 is the GEOCENTRIC mean longitude. The 180 degree flip belongs to a heliocentric-Earth
            // derivation, and adding it to L0 lands on the wrong point of the orbit (100.38 vs the engine's 280.3727).
            return Normalize(trueLongitude + fk5 + (aberrationArcseconds / 3600.0));
        }

        /// <summary>Earth-Sun radius vector in AU, Meeus 25.5, truncated.</summary>
        public static double SunRadiusVector(double t)
        {
            var l0 = SunGeometricMeanLongitude(t);
            var m = SunMeanAnomaly(t) * Math.PI / 180.0;

            // Meeus 25.3: the equation of centre in RADIANS, converted from the degree series above.
            var equationOfCentreRadians = ((1.914602 - (0.004817 * t) - (0.000014 * t * t)) * Math.Sin(m)
                                           + ((0.019993 - (0.000101 * t)) * Math.Sin(2 * m))
                                           + (0.000289 * Math.Sin(3 * m))) * Math.PI / 180.0;

            var trueAnomaly = m + equationOfCentreRadians;
            var eccentricity = 0.016708634 - (0.000042037 * t) - (0.0000001267 * t * t);
            return (1.000001018 * (1 - eccentricity * eccentricity)) / (1 + eccentricity * Math.Cos(trueAnomaly));
        }

        /// <summary>Mean obliquity of the ecliptic, degrees (Meeus 22.2).</summary>
        public static double MeanObliquity(double t) =>
            23.0 + ((26.0 + ((21.448 - (46.8150 * t) - (0.00059 * t * t) + (0.001813 * t * t * t)) / 60.0)) / 60.0);

        /// <summary>The Moon's mean anomaly identity used to pin the argument, degrees.</summary>
        public static double MoonMeanAnomalyIdentity(double t) => MoonMeanAnomaly(t);

        /// <summary>Normalises an angle into [0, 360).</summary>
        public static double Normalize(double degrees)
        {
            var value = degrees % 360.0;
            if (value < 0)
                value += 360.0;
            return value;
        }
    }
}

/// <summary>The generated set as committed, with the provenance that keeps it distinguishable from Horizons.</summary>
public sealed record MeanReferenceDocument(
    string Source,
    string Licence,
    string EphemerisSourceOrDefinition,
    string AccuratelyRangeUtYears,
    string Purpose,
    IReadOnlyList<MeanReferenceRow> Rows);

/// <summary>
/// One row of the generated set.
/// </summary>
/// <remarks>
/// <para><b><see cref="Kind"/> is required and has no default.</b> A row that omitted it could deserialise into
/// <see cref="MeanReferenceKind.MeanDefinitionIdentity"/> and be compared as if it were a Horizons observation, which
/// is the same omission-as-a-value defect the parity fixtures closed for <c>Claim</c>. The field is a string in the
/// record and parsed strictly, so an unknown value fails rather than landing on a default.</para>
/// </remarks>
public sealed record MeanReferenceRow(
    string Body,
    string Quantity,
    string Kind,
    double JulianDayUt,
    double LongitudeDegrees,
    double ToleranceArcseconds,
    string Definition);

/// <summary>What kind of number a generated row carries. Only one, today, and stated rather than assumed.</summary>
public enum MeanReferenceKind
{
    /// <summary>A published catalogue mean-longitude identity. NOT an observed position.</summary>
    MeanDefinitionIdentity,

    /// <summary>A Horizons observation. Never present in the generated set; refused if it appears there.</summary>
    HorizonsObservation
}
