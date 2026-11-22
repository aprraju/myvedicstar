using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Reference;

/// <summary>
/// Builds the Horizons parity fixture (AST-12a, api#140): coverage, provenance and rows, from a fetch port.
/// </summary>
/// <remarks>
/// <para><b>This class contains no network code.</b> It takes an <see cref="IHorizonsClient"/> and produces the artifact,
/// so every structural rule below - the frame assertion, the per-row provenance, the coverage count - is testable
/// against a refusing or canned port without a live service. The fetch is one class on the other side of the port; if
/// this file needed the network to be reviewed, the structural half would be unreviewable until the live half worked.</para>
/// <para><b>A row is never emitted without a value from the port.</b> There is no placeholder path and no default: a
/// failed fetch aborts the whole generation rather than producing a fixture that is part hand-written. A fixture with
/// some rows missing looks exactly like a fixture whose coverage rule selected fewer instants, which is why the failure
/// is loud.</para>
/// <para><b>The frame claim is read from <see cref="ReferenceFrame"/>, not restated.</b> The rows and the assertion that
/// describes them therefore cannot drift apart: changing the constant changes what both say.</para>
/// </remarks>
public sealed class HorizonsFixtureGenerator
{
    /// <summary>The service identity recorded in every provenance block.</summary>
    /// <remarks>Fixed here rather than passed in: a generator that could be told it was talking to something else
    /// would let a wrong-service artifact carry a right-service provenance.</remarks>
    public const string ServiceName = "JPL Horizons";

    private readonly IHorizonsClient _client;

    public HorizonsFixtureGenerator(IHorizonsClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>
    /// Generates the fixture for <paramref name="bodies"/> over <paramref name="instantsTt"/>.
    /// </summary>
    /// <param name="bodies">The bodies to sample, in the engine's vocabulary.</param>
    /// <param name="instantsTt">The instants to sample, already in Terrestrial Time.</param>
    /// <param name="coverage">The span and selection rule this run claims to cover.</param>
    /// <param name="quantity">Geometric or apparent. Recorded, never inferred.</param>
    /// <param name="token">Cancellation.</param>
    /// <exception cref="ArgumentException">No bodies, no instants, or a coverage row count that disagrees with the
    /// instants actually supplied. All three are caller errors that would otherwise produce an artifact claiming more
    /// or less than it contains.</exception>
    /// <exception cref="HorizonsUnavailableException">The port refused. Nothing is returned: a partial fixture is
    /// indistinguishable from a complete one at read time.</exception>
    public async Task<HorizonsFixture> GenerateAsync(
        IReadOnlyList<string> bodies,
        IReadOnlyList<TtInstant> instantsTt,
        HorizonsCoverage coverage,
        HorizonsQuantity quantity = HorizonsQuantity.Geometric,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(instantsTt);
        ArgumentNullException.ThrowIfNull(coverage);

        if (bodies.Count == 0)
            throw new ArgumentException("A fixture with no bodies covers nothing; say so by not generating one.", nameof(bodies));

        if (instantsTt.Count == 0)
            throw new ArgumentException("A fixture with no instants covers nothing; say so by not generating one.", nameof(instantsTt));

        // The coverage record is a claim ABOUT the rows, so a disagreement is caught before the network is touched.
        // Otherwise a truncated run writes a file that still claims the full count.
        var expectedRows = bodies.Count * instantsTt.Count;
        if (coverage.RowCount != expectedRows)
        {
            throw new ArgumentException(
                $"Coverage claims {coverage.RowCount} rows but {bodies.Count} bodies x {instantsTt.Count} instants is "
                + $"{expectedRows}. A count that disagrees with the selection rule makes a truncated file undetectable.",
                nameof(coverage));
        }

        var rows = new List<HorizonsFixtureRow>(expectedRows);
        HorizonsProvenance? provenance = null;

        foreach (var body in bodies)
        {
            foreach (var instant in instantsTt)
            {
                var value = await _client.FetchAsync(new HorizonsQuery(body, instant, quantity), token)
                    .ConfigureAwait(false);

                // The port's provenance is per-response, so every row must carry one. Kept per row rather than once per
                // file: two rows from different responses (different observer tables, different ephemeris) must not be
                // able to share a single provenance block that is true of only one of them.
                provenance ??= value.Provenance;

                rows.Add(new HorizonsFixtureRow(
                    Body: body,
                    InstantTt: instant,
                    Quantity: quantity,
                    LongitudeDegrees: value.LongitudeDegrees,
                    LatitudeDegrees: value.LatitudeDegrees,
                    ToleranceArcseconds: null,
                    ToleranceBasis: ToleranceUnfilled));
            }
        }

        // Unreachable when the port is honest, but the compiler cannot know that and a silent null would propagate an
        // artifact with no provenance - the one thing that makes it distinguishable from hand-written numbers.
        if (provenance is null)
        {
            throw new HorizonsUnavailableException("no-provenance",
                "The port returned no provenance for any row, so the artifact could not be re-fetched or trusted.");
        }

        var fixture = new HorizonsFixture(coverage, provenance, rows);

        // The generator holds its own output to the artifact schema before returning it (AST-12a). This is the join
        // between the two halves: a row the generator can build but the schema rejects is a generator bug, and it must
        // surface here rather than as an unusable committed reference file.
        HorizonsFixtureSchema.EnsureValid(fixture);

        return fixture;
    }

    /// <summary>
    /// The tolerance basis every generated row carries: the budget is not yet owned.
    /// </summary>
    /// <remarks>
    /// <b>Null tolerance with a written reason, not a measured one.</b> Filling the budget with whatever this fixture
    /// happens to measure would derive the test threshold from the implementation it is meant to check, which is exactly
    /// the api#131 tautology. The number is api#144's decision and this generator must not pre-empt it.
    /// </remarks>
    public const string ToleranceUnfilled =
        "Unfilled: the convergence budget is an owned decision (api#144), not a value this generator measured. A budget "
        + "derived from the fixture it checks would make the row unfalsifiable.";

    /// <summary>
    /// The frame statement a generated artifact is only meaningful under.
    /// </summary>
    /// <remarks>Read from <see cref="ReferenceFrame"/> so the artifact's claim and the generator's behaviour are the
    /// same constant. A caller comparing rows against a differently-framed source is comparing two conventions.</remarks>
    public static string FrameClaim => ReferenceFrame.Ecliptic;
}
