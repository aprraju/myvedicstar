using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using MyVedicStar.Kundali.Shared;
using NodaTime;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// KUN-20: sun/moon sidereal longitudes with their signs, the nine-graha positions, and the Vimshottari and Yogini
/// dasha sequences.
/// </summary>
/// <remarks>
/// <para>Pure calculation. It resolves no profile, no settings, no database and no clock - everything arrives as
/// plain values, so identical inputs always produce identical output.</para>
/// <para>Depends on <see cref="IEphemeris"/>, <see cref="IGrahaPositionsProvider"/> and <see cref="IAyanamsa"/>, not on
/// their implementations: a higher-precision provider is selected through configuration and this code is unchanged.</para>
/// </remarks>
public sealed class KundaliEngine(IEphemeris ephemeris, IAyanamsa ayanamsa) : IKundaliEngine
{
    /// <summary>
    /// Optional graha surface. A caller that only wants Sun/Moon does not have to supply one; when it is absent the graha
    /// section is withheld with <c>not-implemented</c> rather than omitted, so the absence is still explained.
    /// </summary>
    public IGrahaPositionsProvider? GrahaProvider { get; init; }

    /// <summary>Independent reference values the envelope may assert. Supplied by composition, never hard-coded here.</summary>
    public IReadOnlyList<PlanetaryReference> PlanetaryReferences { get; init; } = [];

    /// <summary>Full circle in degrees.</summary>
    private const double CircleDegrees = 360.0;

    /// <summary>Degrees per zodiac sign. Whole-sign division, per the approved conventions.</summary>
    private const double DegreesPerSign = 30.0;

    public KundaliCalculationResult Calculate(KundaliCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gaps = new List<KundaliCalculationGap>();

        // A time-dependent result cannot be produced from an approximate or unknown birth time. Suppress it and say
        // so - computing from a midpoint or an assumed instant would be a fabricated Ascendant, which is worse than
        // an absent one.
        var hasExactTime = request.BirthTimeMode == BirthTimeMode.Known && request.BirthTime.HasValue;

        var conventions = BuildConventions(request);

        if (!hasExactTime)
        {
            AddGap(gaps, nameof(KundaliCalculationResult.SiderealSunLongitude),
                KundaliCalculationReasons.RequiresExactBirthTime);
            AddGap(gaps, nameof(KundaliCalculationResult.SiderealMoonLongitude),
                KundaliCalculationReasons.RequiresExactBirthTime);
            AddGap(gaps, nameof(KundaliCalculationResult.SunSign),
                KundaliCalculationReasons.RequiresExactBirthTime);
            AddGap(gaps, nameof(KundaliCalculationResult.MoonSign),
                KundaliCalculationReasons.RequiresExactBirthTime);

            // Dashas are anchored on the birth instant and selected by the moon's position at it, so an inexact
            // birth time makes the whole sequence indeterminate - not merely the fine detail of it.
            AddGap(gaps, nameof(KundaliCalculationResult.VimshottariMahadashas),
                KundaliCalculationReasons.RequiresExactBirthTime);
            AddGap(gaps, nameof(KundaliCalculationResult.YoginiPeriods),
                KundaliCalculationReasons.RequiresExactBirthTime);
            AddGap(gaps, nameof(KundaliCalculationResult.CharaSignPeriods),
                KundaliCalculationReasons.RequiresExactBirthTime);
            AddGap(gaps, nameof(KundaliCalculationResult.GrahaPositions),
                KundaliCalculationReasons.RequiresExactBirthTime);

            return new KundaliCalculationResult(conventions, null, null, null, null, null, null, null, gaps);
        }

        // One resolution of the local wall clock to an instant, shared by the astronomy and the dasha anchor: two
        // independent conversions could disagree across a DST boundary.
        var birthInstant = ResolveBirthInstant(request);
        var julianDayUt = JulianDay.FromInstant(birthInstant);
        var ayanamsaDegrees = ayanamsa.GetDegrees(julianDayUt);

        var sunTropical = ephemeris.GetSunLongitude(julianDayUt);
        var moonTropical = ephemeris.GetMoonLongitude(julianDayUt);

        // Tropical to sidereal. A single named conversion so the direction of the subtraction is not open to
        // interpretation at each call site.
        var sunSidereal = Normalize(sunTropical - ayanamsaDegrees);
        var moonSidereal = Normalize(moonTropical - ayanamsaDegrees);

        // The ascendant is computable but has no independent reference yet, so it is withheld as
        // unvalidated-reference rather than published unproven. Distinct from "requires exact time": this one is a
        // validation task, not a computation gap.
        AddGap(gaps, "Ascendant", KundaliCalculationReasons.UnvalidatedReference);

        var periods = VimshottariDasha.BuildMahadashas(moonSidereal, birthInstant);
        var mahadashas = periods
            .Select(period => new KundaliVimshottariPeriod(period.Lord, period.StartUtc.ToDateTimeOffset(),
                period.EndUtc.ToDateTimeOffset(), period.IsRemainder))
            .ToArray();

        var yogini = YoginiDasha.BuildPeriods(moonSidereal, birthInstant)
            .Select(period => new KundaliYoginiPeriod(period.Name, period.Ruler,
                period.StartUtc.ToDateTimeOffset(), period.EndUtc.ToDateTimeOffset(), period.IsRemainder))
            .ToArray();

        // The Chara progression needs the ascendant, which is withheld as unvalidated-reference, so it is not built
        // here. Reported rather than silently omitted, and reported with the same validation reason as the ascendant
        // it depends on - because that is the actual cause.
        AddGap(gaps, nameof(KundaliCalculationResult.CharaSignPeriods),
            KundaliCalculationReasons.UnvalidatedReference);

        var grahas = BuildGrahaPositions(julianDayUt, ayanamsaDegrees, gaps);

        return new KundaliCalculationResult(conventions, sunSidereal, moonSidereal,
            SignOf(sunSidereal), SignOf(moonSidereal), mahadashas, yogini, null, gaps, grahas);
    }

