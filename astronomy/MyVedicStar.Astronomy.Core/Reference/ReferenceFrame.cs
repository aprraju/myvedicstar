namespace MyVedicStar.Astronomy.Reference;

/// <summary>
/// The single statement of which frame and quantity the reference rows are in, asserted rather than assumed.
/// </summary>
/// <remarks>
/// <para><b>Why a constant and not a comment.</b> A reference comparison measures the difference between two
/// conventions as if it were accuracy error. A frame change on either side - ecliptic to equatorial, mean to true
/// equinox, geometric to apparent - moves the number by far more than the tolerances these rows hold, and the result
/// looks like an engine defect. The assertion below reads the same constant the generator writes rows from, so the two
/// sides cannot drift apart silently.</para>
/// <para><b>Round-trip required, one-way is not enough.</b> A frame error that happens to cancel between the forward and
/// return path passes a one-way test. <see cref="RoundTripIsTighterThanEveryTolerance"/> is the guard, and its
/// tolerance must be tighter than every row budget or it proves nothing.</para>
/// </remarks>
public static class ReferenceFrame
{
    /// <summary>The frame the reference rows are expressed in: ecliptic longitude/latitude, mean equinox of date.</summary>
    /// <remarks>
    /// Matches the engine's published convention. Written once here and read by both the generator and its tests, so a
    /// frame change on one side fails an assertion rather than silently changing what a number means.
    /// </remarks>
    public const string Ecliptic = "ecliptic-longitude-latitude-mean-equinox-of-date";

    /// <summary>The time scale of <see cref="HorizonsFixtureRow.InstantTt"/>.</summary>
    /// <remarks>
    /// Terrestrial Time, not UT. The engine converts UT to TT internally through its own delta-T, so the generator
    /// must hand it an instant already labelled TT and must not re-derive the offset. Fetching in UT and converting
    /// independently would absorb a delta-T difference into the reported accuracy - ~59 s in 1992, second-order for the
    /// Sun and not for the Moon.
    /// </remarks>
    public const string TimeScale = "TT";

    /// <summary>
    /// The round-trip tolerance in arcseconds: the bound within which a forward-then-inverse transform must return.
    /// </summary>
    /// <remarks>
    /// Deliberately far tighter than any row tolerance. If it were the same order as a row budget, a frame error of
    /// that size would cancel inside the round trip and the test would pass while every row was wrong.
    /// </remarks>
    public const double RoundTripToleranceArcseconds = 1e-9;
}

