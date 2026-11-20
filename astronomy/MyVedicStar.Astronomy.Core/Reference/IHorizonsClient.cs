using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Reference;

/// <summary>
/// What the generator asks a reference service for, expressed so the request is reproducible from the artifact.
/// </summary>
/// <param name="Body">The body to fetch, in the engine's body vocabulary.</param>
/// <param name="InstantTt">The instant, in Terrestrial Time.</param>
/// <param name="Quantity">Geometric or apparent.</param>
public sealed record HorizonsQuery(string Body, TtInstant InstantTt, HorizonsQuantity Quantity);

/// <summary>
/// A reference value returned for one query, with the provenance of the response it came from.
/// </summary>
/// <param name="LongitudeDegrees">Ecliptic longitude in degrees.</param>
/// <param name="LatitudeDegrees">Ecliptic latitude in degrees.</param>
/// <param name="Provenance">Where this came from. Never null: a value with no provenance is indistinguishable from a
/// hand-written one, which is exactly what an independent reference must not be.</param>
public sealed record HorizonsValue(
    double LongitudeDegrees,
    double LatitudeDegrees,
    HorizonsProvenance Provenance);

/// <summary>
/// The reference service, behind a port so the generator's structure can be built and reviewed before any fetch runs.
/// </summary>
/// <remarks>
/// <para><b>This port exists ahead of a working implementation on purpose.</b> The parts of the generator that can be
/// reviewed without trusting a number - frame assertion, round-trip, coverage, per-row provenance - do not need the
/// network. Building them behind an unimplemented port means the structure is agreed before it is filled, and the
/// fetch becomes a one-class addition rather than a rewrite.</para>
/// </remarks>
public interface IHorizonsClient
{
    /// <summary>
    /// Fetches one value.
    /// </summary>
    /// <exception cref="HorizonsUnavailableException">
    /// The service cannot be reached, or the request parameters are not yet established. The exception is the refusing
    /// path: the generator must fail loudly rather than produce a row with no real value behind it.
    /// </exception>
    Task<HorizonsValue> FetchAsync(HorizonsQuery query, CancellationToken token = default);
}

/// <summary>
/// Raised when a reference value cannot be obtained, or when the request cannot yet be formed correctly.
/// </summary>
/// <remarks>
/// <para><b>The message distinguishes "unreachable" from "parameters unresolved".</b> They need different fixes, and a
/// single "fetch failed" would send someone to check the network when the actual problem is the
/// <c>APPARENT</c>/geometric parameter set - which is the state this generator is in.</para>
/// </remarks>
public sealed class HorizonsUnavailableException : Exception
{
    public HorizonsUnavailableException(string reason, string detail)
        : base($"Horizons reference value unavailable ({reason}): {detail}")
        => Reason = reason;

    /// <summary>A stable code: <c>unreachable</c> or <c>parameters-unresolved</c>.</summary>
    public string Reason { get; }
}

/// <summary>
/// The client that refuses every request, used until the parameter set is settled.
/// </summary>
/// <remarks>
/// <para><b>This is not a stub that returns zeros.</b> Returning a plausible number would be the worst possible
/// behaviour: rows would be generated, committed, and would look like reference data. A refusing client cannot
/// produce a file, so a run either has a real implementation behind it or produces nothing.</para>
/// <para>It exists so the generator can be wired, composed and tested end-to-end while the fetch is unresolved, and so
/// the failure mode is exercised rather than assumed.</para>
/// </remarks>
public sealed class RefusingHorizonsClient : IHorizonsClient
{
    public Task<HorizonsValue> FetchAsync(HorizonsQuery query, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        throw new HorizonsUnavailableException("parameters-unresolved",
            "The APPARENT/geometric parameter set for ecliptic output is not yet established, so no request can be "
            + "formed whose result would be comparable with the engine. See #140. No value is fabricated in the "
            + "meantime: a row with a guess behind it is worse than no row.");
    }
}

