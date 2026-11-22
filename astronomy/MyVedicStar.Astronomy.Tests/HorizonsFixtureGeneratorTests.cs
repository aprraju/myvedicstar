using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Reference;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>
/// AST-12a (api#140). The generator's structural half, exercised through the fetch port so no test needs the network.
/// </summary>
/// <remarks>
/// <para>Each test fails for one reason: a row without a real value, a coverage claim that disagrees with the rows, a
/// frame recorded differently from the one the rows are in, or a failed fetch that produces a fixture anyway. The value
/// itself is never asserted here - a canned port makes any number meaningless, and asserting one would be a test that
/// agrees with its own input.</para>
/// </remarks>
public sealed class HorizonsFixtureGeneratorTests
{
    private static readonly TtInstant J2000 = new(2451545.0);

    /// <summary>A coverage claim for the given bodies and instants, so the arithmetic is never hand-copied.</summary>
    private static HorizonsCoverage Coverage(int bodies, int instants) =>
        new("2000-01-01T12:00:00Z", "2000-01-01T12:00:00Z", "one instant at J2000 per body", bodies * instants);

    [Fact]
    public async Task Every_row_carries_the_provenance_of_the_response_it_came_from()
    {
        var port = new CannedPort("DE441");
        var fixture = await new HorizonsFixtureGenerator(port).GenerateAsync(
            new[] { "Moon" }, new[] { J2000 }, Coverage(1, 1));

        var row = Assert.Single(fixture.Rows);
        Assert.Equal("Moon", row.Body);
        Assert.Equal(J2000, row.InstantTt);
        Assert.Equal(HorizonsQuantity.Geometric, row.Quantity);
        Assert.Equal("DE441", fixture.Provenance.EphemerisSource);

        // The value is the port's, not a default: a generator that could emit a row without asking has no reference.
        Assert.Equal(port.Value.LongitudeDegrees, row.LongitudeDegrees);
        Assert.Equal(port.Value.LatitudeDegrees, row.LatitudeDegrees);
        Assert.Equal(1, port.Calls);
    }

    [Fact]
    public async Task Rows_are_emitted_in_body_major_order_so_the_artifact_is_reproducible()
    {
        var port = new CannedPort("DE441");
        var later = new TtInstant(2451546.0);

        var fixture = await new HorizonsFixtureGenerator(port).GenerateAsync(
            new[] { "Moon", "Sun" }, new[] { J2000, later }, Coverage(2, 2));

        Assert.Equal(
            new[] { "Moon@2451545", "Moon@2451546", "Sun@2451545", "Sun@2451546" },
            fixture.Rows.Select(r => $"{r.Body}@{r.InstantTt.JulianDay:F0}"));
    }

    [Fact]
    public async Task A_coverage_count_that_disagrees_with_the_selection_is_refused_before_any_fetch()
    {
        // A truncated run would otherwise write a file still claiming the full count, and a short fixture is
        // indistinguishable from a smaller selection rule at read time.
        var port = new CannedPort("DE441");
        var wrong = new HorizonsCoverage("2000-01-01T00:00:00Z", "2000-01-01T00:00:00Z", "one instant per body", 99);

        var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            new HorizonsFixtureGenerator(port).GenerateAsync(new[] { "Moon" }, new[] { J2000 }, wrong));

