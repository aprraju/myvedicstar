using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using NodaTime;

namespace MyVedicStar.Kundali.Calculation;

/// <summary>
/// Transit windows and Sade Sati intervals computed for an explicit reference instant.
/// </summary>
/// <remarks>
/// <para><b>Nothing here reads a clock.</b> Every method takes the instant it works from, so a caller can reproduce a
/// past answer exactly - which is the difference between a test and a demonstration.</para>
/// <para><b>Signs, not degrees, define ingress.</b> A window opens and closes when the graha crosses a 30-degree
/// sidereal boundary. That is a well-defined geometric event, unlike an aspect "orb", and it is the same boundary the
/// chart section uses.</para>
/// <para><b>Why Saturn and Jupiter only.</b> Sade Sati is a Saturn transit by definition, and the classical Jupiter
/// transits are the other slow-body event a reader plans around. Windows for fast bodies would be noise, so they are
/// not offered rather than offered and then filtered by the caller.</para>
/// </remarks>
public sealed class TransitCalculator(IGrahaPositionsProvider grahas, IAyanamsa ayanamsa)
{
    /// <summary>Half-width of the instant search, in days. One day is far below the shortest window this class
    /// reports (Saturn spends months in a sign), and keeps the search to a handful of evaluations.</summary>
    private const double SearchStepDays = 1.0;

    /// <summary>Bisection stop: one second of time. Beyond this the boundary is finer than any published time.</summary>
    private const double BoundaryToleranceDays = 1.0 / 86400.0;

    /// <summary>
    /// KUN-13: the sign windows for one graha around a reference instant.
    /// </summary>
    /// <param name="graha">Saturn or Jupiter. Any other graha is refused rather than computed and filtered.</param>
    /// <param name="referenceInstantUtc">The instant the caller is asking about.</param>
    /// <param name="windowDays">How far either side to search. Bounded by the caller so the cost is explicit.</param>
    public IReadOnlyList<GrahaSignWindow> WindowsAround(string graha, Instant referenceInstantUtc, int windowDays)
    {
        if (graha is not ("Saturn" or "Jupiter"))
            throw new ArgumentOutOfRangeException(nameof(graha), graha,
                "Transit windows are computed for Saturn and Jupiter only.");
        if (windowDays is < 1 or > 366 * 4)
            throw new ArgumentOutOfRangeException(nameof(windowDays), windowDays,
                "A transit window search must cover between 1 day and 4 years.");

        return Windows(graha, referenceInstantUtc, Duration.FromDays(windowDays));
    }

    /// <summary>
    /// KUN-13: the Sade Sati interval that contains <paramref name="referenceInstantUtc"/>, or the next one if that
    /// instant falls between phases.
    /// </summary>
    /// <param name="moonSiderealLongitude">The natal Moon longitude. The natal sign is derived from it here, not by the
    /// caller, so two call sites cannot disagree about which sign the phases are measured from.</param>
    /// <param name="referenceInstantUtc">The instant the caller is asking about. Before the first Saturn ingress into
    /// the 12th-from-Moon sign, this is how far back to look for the interval in progress.</param>
    public SadeSatiInterval? SadeSati(double moonSiderealLongitude, Instant referenceInstantUtc)
    {
        var moonSign = SignIndex(moonSiderealLongitude);
        var risingSign = (moonSign + 11) % 12;

        // Saturn's sidereal period is about 29.46 years and the span covers all three signs, so the interval that
        // contains the reference starts at most one Saturn revolution before it. Search that far back, plus a margin
        // for an interval that opens just after the reference.
        var searchFrom = referenceInstantUtc - Duration.FromDays(366 * 30);
        var searchTo = referenceInstantUtc + Duration.FromDays(366 * 4);

        var windows = Windows("Saturn", searchFrom, searchTo - searchFrom);
        var phases = windows
            .Where(window => window.Sign == NameOf(risingSign) || window.Sign == NameOf(moonSign)
                             || window.Sign == NameOf((moonSign + 1) % 12))
            .Select(window => new SadeSatiPhaseWindow(PhaseOf(window.Sign, moonSign), window.Sign, NameOf(moonSign),
                window.StartUtc, window.EndUtc))
            .ToArray();

        if (phases.Length == 0)
            return null;

        // An interval is the run of consecutive phases that includes the reference instant, or the next run when the
        // reference falls in the gap between two intervals. Saturn leaves the 2nd sign and returns to it only after a
        // full revolution, so consecutive phases in the sorted list belong to the same interval unless more than a
        // year separates them.
        var groups = GroupIntoIntervals(phases);
        var reference = referenceInstantUtc.ToDateTimeOffset();
        var interval = groups.FirstOrDefault(group => group[0].StartUtc <= reference
                                                     && group[^1].EndUtc > reference)
                       ?? groups.FirstOrDefault(group => group[0].StartUtc > reference)
                       ?? [];
        if (interval.Length == 0)
            return null;

        return new SadeSatiInterval(NameOf(moonSign), interval[0].StartUtc, interval[^1].EndUtc,
            IsPartial: interval[0].StartUtc == ToDto(searchFrom) || interval[^1].EndUtc == ToDto(searchTo),
            interval);
    }

