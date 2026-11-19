namespace MyVedicStar.Astronomy.Core;

/// <summary>
/// An instant expressed as a Julian Day in <b>Universal Time</b> (api#140).
/// </summary>
/// <remarks>
/// <para>
/// This type exists because <c>2451545.0</c> means two different instants depending on which function receives it.
/// It is J2000.0 in TT — already an ephemeris day — while methods that take UT apply delta-T internally. The same
/// literal passed to both is 63.86 seconds apart, which is about 35 arcseconds of lunar motion and about 2.5
/// arcseconds of solar motion. <b>The difference is invisible on the Sun and fatal on the Moon</b>, which is how a
/// reference comparison spent five rounds measuring the wrong pair.
/// </para>
/// <para>
/// A comment cannot prevent that; a type can. There is exactly one conversion, <see cref="ToTerrestrialTime"/>, and
/// it is one-directional on purpose: the engine converts UT to TT internally and nothing converts back, so a
/// rounding trip cannot accumulate.
/// </para>
/// </remarks>
/// <param name="JulianDay">Julian Day in UT.</param>
public readonly record struct UtInstant(double JulianDay)
{
    /// <summary>
    /// Converts to Terrestrial Time. <b>Passing this result to anything that expects <see cref="UtInstant"/> does not
    /// compile</b>, which is the entire point of the type pair.
    /// </summary>
    public TtInstant ToTerrestrialTime() => new(TtOffsets.ToTerrestrialTime(this));

    public override string ToString() => $"JD {JulianDay:R} UT";
}

/// <summary>
/// An instant expressed as a Julian Day in <b>Terrestrial Time</b> — an ephemeris day (api#140).
/// </summary>
/// <remarks>
/// This is what the analytical series take. It is deliberately a distinct type from <see cref="UtInstant"/> so that
/// the argument mistake that caused a 31.99 arcsecond discrepancy cannot be written: the compiler rejects it.
/// </remarks>
/// <param name="JulianDay">Julian Day in TT (JDE).</param>
public readonly record struct TtInstant(double JulianDay)
{
    public override string ToString() => $"JDE {JulianDay:R} TT";
}

/// <summary>
/// The one place UT becomes TT for reference work (api#140).
/// </summary>
/// <remarks>
/// The conversion is <b>called, not reimplemented</b>: the astronomy provider's <c>DeltaT</c> is the only
/// implementation, so the generator and the engine cannot diverge — a divergence would be impossible rather than
/// something a test has to notice. See <c>TtOffsets.Conversion</c>, which the provider installs at start-up.
/// </remarks>
public static class TtOffsets
{
    /// <summary>
    /// The installed UT→TT conversion. Set once by the provider assembly so that Core does not depend on a provider
    /// (the dependency direction is enforced by test). A missing conversion throws rather than defaulting, because a
    /// silent fallback would reintroduce exactly the invisible difference this type pair removes.
    /// </summary>
    public static Func<double, double>? Conversion { get; set; }

    internal static double ToTerrestrialTime(UtInstant ut) =>
        Conversion?.Invoke(ut.JulianDay)
        ?? throw new InvalidOperationException(
            "No UT->TT conversion is installed. The astronomy provider must set TtOffsets.Conversion; defaulting here "
            + "would make a delta-T difference invisible, which is the defect api#140 exists to remove.");
}
