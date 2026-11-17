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
        Assert.All(fixtures, f => Assert.True(f.ToleranceArcseconds > 0 && f.ToleranceArcseconds <= (f.Body == CelestialBody.Moon ? 3.0 : 1.0),
            $"{f.Body} tolerance {f.ToleranceArcseconds} exceeds the ADR-0016 budget for that body."));
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyVedicStar.Astronomy.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Astronomy solution root not found.");
    }
}
