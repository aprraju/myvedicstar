using MyVedicStar.Astronomy.Core;

namespace MyVedicStar.Astronomy.Tests;

/// <summary>FR-06 provider-selection parsing: strict, no silent defaults.</summary>
public sealed class ProviderSelectionTests
{
    [Theory]
    [InlineData("analytical", ProviderSelection.Analytical)]
    [InlineData("spice", ProviderSelection.Spice)]
    [InlineData("compare", ProviderSelection.Compare)]
    [InlineData("SPICE", ProviderSelection.Spice)]
    [InlineData("  Compare  ", ProviderSelection.Compare)]
    public void Known_keys_parse_to_their_selection(string key, ProviderSelection expected) =>
        Assert.Equal(expected, ProviderSelectionParser.Parse(key));

    [Theory]
    [InlineData("swisseph")]
    [InlineData("vsop87")]
    [InlineData("spice2")]
    [InlineData("false")]
    public void Unknown_keys_throw_rather_than_defaulting(string key)
    {
        var failure = Assert.Throws<ArgumentException>(() => ProviderSelectionParser.Parse(key));
        Assert.Contains("Unknown astronomy provider key", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_key_throws_rather_than_defaulting_to_analytical(string? key)
    {
        var failure = Assert.Throws<ArgumentException>(() => ProviderSelectionParser.Parse(key));
        Assert.Contains("not treated as 'analytical'", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderSelection.Analytical, "analytical")]
    [InlineData(ProviderSelection.Spice, "spice")]
    [InlineData(ProviderSelection.Compare, "compare")]
    public void Keys_round_trip(ProviderSelection selection, string expected) =>
        Assert.Equal(expected, ProviderSelectionParser.ToKey(selection));

    [Fact]
    public void An_undefined_selection_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProviderSelectionParser.ToKey((ProviderSelection)99));
    }

    [Fact]
    public void The_fixture_file_is_present_and_covers_the_port_surface()
    {
        var fixtures = ParityFixtures.Load(FindSolutionRoot());

        // The four port methods reduce to Sun and Moon longitude, so both must appear.
        Assert.Contains(fixtures, f => f.Body == CelestialBody.Sun);
        Assert.Contains(fixtures, f => f.Body == CelestialBody.Moon);
        // ADR-0016 section 4 sets the planetary budget at 1 arcsecond and the lunar budget at 3, so the ceiling is
        // body-specific rather than a single number. A fixture may not exceed the budget its own body is allowed.
        // Per-body ceiling, applied to every row regardless of claim. The unoffset series budget (about 1.8 arcsec) is
        // inside the lunar ceiling, so this now guards two claims for the Moon rather than one (api#130).
        Assert.All(fixtures, f => Assert.True(f.ToleranceArcseconds > 0 && f.ToleranceArcseconds <= (f.Body == CelestialBody.Moon ? 3.0 : 1.0),
            $"{f.Body} ({f.Claim}) tolerance {f.ToleranceArcseconds} exceeds the ADR-0016 budget for that body."));
    }

    [Fact]
    public void The_two_Moon_claims_are_separately_owned_rows_with_different_budgets()
    {
        var fixtures = ParityFixtures.Load(FindSolutionRoot());

        // api#130. Two claims about the same body and instant must be two rows, each with its own tolerance. This is the
        // property that makes the unoffset series tolerance data rather than a literal in a test: editing one row
        // changes exactly one assertion.
        var engine = ParityFixtures.ToleranceArcseconds(FindSolutionRoot(), CelestialBody.Moon, FixtureClaim.EngineReference);
        var series = ParityFixtures.ToleranceArcseconds(FindSolutionRoot(), CelestialBody.Moon, FixtureClaim.UnoffsetSeries);

        Assert.Equal(3.0, engine);
        Assert.Equal(1.8, series);
        Assert.NotEqual(engine, series);

        // And the row carries its claim explicitly, so a body-keyed lookup could not silently pick either one.
        Assert.Single(fixtures, f => f.Body == CelestialBody.Moon && f.Claim == FixtureClaim.EngineReference);
        Assert.Single(fixtures, f => f.Body == CelestialBody.Moon && f.Claim == FixtureClaim.UnoffsetSeries);

        // Only one claim has an engine value to compare, so only one may enter the parity comparison set. A series row
        // in that set would fail against GetMoonLongitude for the documented reason the offset exists.
        var comparisons = ParityFixtures.Comparisons(fixtures).ToArray();
        Assert.DoesNotContain(comparisons, f => f.Claim == FixtureClaim.UnoffsetSeries);
        Assert.Contains(comparisons, f => f.Body == CelestialBody.Moon);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyVedicStar.Astronomy.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Astronomy solution root not found.");
    }
}
