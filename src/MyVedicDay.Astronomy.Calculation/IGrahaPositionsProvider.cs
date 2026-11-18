using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy;

/// <summary>
/// Sidereal positions of the nine grahas.
/// </summary>
/// <remarks>
/// <para>A sibling port to <see cref="IEphemeris"/>, not a widening of it. The Sun/Moon contract is consumed by Panchang,
/// Muhurta and the event calculators, whose stubs implement four methods; adding seven more would break every one of them
/// for no benefit. A separate port also lets a provider satisfy the graha surface without claiming it can produce
/// equatorial coordinates for planets, which is a different and unproven capability.</para>
/// <para>Longitudes are sidereal (nirayana) under the requested ayanamsa, in [0, 360). Absence is always stated: a body a
/// provider cannot produce is returned with a reason code rather than omitted from the list.</para>
/// </remarks>
public interface IGrahaPositionsProvider
{
    /// <summary>Sidereal longitude and state of every graha, in <see cref="Graha"/> order.</summary>
    /// <param name="julianDayUt">Instant, Julian Day UT. Implementations handle Delta T internally, as IEphemeris does.</param>
    /// <param name="ayanamsaDegrees">Ayanamsa of date to subtract from the tropical longitude.</param>
    IReadOnlyList<GrahaPosition> GetSiderealPositions(double julianDayUt, double ayanamsaDegrees);

    /// <summary>
    /// Which grahas this provider can produce, and the reason for each one it cannot. Read by the capability endpoint and
    /// by the accuracy envelope, so the engine's promises come from the implementation rather than from a constant that
    /// can drift away from it.
    /// </summary>
    GrahaSupport GetSupport();
}

/// <summary>Per-graha support declaration.</summary>
/// <param name="Computable">True when the provider returns a longitude for this graha.</param>
/// <param name="Reason">Null exactly when <paramref name="Computable"/> is true.</param>
public sealed record GrahaSupportEntry(Graha Graha, bool Computable, string? Reason);

/// <summary>The provider's support map, in graha order.</summary>
public sealed record GrahaSupport(IReadOnlyList<GrahaSupportEntry> Entries)
{
    /// <summary>True when every graha is computable. Never inferred from a partial list.</summary>
    public bool CoversAllGrahas => Entries.All(entry => entry.Computable);

    public GrahaSupportEntry Of(Graha graha) => Entries.Single(entry => entry.Graha == graha);
}