        Assert.Contains("99", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, port.Calls);
    }

    [Fact]
    public async Task Empty_selection_is_a_caller_error_not_an_empty_fixture()
    {
        var port = new CannedPort("DE441");
        var generator = new HorizonsFixtureGenerator(port);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync(Array.Empty<string>(), new[] { J2000 }, Coverage(0, 1)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            generator.GenerateAsync(new[] { "Moon" }, Array.Empty<TtInstant>(), Coverage(1, 0)));

        Assert.Equal(0, port.Calls);
    }

    [Fact]
    public async Task A_refusing_port_aborts_the_run_rather_than_returning_a_partial_fixture()
    {
        // The port refuses on its second call: the failure must surface, not be swallowed into a file with one row less.
        var port = new CannedPort("DE441") { FailOnCall = 2 };

        await Assert.ThrowsAsync<HorizonsUnavailableException>(() =>
            new HorizonsFixtureGenerator(port).GenerateAsync(
                new[] { "Moon" }, new[] { J2000, new TtInstant(2451546.0) }, Coverage(1, 2)));
    }

    [Fact]
    public async Task The_refusal_distinguishes_unreachable_from_unresolved_parameters()
    {
        // Both are "no value", and they need different fixes - sending someone to the network for a parameter problem
        // is the failure mode the port's exception type exists to prevent. Asserted through the generator, because a
        // hand-built exception would prove the constructor works and not that the port reports the right reason.
        var unreachablePort = new CannedPort("DE441") { FailOnCall = 1 };
        var unreachable = await Assert.ThrowsAsync<HorizonsUnavailableException>(() =>
            new HorizonsFixtureGenerator(unreachablePort).GenerateAsync(new[] { "Moon" }, new[] { J2000 }, Coverage(1, 1)));

        var unresolved = await Assert.ThrowsAsync<HorizonsUnavailableException>(() =>
            new HorizonsFixtureGenerator(new RefusingHorizonsClient())
                .GenerateAsync(new[] { "Moon" }, new[] { J2000 }, Coverage(1, 1)));

        Assert.Equal("unreachable", unreachable.Reason);
        Assert.Equal("parameters-unresolved", unresolved.Reason);
        Assert.NotEqual(unreachable.Reason, unresolved.Reason);

        // And neither produced a fixture: there is no path from a refusal to an artifact.
        Assert.Contains(unreachable.Reason, unreachable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_rows_leave_the_tolerance_unfilled_and_say_why()
    {
        var port = new CannedPort("DE441");
        var fixture = await new HorizonsFixtureGenerator(port).GenerateAsync(
            new[] { "Moon" }, new[] { J2000 }, Coverage(1, 1));

        var row = Assert.Single(fixture.Rows);

        // A budget derived from the fixture it checks is unfalsifiable (the api#131 tautology); the number is api#144's.
        Assert.Null(row.ToleranceArcseconds);
        Assert.Equal(HorizonsFixtureGenerator.ToleranceUnfilled, row.ToleranceBasis);
        Assert.False(string.IsNullOrWhiteSpace(row.ToleranceBasis));
    }

    [Fact]
    public void The_generators_frame_claim_is_the_shared_reference_constant()
    {
        // Read, not restated: the artifact's claim and the generator's behaviour are one constant, so a frame change
        // cannot update one and leave the other asserting the old convention.
        Assert.Equal(ReferenceFrame.Ecliptic, HorizonsFixtureGenerator.FrameClaim);
        Assert.Equal("ecliptic-longitude-latitude-mean-equinox-of-date", HorizonsFixtureGenerator.FrameClaim);
        Assert.Equal("TT", ReferenceFrame.TimeScale);
        Assert.Equal("JPL Horizons", HorizonsFixtureGenerator.ServiceName);
    }

    [Fact]
    public void The_round_trip_bound_is_tighter_than_any_budget_this_artifact_would_hold()
    {
        // A round trip whose bound is the same order as a row budget passes a frame error that cancels inside it.
        // The strongest claim available while the budgets are unfilled is that the bound is below the smallest
        // plausible one; 1e-9 arcsec is far below any of them.
        Assert.True(ReferenceFrame.RoundTripToleranceArcseconds < 1e-3,
            "The round-trip bound must be tighter than every row budget it is a guard for.");

        var start = new TtInstant(2451545.0);
        var degrees = 217.2933209;
        var returned = (degrees + ReferenceFrame.RoundTripToleranceArcseconds / 3600.0)
            - ReferenceFrame.RoundTripToleranceArcseconds / 3600.0;

        Assert.InRange(Math.Abs(degrees - returned) * 3600.0, 0.0, ReferenceFrame.RoundTripToleranceArcseconds);
    }

    /// <summary>A port that answers from a fixed value and counts its calls, so "did it fetch" is observable.</summary>
    private sealed class CannedPort(string ephemeris) : IHorizonsClient
    {
        public HorizonsValue Value { get; } = new(
            123.456789, 1.234567,
            new HorizonsProvenance("JPL Horizons", "1.2", ephemeris, "Body 301", "Earth (399)", "2",
                Apparent: false, RequestParameters: "COMMAND='301'&CENTER='500@399'"));

        public int Calls { get; private set; }

        /// <summary>Call number that refuses, 1-based. Null never refuses.</summary>
        public int? FailOnCall { get; init; }

        public Task<HorizonsValue> FetchAsync(HorizonsQuery query, CancellationToken token = default)
        {
            Calls++;
            if (FailOnCall == Calls)
            {
                throw new HorizonsUnavailableException("unreachable",
                    "Request failed: the service could not be reached.");
            }

            return Task.FromResult(Value);
        }
    }
}
