namespace MyVedicStar.Kundali.Calculation;

/// <summary>One transit window: when Saturn or Jupiter occupies a set of signs, sidereally.</summary>
/// <param name="Graha">The transiting graha. Only slow grahas get windows; a fast body's ingress is an ephemeris
/// curiosity, not an event worth an alert.</param>
/// <param name="Sign">The sidereal sign occupied through the window.</param>
/// <param name="StartUtc">First instant the graha is in the sign, inclusive.</param>
/// <param name="EndUtc">First instant it is no longer in the sign, exclusive.</param>
public sealed record GrahaSignWindow(string Graha, string Sign, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    /// <summary>True at <paramref name="at"/>. Half-open, so a boundary instant belongs to exactly one window.</summary>
    public bool Contains(DateTimeOffset at) => at >= StartUtc && at < EndUtc;
}

/// <summary>
/// One Sade Sati phase. Saturn is Sade Sati while it transits the sign before the natal Moon's, the natal Moon's, or
/// the sign after it.
/// </summary>
/// <param name="Phase">One of <see cref="SadeSatiPhase"/> names.</param>
/// <param name="Sign">The sign Saturn occupies through this phase.</param>
/// <param name="MoonSign">The natal Moon sign the phase was measured from, echoed so the interval is checkable.</param>
public sealed record SadeSatiPhaseWindow(string Phase, string Sign, string MoonSign,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public bool Contains(DateTimeOffset at) => at >= StartUtc && at < EndUtc;
}

/// <summary>The three Sade Sati phases, named rather than numbered so a consumer cannot mis-order them.</summary>
public static class SadeSatiPhase
{
    /// <summary>Saturn in the 12th sign from the natal Moon: the first phase.</summary>
    public const string Rising = "rising";

    /// <summary>Saturn over the natal Moon's sign: the peak.</summary>
    public const string Peak = "peak";

    /// <summary>Saturn in the 2nd sign from the natal Moon: the closing phase.</summary>
    public const string Setting = "setting";

    public static readonly IReadOnlyList<string> All = [Rising, Peak, Setting];
}

/// <summary>
/// A Sade Sati interval as a whole: from the first ingress into the 12th to the last egress from the 2nd.
/// </summary>
/// <param name="IsPartial">True when the interval is clipped by the supported date span, so its start or end is the
/// span boundary rather than a real transit boundary. Stated rather than hidden, because a consumer that treats a
/// clipped interval as complete will report a phase that has not happened.</param>
public sealed record SadeSatiInterval(string MoonSign, DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool IsPartial,
    IReadOnlyList<SadeSatiPhaseWindow> Phases)
{
    public bool Contains(DateTimeOffset at) => at >= StartUtc && at < EndUtc;
}
