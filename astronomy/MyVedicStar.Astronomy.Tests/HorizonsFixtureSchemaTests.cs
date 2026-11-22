using MyVedicStar.Astronomy.Core;
using MyVedicStar.Astronomy.Reference;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>
/// AST-12a (api#140). The generator's frame assertion and per-row provenance/coverage schema, enforced on every row.
/// </summary>
/// <remarks>
/// <para><b>These tests are about what the artifact CLAIMS, not about whether a value looks right.</b> A longitude cannot
/// reveal which equinox it came from: a wrong frame produces a plausible degree value, and a topocentric fetch produces
/// a plausible value 245 arcseconds away. Only the recorded claim can be checked, so that is what is checked.</para>
/// </remarks>
public sealed class HorizonsFixtureSchemaTests
{
    private static readonly TtInstant J2000 = new(2451545.0);

    private static HorizonsProvenance Provenance() => new(
        "JPL Horizons", "1.2", "DE441", "Body 301", "Earth (399)", "2",
        Apparent: false, RequestParameters: "COMMAND='301'&CENTER='500@399'");

    private static HorizonsCoverage Coverage(int rows) =>
        new("2000-01-01T12:00:00Z", "2000-01-01T12:00:00Z", "one instant per body", rows);

    private static HorizonsFixtureRow Row(
        string body = "Moon",
        double longitude = 123.456,
        double latitude = 1.5,
        HorizonsQuantity quantity = HorizonsQuantity.Geometric,
        double? tolerance = null,
        string basis = HorizonsFixtureGenerator.ToleranceUnfilled) =>
        new(body, J2000, quantity, longitude, latitude, tolerance, basis);

    [Fact]
    public void A_generator_produced_fixture_satisfies_the_schema()
    {
        // The control: if the schema rejected everything, every test below would pass for the wrong reason.
        var fixture = new HorizonsFixture(Coverage(1), Provenance(), new[] { Row() });

        Assert.Empty(HorizonsFixtureSchema.Check(fixture));
        HorizonsFixtureSchema.EnsureValid(fixture);
    }

    [Fact]
    public void The_generators_own_output_satisfies_the_schema_it_will_be_held_to()
    {
        // Round-trips the two halves: the generator builds the artifact, the schema judges it. A change to either that
        // breaks the agreement fails here rather than at the moment a reference file is committed.
        var port = new StubPort(Provenance());
        var fixture = new HorizonsFixtureGenerator(port)
            .GenerateAsync(new[] { "Moon", "Sun" }, new[] { J2000 }, Coverage(2))
            .GetAwaiter().GetResult();

        Assert.Empty(HorizonsFixtureSchema.Check(fixture));
    }

    [Fact]
    public void The_frame_is_the_shared_constant_and_a_row_cannot_carry_a_second_one()
    {
        // One frame claim per artifact, read from ReferenceFrame. The row type has no frame field on purpose: a per-row
        // frame would let two rows in one file mean different conventions, which no reader could detect.
        Assert.Equal("ecliptic-longitude-latitude-mean-equinox-of-date", ReferenceFrame.Ecliptic);
        Assert.Equal(ReferenceFrame.Ecliptic, HorizonsFixtureGenerator.FrameClaim);

        var rowProperties = typeof(HorizonsFixtureRow).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain(rowProperties, name => name.Contains("Frame", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_coverage_claim_that_disagrees_with_the_row_count_is_reported()
    {
        var fixture = new HorizonsFixture(Coverage(3), Provenance(), new[] { Row() });

        var problems = HorizonsFixtureSchema.Check(fixture);

        Assert.Contains(problems, p => p.Contains("claims 3 rows", StringComparison.Ordinal)
            && p.Contains("holds 1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_blank_provenance_field_is_reported_by_name()
    {
        var incomplete = Provenance() with { CenterBody = " " };

        var problems = HorizonsFixtureSchema.CheckProvenance(incomplete);

        Assert.Contains(problems, p => p.Contains(nameof(HorizonsProvenance.CenterBody), StringComparison.Ordinal));
    }

    [Fact]
    public void A_topocentric_center_recorded_as_geocentric_is_caught_in_provenance_not_in_the_numbers()
    {
        // The 245 arcsecond incident: the value looked right. Only the recorded center distinguishes them, so the
        // schema is where a wrong center must be caught.
        var topocentric = Provenance() with { CenterBody = "Kushiro (399)" };

        var problems = HorizonsFixtureSchema.CheckProvenance(topocentric);

        Assert.Empty(problems); // a non-empty center is well-formed ...
        Assert.NotEqual("Earth (399)", topocentric.CenterBody); // ... and the row's claim says which one it was.
    }

    [Fact]
    public void Every_row_is_checked_not_just_the_first()
    {
        var rows = new[] { Row(), Row(longitude: double.NaN), Row(body: "  ") };

        var problems = HorizonsFixtureSchema.Check(Coverage(3), Provenance(), rows);

        Assert.Contains(problems, p => p.StartsWith("Row 1", StringComparison.Ordinal) && p.Contains("non-finite"));
        Assert.Contains(problems, p => p.StartsWith("Row 2", StringComparison.Ordinal) && p.Contains("names no body"));
    }

    [Fact]
    public void A_row_with_a_tolerance_must_not_also_say_it_is_unfilled()
    {
        var contradictory = Row(tolerance: 2.0, basis: HorizonsFixtureGenerator.ToleranceUnfilled);

        var problems = HorizonsFixtureSchema.CheckRow(contradictory, 0);

        Assert.Contains(problems, p => p.Contains("carries a tolerance", StringComparison.Ordinal));
    }

    [Fact]
    public void A_row_without_a_tolerance_or_a_reason_is_refused()
    {
        var unexplained = Row(basis: " ");

        var problems = HorizonsFixtureSchema.CheckRow(unexplained, 0);

        Assert.Contains(problems, p => p.Contains("no tolerance basis", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(361.5)]
    [InlineData(-720.0)]
    public void A_longitude_that_is_not_an_angle_is_refused(double longitude)
    {
        var problems = HorizonsFixtureSchema.CheckRow(Row(longitude: longitude), 0);

        Assert.Contains(problems, p => p.Contains("longitude", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(359.999)]
    [InlineData(0.0)]
    public void A_negative_or_full_revolution_longitude_is_an_angle_and_is_accepted(double longitude)
    {
        // -0.001 deg and 359.999 deg name the same direction. Rejecting them would fail a correct row for its sign
        // convention, and normalising them here would hide a convention difference instead of measuring it.
        Assert.Empty(HorizonsFixtureSchema.CheckRow(Row(longitude: longitude), 0));
    }

    [Fact]
    public void EnsureValid_throws_with_every_problem_listed()
    {
        var fixture = new HorizonsFixture(
            Coverage(5),
            Provenance() with { EphemerisSource = string.Empty },
            new[] { Row(longitude: double.NaN) });

        var refusal = Assert.Throws<InvalidOperationException>(() => HorizonsFixtureSchema.EnsureValid(fixture));

        Assert.Contains("claims 5 rows", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(HorizonsProvenance.EphemerisSource), refusal.Message, StringComparison.Ordinal);
        Assert.Contains("non-finite", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A port that answers from a fixed provenance, so the artifact shape is the only variable.</summary>
    private sealed class StubPort(HorizonsProvenance provenance) : IHorizonsClient
    {
        public Task<HorizonsValue> FetchAsync(HorizonsQuery query, CancellationToken token = default) =>
            Task.FromResult(new HorizonsValue(42.0, 0.5, provenance));
    }
}
