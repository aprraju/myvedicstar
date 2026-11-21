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
    /// Converts to Terrestrial Time through <b>the seam the caller was given</b> (api#159). <b>Passing this result to
    /// anything that expects <see cref="UtInstant"/> does not compile</b>, which is the entire point of the type pair.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The seam has no conversion. That is a state the seam can hold and a test can construct; it is not an accident of
    /// test ordering.
    /// </exception>
    public TtInstant ToTerrestrialTime(TtConversionSeam seam)
    {
        ArgumentNullException.ThrowIfNull(seam);
        return seam.ToTerrestrialTime(this);
    }

    public override string ToString() => $"JD {JulianDay:R} UT";
}

public readonly record struct TtInstant(double JulianDay)
{
    /// <summary>
    /// Converts back to Universal Time through the same seam (api#159). Provided because a seam pairs a conversion with
    /// its inverse at construction; on the latched design this direction did not exist.
    /// </summary>
    public UtInstant ToUniversalTime(TtConversionSeam seam)
    {
        ArgumentNullException.ThrowIfNull(seam);
        return seam.ToUniversalTime(this);
    }

    public override string ToString() => $"JDE {JulianDay:R} TT";
}

/// <summary>
/// The one place UT becomes TT for reference work (api#140), as a <b>per-instance seam</b> (api#159).
/// </summary>
/// <remarks>
/// <para>
/// The previous design was a process-wide install-once latch. That made the two worlds a test must distinguish - "a
/// conversion is loaded" and "no conversion is loaded" - decided by whichever test ran first, and an unlucky order left
/// every later test in the process running with <b>no</b> conversion. The latch itself made it worse: <c>Install</c>
/// became a no-op once anything had installed, so the restore after a test could not take effect either.
/// </para>
/// <para>
/// A seam holds its conversion <b>as a field</b>. Absence is therefore a state that can be <b>constructed</b> and
/// asserted: <see cref="Uninstalled"/> has no conversion and throws when asked to convert. Nothing here is a switch:
/// holding an uninstalled seam does not affect <see cref="Default"/> or any other seam, so observing absence cannot
/// break the world for the next test - which is what a production-callable reset did.
/// </para>
/// <para>
/// The conversion is still <b>called, not reimplemented</b>: the astronomy provider installs its own <c>DeltaT</c> as
/// <see cref="Default"/>, so the generator and the engine cannot disagree.
/// </para>
/// </remarks>
public sealed class TtConversionSeam : IDisposable
{
    private readonly TtConversionSeam? _previous;
    private readonly Func<double, double>? _toTt;
    private readonly Func<double, double>? _toUt;
    private bool _disposed;

    private TtConversionSeam(TtConversionSeam? previous, Func<double, double>? toTt, Func<double, double>? toUt)
    {
        _previous = previous;
        _toTt = toTt;
        _toUt = toUt;
    }

    // The conversion the process actually uses, installed by the astronomy provider at composition. Field-backed rather
    // than a property so it can be published atomically: a request that reads it sees either the old seam or the new one,
    // never a half-installed one, and the mutable-static census test (api#75) recognises exactly this one field.
    private static TtConversionSeam _default = new(null, null, null);

    /// <summary>The conversion the process actually uses; installed by the astronomy provider at composition.</summary>
    public static TtConversionSeam Default
    {
        get => Volatile.Read(ref _default);
        private set => Volatile.Write(ref _default, value);
    }

    /// <summary>
    /// A seam with <b>no</b> conversion. This is the state the latch could only reach by accident, and the reason the
    /// old design could not assert the missing-conversion branch without a production-visible reset.
    /// </summary>
    public static TtConversionSeam Uninstalled() => new(Default, null, null);

    /// <summary>
    /// A seam holding this exact conversion and its inverse. Both directions are supplied together, so a round trip
    /// cannot silently pair a forward from one conversion with the inverse of another.
    /// </summary>
    public static TtConversionSeam Installed(Func<double, double> toTt, Func<double, double> toUt)
    {
        ArgumentNullException.ThrowIfNull(toTt);
        ArgumentNullException.ThrowIfNull(toUt);
        return new(Default, toTt, toUt);
    }

    /// <summary>
    /// Installs <paramref name="toTt"/> and <paramref name="toUt"/> as <see cref="Default"/>. Called by the provider at
    /// composition. Not install-once, and deliberately so: composing twice is normal (two hosts in one process), and a
    /// refusal left later work with whatever won first, including nothing at all.
    /// </summary>
    public static void InstallDefault(Func<double, double> toTt, Func<double, double> toUt)
    {
        ArgumentNullException.ThrowIfNull(toTt);
        ArgumentNullException.ThrowIfNull(toUt);
        Default = new(null, toTt, toUt);
    }

    /// <summary>
    /// Clears <see cref="Default"/> back to "no conversion", for tests only.
    /// </summary>
    /// <remarks>
    /// api#159. This is the ONE thing a test may do to process-wide state, and it is deliberately a separate method
    /// rather than a switch on an instance: it touches only what composition sets, so it cannot make an ABSENT seam
    /// appear present, and it cannot make a test's own conversion disappear mid-test. The defect being removed was a
    /// reset that both cleared global state AND was the only way to observe absence; here absence is constructed.
    /// </remarks>
    public static void ResetDefaultForTests() => Default = new(null, null, null);

    /// <summary>Whether this seam holds a conversion. An observation, not a switch.</summary>
    public bool HasConversion => _toTt is not null && _toUt is not null;

    /// <summary>Converts through this seam. Throws when this seam holds no conversion - that is the assertable state.</summary>
    public TtInstant ToTerrestrialTime(UtInstant ut) => new(ToTerrestrialTimeDays(ut));

    /// <summary>Converts back through this seam's own inverse. Throws when this seam holds no conversion.</summary>
    public UtInstant ToUniversalTime(TtInstant tt) =>
        new(_toUt?.Invoke(tt.JulianDay)
            ?? throw new InvalidOperationException(
                "This TT->UT seam holds no conversion. The latch design could not express this direction at all."));

    private double ToTerrestrialTimeDays(UtInstant ut) =>
        _toTt?.Invoke(ut.JulianDay)
        ?? throw new InvalidOperationException(
            "This UT->TT seam holds no conversion. Defaulting here would make a delta-T difference invisible, which is "
            + "the defect api#140 exists to remove. Install one at composition, or construct this seam with one.");

    /// <summary>Restores the seam that was default when this one was constructed.</summary>
    /// <remarks>
    /// Scoped restore, not a reset: it puts back the value this seam captured, so tests cannot leak into each other by
    /// order. Nothing outside this assembly can call it - it is a member of a type the production code path never holds.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_previous is not null)
            Default = _previous;
    }
}
