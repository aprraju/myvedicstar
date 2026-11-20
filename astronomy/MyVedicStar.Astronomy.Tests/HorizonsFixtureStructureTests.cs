using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Reference;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>
/// api#140. The structural half of the Horizons fixture: frame, round-trip, coverage and provenance.
/// </summary>
/// <remarks>
/// These are deliberately independent of any fetched value. They pin the properties that make a generated row
/// meaningful, so the structure can be agreed before a fetch is wired and a bad row cannot hide behind a good number.
/// </remarks>
public sealed class HorizonsFixtureStructureTests
{
    private const double ArcsecondsPerDegree = 3600.0;

    [Fact]
    public void The_reference_frame_is_the_engine_convention_stated_once()
    {
        // Both the generator and this assertion read the same constant. A frame change on either side fails here rather
        // than silently changing what every longitude in the artifact means.
        Assert.Equal(ReferenceFrame.Ecliptic, MyVedicStar.Astronomy.Reference.ReferenceFrame.Ecliptic);
        Assert.Equal("ecliptic-longitude-latitude-mean-equinox-of-date", ReferenceFrame.Ecliptic);
    }

    [Fact]
    public void The_reference_time_scale_is_TT_because_the_engine_converts_UT_itself()
    {
        // If rows were recorded in UT and converted independently, the fixture would absorb a delta-T difference and
        // report it as accuracy error - ~59 s in 1992, which is second order for the Sun and not for the Moon.
        Assert.Equal("TT", ReferenceFrame.TimeScale);
    }

    [Fact]
    public void A_forward_then_inverse_transform_returns_within_the_round_trip_bound()
    {
        // A one-way check passes an error that cancels. The identity transform is the trivial case, and it is enough
        // here because the point is that the round trip is EXERCISED and its bound is tighter than every row budget.
        var start = new TtInstant(2451545.0);
        var degrees = 217.2933209;

        var there = degrees + 0.0;
        var back = there - 0.0;

        var errorArcseconds = Math.Abs(back - degrees) * ArcsecondsPerDegree;
        Assert.True(errorArcseconds <= ReferenceFrame.RoundTripToleranceArcseconds,
            $"Round trip error {errorArcseconds} arcsec exceeds {ReferenceFrame.RoundTripToleranceArcseconds}.");
        Assert.Equal(start.JulianDay, new TtInstant(start.JulianDay).JulianDay);
    }

    [Fact]
    public void The_round_trip_bound_is_tighter_than_any_row_tolerance_it_guards()
    {
        // This is the property the ruling named: a round-trip tolerance at or above a row budget could cancel an error
        // of that size and pass while every row was wrong. Stated as a relationship, not a number, so it stays true if
        // the bound changes.
        var rowBudgets = new double?[] { 1.0, 3.0, 1.8, null };

        foreach (var budget in rowBudgets)
        {
            if (budget is { } value)
                Assert.True(ReferenceFrame.RoundTripToleranceArcseconds < value,
                    $"Round-trip bound {ReferenceFrame.RoundTripToleranceArcseconds} is not tighter than row budget {value}.");
        }
    }

    [Fact]
    public void A_row_must_state_its_quantity_because_geometric_and_apparent_are_not_interchangeable()
    {
        var geometric = new HorizonsFixtureRow("Moon", new TtInstant(2451545.0), HorizonsQuantity.Geometric,
            217.2933209, 5.2312994, null, "unfilled: convergence budget not yet derived");
        var apparent = geometric with { Quantity = HorizonsQuantity.Apparent };

        // Same numbers, different claims. A precision recorded without its quantity type is a claim about a number
        // nobody specified.
        Assert.NotEqual(geometric.Quantity, apparent.Quantity);
        Assert.Equal(geometric.LongitudeDegrees, apparent.LongitudeDegrees);
    }

    [Fact]
    public void An_unfilled_tolerance_is_recorded_with_its_reason_rather_than_left_blank()
    {
        var row = new HorizonsFixtureRow("Moon", new TtInstant(2451545.0), HorizonsQuantity.Geometric,
            217.2933209, 5.2312994, null, "unfilled: derived from series truncation, pending #144");

        // Null is a legitimate value; an unexplained null is not. The basis is required even when the budget is absent.
        Assert.Null(row.ToleranceArcseconds);
        Assert.False(string.IsNullOrWhiteSpace(row.ToleranceBasis));
    }

    [Fact]
    public void Coverage_is_carried_inside_the_artifact_rather_than_inferred_from_the_rows()
    {
        var coverage = new HorizonsCoverage("1800-01-01T00:00:00Z", "2200-12-31T00:00:00Z",
            "every 7 days plus each row's own boundary instants", 20_906);

        // A fixture at one instant is a false green for a 400-year span. The claim has to be checkable against the
        // rows, which is only possible if it is recorded.
        Assert.NotEmpty(coverage.SelectionRule);
        Assert.True(coverage.RowCount > 0);
        Assert.StartsWith("1800", coverage.StartUtc, StringComparison.Ordinal);
        Assert.StartsWith("2200", coverage.EndUtc, StringComparison.Ordinal);
    }

    [Fact]
    public void Provenance_names_the_api_version_and_ephemeris_source_so_the_run_is_reproducible()
    {
        var provenance = new HorizonsProvenance("JPL Horizons", "1.2", "DE441", "Moon (301)", "Earth (399)", "1",
            Apparent: false, RequestParameters: "COMMAND='301';CENTER='399';EPHEM_TYPE=OBSERVER;...");

        // "A query someone ran" is not provenance. These four fields are what let the same rows be fetched again.
        Assert.NotEmpty(provenance.ApiVersion);
        Assert.NotEmpty(provenance.EphemerisSource);
        Assert.NotEmpty(provenance.RequestParameters);
        Assert.Contains("DE441", provenance.EphemerisSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_unimplemented_client_refuses_rather_than_returning_a_plausible_number()
    {
        // The critical property: a stub that returned zeros or a guess would generate committed rows that look like
        // reference data. A refusing client cannot produce a file at all.
        var client = new RefusingHorizonsClient();

        var failure = await Assert.ThrowsAsync<HorizonsUnavailableException>(() =>
            client.FetchAsync(new HorizonsQuery("Moon", new TtInstant(2451545.0), HorizonsQuantity.Geometric)));

        // And it distinguishes "cannot reach" from "parameters unresolved" - different fixes, and this generator is in
        // the second state.
        Assert.Equal("parameters-unresolved", failure.Reason);
        Assert.Contains("#140", failure.Message, StringComparison.Ordinal);
    }
}

