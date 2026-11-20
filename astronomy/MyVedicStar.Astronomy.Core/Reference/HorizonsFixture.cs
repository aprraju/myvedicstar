using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Reference;

/// <summary>
/// Where a Horizons-derived fixture row came from, recorded so the artifact can be re-fetched rather than trusted.
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> A fixture whose provenance is "a query someone ran once" cannot be
/// distinguished from a hand-written number, and the whole point of an independent reference is that it is not one.
/// The provenance block is what makes the artifact reproducible: given it, the same rows can be fetched again by
/// someone else and compared.</para>
/// <para><b>The transient parameters are part of the record.</b> Horizons output depends on the observer table, the
/// time scale, the apparent-versus-geometric flag and the ephemeris source; recording only the target and the date
/// would leave the row ambiguous in exactly the ways that produce a few arcseconds of fake disagreement.</para>
/// </remarks>
/// <param name="Service">The service queried, e.g. <c>JPL Horizons</c>. Named so a future reader knows what to ask.</param>
/// <param name="ApiVersion">The service's API version used for the fetch, e.g. <c>1.2</c>.</param>
/// <param name="EphemerisSource">The ephemeris the response was derived from, e.g. <c>DE441</c>.</param>
/// <param name="TargetBody">The target body identifier as the service reports it, e.g. <c>Moon (301)</c>.</param>
/// <param name="CenterBody">The centre body identifier, e.g. <c>Earth (399)</c>.</param>
/// <param name="ObserverTable">The observer quantity table requested, e.g. <c>1</c> (astrometric RA/Dec) or the
/// ecliptic variant. Recorded because the same instant yields different columns under different tables.</param>
/// <param name="Apparent">Whether apparent quantities were requested. <c>null</c> is not permitted: the choice is a
/// recorded decision, and an unrecorded one is what made this ambiguous in the first place.</param>
/// <param name="RequestParameters">The full query string, verbatim, so the request is re-issuable without
/// reconstruction.</param>
public sealed record HorizonsProvenance(
    string Service,
    string ApiVersion,
    string EphemerisSource,
    string TargetBody,
    string CenterBody,
    string ObserverTable,
    bool Apparent,
    string RequestParameters);

/// <summary>
/// The span and selection rule the fixture claims to cover.
/// </summary>
/// <remarks>
/// This is carried <b>inside</b> the artifact rather than inferred from which rows happen to exist. A fixture whose
/// coverage is implied by its rows cannot be checked for a gap: missing coverage and claimed coverage look identical.
/// </remarks>
/// <param name="StartUtc">First instant covered, inclusive.</param>
/// <param name="EndUtc">Last instant covered, inclusive.</param>
/// <param name="SelectionRule">How the sampled instants were chosen, in words a reader can reapply.</param>
/// <param name="RowCount">How many rows the coverage rule produced. Recorded so a truncated file is detectable.</param>
public sealed record HorizonsCoverage(
    string StartUtc,
    string EndUtc,
    string SelectionRule,
    int RowCount);

/// <summary>
/// Which quantity a row's longitude refers to.
/// </summary>
/// <remarks>
/// A recorded precision with an unstated quantity type is a claim about a number nobody specified: geometric and
/// apparent longitudes differ by aberration and nutation, which is far larger than the tolerances these rows exist to
/// hold. The enum is the quantity, so a row cannot omit it.
/// </remarks>
public enum HorizonsQuantity
{
    /// <summary>Geometric ecliptic longitude, no aberration or nutation applied.</summary>
    Geometric = 0,

    /// <summary>Apparent longitude: geometric with the stated corrections applied (see the provenance flags).</summary>
    Apparent = 1,
}

/// <summary>
/// One reference row.
/// </summary>
/// <param name="Body">The graha this row references, matching the engine's body vocabulary.</param>
/// <param name="InstantTt">The instant, explicitly in Terrestrial Time - the scale the fetched values are for.</param>
/// <param name="Quantity">Which longitude this is. Required; see <see cref="HorizonsQuantity"/>.</param>
/// <param name="LongitudeDegrees">Ecliptic longitude in degrees.</param>
/// <param name="LatitudeDegrees">Ecliptic latitude in degrees.</param>
/// <param name="ToleranceArcseconds">
/// The convergence budget for this row, or <c>null</c> when it is not yet established.
/// <para><b>Null is a legitimate value and is not a placeholder.</b> A tolerance that derives from nothing is an
/// unowned number wearing a name; leaving it null records that the budget is not yet owned, which is a fact. Filling
/// it with the number the fixture happens to measure would make the row unfalsifiable - the budget would be derived
/// from the implementation it is supposed to check.</para>
/// </param>
/// <param name="ToleranceBasis">How the budget was derived, or why it is unfilled. Required even when the tolerance
/// is null: "unfilled" is an explanation, not the absence of one.</param>
public sealed record HorizonsFixtureRow(
    string Body,
    TtInstant InstantTt,
    HorizonsQuantity Quantity,
    double LongitudeDegrees,
    double LatitudeDegrees,
    double? ToleranceArcseconds,
    string ToleranceBasis);

/// <summary>
/// The Horizons-derived fixture artifact: coverage, provenance, and rows.
/// </summary>
/// <remarks>
/// Kept in its own file, separate from <c>parity-fixtures.json</c>, so a generated artifact cannot inherit the
/// credibility of hand-derived Meeus references or lend them its own. One authority per fact.
/// </remarks>
public sealed record HorizonsFixture(
    HorizonsCoverage Coverage,
    HorizonsProvenance Provenance,
    IReadOnlyList<HorizonsFixtureRow> Rows);

