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
    /// Installs the single UT->TT conversion. Called by the provider at composition, from Core's public surface so that
    /// no assembly needs to be named in InternalsVisibleTo in order to do it.
    /// </summary>
    /// <remarks>
    /// <b>Install-once, not a settable property.</b> Only the first call takes effect, decided atomically, so two
    /// conversions can never both be live - which would mean whichever ran last wins, the two-sources defect in its
    /// most literal form. Later calls return false rather than throwing, because composing twice is a normal thing
    /// (two hosts in one process, a test host beside the real one) and is not itself two different conversions.
    /// There is no public way to clear it, so a caller cannot quietly remove the guard either.
    /// </remarks>
    public static bool Install(Func<double, double> conversion)
    {
        ArgumentNullException.ThrowIfNull(conversion);

        // Atomic, not check-then-act. Composition can run concurrently - two hosts in one process, or a test host
        // alongside the real one - and a guarded read followed by a write races between them. CompareExchange decides
        // exactly one winner, and the losers return false rather than throwing: installing the SAME conversion twice is
        // idempotent, and refusing it would fail start-up for a condition that is not actually two conversions.
        var winner = Interlocked.CompareExchange(ref _installed, 1, 0) == 0;
        if (winner)
            _conversion = conversion;

        return winner;
    }

    private static Func<double, double>? _conversion;
    private static int _installed;

    /// <summary>The installed conversion, or null. Visible to the astronomy tests so they can assert the guard.</summary>
    public static bool IsInstalled => _conversion is not null;

    internal static Func<double, double>? Installed => _conversion;

    internal static double ToTerrestrialTime(UtInstant ut) =>
        _conversion?.Invoke(ut.JulianDay)
        ?? throw new InvalidOperationException(
            "No UT->TT conversion is installed. The astronomy provider must call TtOffsets.Install; defaulting here "
            + "would make a delta-T difference invisible, which is the defect api#140 exists to remove.");

    /// <summary>Clears the installation, for tests only.</summary>
    /// <remarks>Public because the guard has to be exercised: a test that cannot reset it cannot prove the
    /// missing-conversion branch fires. No production path calls this.</remarks>
    public static void ResetForTests()
    {
        _conversion = null;
        Interlocked.Exchange(ref _installed, 0);
    }
}