    /// <summary>
    /// The sign index the slow graha occupies at an instant. Exposed because the section response reports the transit
    /// sign directly and must use this same conversion.
    /// </summary>
    public int TransitSign(string graha, Instant instant) => SignIndex(SiderealLongitude(graha, instant));

    private static SadeSatiPhaseWindow[][] GroupIntoIntervals(IReadOnlyList<SadeSatiPhaseWindow> phases)
    {
        var ordered = phases.OrderBy(phase => phase.StartUtc).ToArray();
        var groups = new List<List<SadeSatiPhaseWindow>>();
        foreach (var phase in ordered)
        {
            // A gap of more than a year between consecutive phases means Saturn has left the Sade Sati signs and
            // will not return for another revolution: that is a new interval, not a continuation.
            if (groups.Count == 0 || phase.StartUtc - groups[^1][^1].EndUtc > TimeSpan.FromDays(400))
                groups.Add([phase]);
            else
                groups[^1].Add(phase);
        }

        return groups.Select(group => group.ToArray()).ToArray();
    }

    private IReadOnlyList<GrahaSignWindow> Windows(string graha, Instant from, Duration span)
    {
        var windows = new List<GrahaSignWindow>();
        var start = from;
        var currentSign = TransitSign(graha, start);
        var windowStart = start;

        // Stop before the span end: a step that would probe beyond it must not emit a boundary that the closing
        // window then re-emits as a zero-length window at the same instant.
        for (var elapsed = SearchStepDays; elapsed <= span.TotalDays; elapsed += SearchStepDays)
        {
            var probe = start + Duration.FromDays(elapsed);
            if (probe > start + span || start + span - probe < Duration.FromDays(SearchStepDays))
                break;

            var sign = TransitSign(graha, probe);
            if (sign == currentSign)
                continue;

            var boundary = FindBoundary(graha, probe - Duration.FromDays(SearchStepDays), probe, currentSign);
            windows.Add(new GrahaSignWindow(graha, NameOf(currentSign), ToDto(windowStart), ToDto(boundary)));
            currentSign = sign;
            windowStart = boundary;
        }

        windows.Add(new GrahaSignWindow(graha, NameOf(currentSign), ToDto(windowStart),
            ToDto(start + span)));
        return windows;
    }

    /// <summary>
    /// Bisection between two instants known to hold different signs. Returns the first instant of the new sign.
    /// </summary>
    /// <remarks>
    /// Deliberately the last instant of the old sign rather than the first of the new one. Windows are half-open
    /// [start, end), so an end value equal to the next start would make the boundary instant belong to both windows;
    /// returning the outgoing instant makes the tiling exact by construction instead of by a convention every caller
    /// has to remember.
    /// </remarks>
    private Instant FindBoundary(string graha, Instant before, Instant after, int signBefore)
    {
        var low = before;
        var high = after;
        while ((high - low).TotalDays > BoundaryToleranceDays)
        {
            var mid = low + Duration.FromTicks((high - low).BclCompatibleTicks / 2);
            if (TransitSign(graha, mid) == signBefore)
                low = mid;
            else
                high = mid;
        }

        return high;
    }

    private double SiderealLongitude(string graha, Instant instant)
    {
        var julianDayUt = JulianDay.FromInstant(instant);
        var wanted = Enum.Parse<Graha>(graha, ignoreCase: false);
        var position = grahas.GetSiderealPositions(julianDayUt, ayanamsa.GetDegrees(julianDayUt))
            .Single(entry => entry.Graha == wanted);

        // A withheld position is not a zero: reporting a transit sign from an unvalidated longitude would present an
        // unproven number as a dated event. The caller that needs windows must have a published position.
        return position.SiderealLongitude
               ?? throw new InvalidOperationException(
                   $"{graha} has no published sidereal longitude ({position.Status}: {position.Reason}). "
                   + "A transit window cannot be computed from a withheld position.");
    }

    private static int SignIndex(double siderealLongitude) =>
        (int)Math.Floor(((siderealLongitude % 360d) + 360d) % 360d / 30d) % 12;

    private static string PhaseOf(string saturnSign, int moonSign) =>
        saturnSign == NameOf((moonSign + 11) % 12) ? SadeSatiPhase.Rising
        : saturnSign == NameOf(moonSign) ? SadeSatiPhase.Peak
        : SadeSatiPhase.Setting;

    private static string NameOf(int sign) => MyVedicStar.Kundali.Shared.KundaliSigns.NameOf(sign);

    private static DateTimeOffset ToDto(Instant instant) => instant.ToDateTimeOffset();
}