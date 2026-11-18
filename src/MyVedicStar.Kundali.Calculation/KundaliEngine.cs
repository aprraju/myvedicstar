using MyVedicDay.Astronomy;
using NodaTime;
using MyVedicStar.VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// KUN-20 slices 1 and 3: sun/moon sidereal longitudes with their signs, and the Vimshottari mahadasha sequence.
/// </summary>
/// <remarks>
/// <para>Pure calculation. It resolves no profile, no settings, no database and no clock - everything arrives as
/// plain values, so identical inputs always produce identical output.</para>
/// <para>Depends on <see cref="IEphemeris"/> and <see cref="IAyanamsa"/>, not on their implementations: a future
/// higher-precision provider is selected through configuration and this code is unchanged.</para>
/// </remarks>
public sealed class KundaliEngine(IEphemeris ephemeris, IAyanamsa ayanamsa) : IKundaliEngine
{
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

            return new KundaliCalculationResult(conventions, null, null, null, null, null, gaps);
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

        var periods = VimshottariDasha.BuildMahadashas(moonSidereal, Instant.FromDateTimeOffset(birthInstant));
        var mahadashas = periods
            .Select(period => new KundaliVimshottariPeriod(period.Lord, period.StartUtc.ToDateTimeOffset(),
                period.EndUtc.ToDateTimeOffset(), period.IsRemainder))
            .ToArray();

        return new KundaliCalculationResult(conventions, sunSidereal, moonSidereal,
            SignOf(sunSidereal), SignOf(moonSidereal), mahadashas, gaps);
    }

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

