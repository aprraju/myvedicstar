using MyVedicDay.Astronomy;
using MyVedicStar.Astronomy.Core;
using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// The graha section as the Kundali engine publishes it: nine rows, every withheld row explained, and the envelope
/// carried with the numbers.
/// </summary>
public sealed class KundaliGrahaPositionsTests
{
    private static readonly DateOnly BirthDate = new(1990, 5, 14);

    private static KundaliCalculationRequest Request(MyVedicStar.VedicProfiles.Shared.BirthTimeMode mode, TimeOnly? time) =>
        new(BirthDate, mode, time, 12.9716m, 77.5946m, "Asia/Kolkata", "Lahiri");

    private static KundaliEngine Engine(IGrahaPositionsProvider? grahaProvider) => new(new StubEphemeris(), new StubAyanamsa())
    {
        GrahaProvider = grahaProvider,
    };

    [Fact]
    public void Nine_grahas_are_returned_and_every_withheld_row_carries_a_reason()
    {
        var result = Engine(new StubProvider()).Calculate(Request(MyVedicStar.VedicProfiles.Shared.BirthTimeMode.Known,
            new TimeOnly(6, 45)));

        var grahas = result.GrahaPositions;
        Assert.NotNull(grahas);
        Assert.Equal(9, grahas!.Grahas.Count);
        Assert.All(grahas.Grahas.Where(graha => !graha.SiderealLongitude.HasValue),
            graha => Assert.False(string.IsNullOrWhiteSpace(graha.Reason)));
        Assert.All(grahas.Grahas.Where(graha => graha.SiderealLongitude.HasValue),
            graha => Assert.Null(graha.Reason));
    }

    [Fact]
    public void With_no_independent_reference_the_section_is_present_but_publishes_no_longitude()
    {
        // This is the release state, asserted so it cannot change silently: the engine computes, the gate withholds.
        var result = Engine(new StubProvider()).Calculate(Request(MyVedicStar.VedicProfiles.Shared.BirthTimeMode.Known,
            new TimeOnly(6, 45)));

        Assert.NotNull(result.GrahaPositions);
        Assert.Empty(result.GrahaPositions!.Published);
        Assert.Equal(9, result.GrahaPositions.Withheld.Count);
        Assert.Equal("MeanNode", result.GrahaPositions.NodeConvention);
        Assert.Contains("No graha tolerance is asserted", result.GrahaPositions.AccuracySummary);
    }

    [Fact]
    public void A_withheld_graha_is_also_reported_as_a_gap_at_its_own_path()
    {
        var result = Engine(new StubProvider()).Calculate(Request(MyVedicStar.VedicProfiles.Shared.BirthTimeMode.Known,
            new TimeOnly(6, 45)));

        var gap = Assert.Single(result.Gaps.Where(entry => entry.Path == "GrahaPositions.Mars"));
        Assert.Equal(KundaliCalculationReasons.UnvalidatedReference, gap.Reason);
    }

    [Fact]
    public void An_inexact_birth_time_withholds_the_whole_section_with_the_matching_reason()
    {
        var result = Engine(new StubProvider()).Calculate(Request(MyVedicStar.VedicProfiles.Shared.BirthTimeMode.Unknown,
            null));

        Assert.Null(result.GrahaPositions);
        Assert.Contains(result.Gaps, gap => gap.Path == nameof(KundaliCalculationResult.GrahaPositions)
            && gap.Reason == KundaliCalculationReasons.RequiresExactBirthTime);
    }

    [Fact]
    public void Without_a_graha_provider_the_section_is_withheld_as_not_implemented_rather_than_silently_absent()
    {
        var result = Engine(null).Calculate(Request(MyVedicStar.VedicProfiles.Shared.BirthTimeMode.Known,
            new TimeOnly(6, 45)));

        Assert.Null(result.GrahaPositions);
        Assert.Contains(result.Gaps, gap => gap.Path == nameof(KundaliCalculationResult.GrahaPositions)
            && gap.Reason == KundaliCalculationReasons.NotImplemented);
    }

    [Fact]
    public void An_independent_reference_turns_exactly_one_graha_into_a_published_placement()
    {
        var engine = new KundaliEngine(new StubEphemeris(), new StubAyanamsa())
        {
            GrahaProvider = new StubProvider(),
            PlanetaryReferences =
            [
                new PlanetaryReference(Graha.Sun, 0d, 0d, 12d, "test-only reference")
            ],
        };

        var result = engine.Calculate(Request(MyVedicStar.VedicProfiles.Shared.BirthTimeMode.Known, new TimeOnly(6, 45)));

        var sun = Assert.Single(result.GrahaPositions!.Published);
        Assert.Equal("Sun", sun.Graha);
        Assert.NotNull(sun.Sign);
        Assert.NotNull(sun.DegreeWithinSign);
        Assert.Equal(8, result.GrahaPositions.Withheld.Count);
    }

    private sealed class StubEphemeris : IEphemeris
    {
        public double GetSunLongitude(double julianDayUt) => 51.5;
        public double GetMoonLongitude(double julianDayUt) => 154.25;
        public EquatorialCoordinates GetSunEquatorial(double julianDayUt) => new(0, 0);
        public LunarEquatorialCoordinates GetMoonEquatorial(double julianDayUt) => new(0, 0, 0);
    }

    private sealed class StubAyanamsa : IAyanamsa
    {
        public double GetDegrees(double julianDayUt) => 23.85;
    }

    /// <summary>A provider that computes every graha, so the tests measure the gate rather than a stub's limitations.</summary>
    private sealed class StubProvider : IGrahaPositionsProvider
    {
        public GrahaSupport GetSupport() => new(Enum.GetValues<Graha>()
            .Select(graha => new GrahaSupportEntry(graha, true, null)).ToArray());

        public IReadOnlyList<GrahaPosition> GetSiderealPositions(double julianDayUt, double ayanamsaDegrees) =>
            Enum.GetValues<Graha>()
                .Select((graha, index) => new GrahaPosition(graha, index * 20d, index, 0d, false, 1d,
                    GrahaReasonCodes.Validated, null))
                .ToArray();
    }
}