    /// <summary>
    /// The nine-graha section. The gate that decides which longitudes may be published lives in
    /// <see cref="GrahaPositionsResolver"/>, not here: this method only adapts the astronomy vocabulary to the Kundali one.
    /// </summary>
    private KundaliGrahaPositions? BuildGrahaPositions(double julianDayUt, double ayanamsaDegrees,
        List<KundaliCalculationGap> gaps)
    {
        if (GrahaProvider is null)
        {
            AddGap(gaps, nameof(KundaliCalculationResult.GrahaPositions), KundaliCalculationReasons.NotImplemented);
            return null;
        }

        var conventions = new GrahaConventions("Sidereal", "Lahiri", ayanamsaDegrees.ToString("0.######",
                System.Globalization.CultureInfo.InvariantCulture), KundaliNodeConvention,
            "analytical", "Grahas are geocentric; house placement needs an ascendant, which is withheld.");
        var resolved = new GrahaPositionsResolver(GrahaProvider)
            .Resolve(julianDayUt, ayanamsaDegrees, conventions, PlanetaryReferences);

        AddGrahaGaps(gaps, resolved);

        var positions = resolved.Grahas.Select(graha => new KundaliGrahaPosition(graha.Graha.ToString(),
            graha.SiderealLongitude,
            graha.Sign is { } sign ? SignName(sign) : null,
            graha.DegreeWithinSign is { } degrees ? (decimal)Math.Round(degrees, 4) : null,
            graha.Retrograde,
            graha.MeanDailyMotionDegrees,
            graha.Reason)).ToArray();

        return new KundaliGrahaPositions(positions, KundaliNodeConvention, Summary(resolved.Accuracy));
    }

    private static void AddGrahaGaps(List<KundaliCalculationGap> gaps, GrahaPositionsResult resolved)
    {
        // A graha absent from the envelope's per-body set is reported once, at its own path, so a caller can bind the
        // reason to the field it chose not to render instead of parsing one aggregate code.
        foreach (var graha in resolved.Grahas.Where(graha => !graha.SiderealLongitude.HasValue))
        {
            AddGap(gaps, $"{nameof(KundaliCalculationResult.GrahaPositions)}.{graha.Graha}",
                graha.Status == GrahaReasonCodes.NotImplemented
                    ? KundaliCalculationReasons.NotImplemented
                    : KundaliCalculationReasons.UnvalidatedReference);
        }
    }

    /// <summary>
    /// One sentence a caller can publish. Built from the envelope rather than restated, so the summary cannot claim more
    /// than the envelope does.
    /// </summary>
    private static string Summary(GrahaAccuracyEnvelope accuracy)
    {
        var asserted = accuracy.PerBodyArcseconds
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} {pair.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} arcsec")
            .ToArray();
        return asserted.Length == 0
            ? "No graha tolerance is asserted: no independent reference values are checked in. Longitudes for Mars, "
              + "Mercury, Jupiter, Venus, Saturn, Rahu and Ketu are withheld as unvalidated-reference."
            : $"Asserted tolerances: {string.Join(", ", asserted)}. All other grahas are withheld as unvalidated-reference.";
    }

    /// <summary>Sign name from the wired sign table, so the graha section and a chart cannot disagree about a label.</summary>
    private static string SignName(int sign) => MyVedicStar.Kundali.Shared.KundaliSigns.NameOf(sign);

    private const string KundaliNodeConvention = "MeanNode";

    /// <summary>Zero-based sign index. 0 = Aries. Whole-sign, so the boundary belongs to the later sign.</summary>
    internal static int SignOf(double siderealLongitude) =>
        (int)Math.Floor(Normalize(siderealLongitude) / DegreesPerSign) % 12;

    /// <summary>Wraps a longitude into [0, 360). Negative inputs wrap forward rather than clamping.</summary>
    internal static double Normalize(double degrees)
    {
        var wrapped = degrees % CircleDegrees;
        return wrapped < 0 ? wrapped + CircleDegrees : wrapped;
    }

    private static Instant ResolveBirthInstant(KundaliCalculationRequest request)
    {
        // The birth date and time are local wall-clock values in the profile's own zone, so the offset is resolved
        // through the zone rather than assumed to be UTC.
        var zone = DateTimeZoneProviders.Tzdb[request.TimezoneId];
        var localDate = new LocalDate(request.BirthDate.Year, request.BirthDate.Month, request.BirthDate.Day);
        var localTime = new LocalTime(request.BirthTime!.Value.Hour, request.BirthTime.Value.Minute,
            request.BirthTime.Value.Second);
        var zoned = zone.AtLeniently(localDate.At(localTime));
        return zoned.ToInstant();
    }

    private static KundaliCalculationConventions BuildConventions(KundaliCalculationRequest request) =>
        new(request.Ayanamsa, "Sidereal", "analytical",
            new KundaliAccuracyEnvelope("analytical", "1 arcsecond (Sun), 3 arcseconds (Moon)",
                "Meeus Astronomical Algorithms worked examples; see parity-fixtures.json"));

    private static void AddGap(List<KundaliCalculationGap> gaps, string path, string reason) =>
        gaps.Add(new KundaliCalculationGap(path, reason));
}

/// <summary>The engine contract. Pure: plain values in, plain values out, no ambient state.</summary>
public interface IKundaliEngine
{
    KundaliCalculationResult Calculate(KundaliCalculationRequest request);
}

