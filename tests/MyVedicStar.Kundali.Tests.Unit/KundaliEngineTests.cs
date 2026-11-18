using MyVedicDay.Astronomy;
using MyVedicStar.Kundali.Calculation;
using MyVedicStar.Kundali.Tests.Unit;
using NodaTime;
using VedicProfiles.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// KUN-20 slice 1. The assertions that matter are the tolerance checks - a value "close enough" without a stated
/// tolerance is not a spec - and the suppression behaviour, where a value must be absent rather than guessed.
/// </summary>
public sealed class KundaliEngineTests
{
    /// <summary>
    /// Reference values taken from Meeus, Astronomical Algorithms (2nd ed.), already committed in the repository at
    /// astronomy/parity-fixtures.json. Reused rather than re-typed so the two cannot drift apart.
    /// </summary>
    private const double SunReferenceTropical = 199.901641;


    private static KundaliCalculationRequest Request(BirthTimeMode mode, TimeOnly? time = null) =>
        new(new DateOnly(1992, 10, 13), mode, time, 13.0827m, 80.2707m, "UTC", "Lahiri");

    [Fact]
    public void Sidereal_conversion_subtracts_the_ayanamsa_and_wraps_rather_than_going_negative()
    {
        // 100 tropical - 24 ayanamsa = 76 sidereal (Gemini, sign 2). The wrap case matters more: a value below the
        // ayanamsa must come out near 360, not negative, or every early-Aries chart would be wrong.
        var engine = new KundaliEngine(new FixedEphemeris(100.0, 20.0), new FixedAyanamsa(24.0));

        var result = engine.Calculate(Request(BirthTimeMode.Known, new TimeOnly(6, 45)));

        Assert.Equal(76.0, result.SiderealSunLongitude!.Value, 9);
        Assert.Equal(2, result.SunSign);
        // 20 - 24 = -4, which must wrap to 356 (Pisces), not clamp at 0.
        Assert.Equal(356.0, result.SiderealMoonLongitude!.Value, 9);
        Assert.Equal(11, result.MoonSign);
    }

    [Fact]
    public void Sign_is_the_whole_sign_containing_the_longitude_and_boundaries_belong_to_the_later_sign()
    {
        Assert.Equal(0, KundaliEngine.SignOf(0.0));      // 0 Aries
        Assert.Equal(0, KundaliEngine.SignOf(29.999));
        Assert.Equal(1, KundaliEngine.SignOf(30.0));     // 30 Taurus - boundary belongs to the later sign
        Assert.Equal(11, KundaliEngine.SignOf(359.999)); // 359 Pisces
        Assert.Equal(0, KundaliEngine.SignOf(360.0));    // wraps to 0 Aries
    }

    [Theory]
    [InlineData(-1.0, 359.0)]
    [InlineData(-30.0, 330.0)]
    [InlineData(361.0, 1.0)]
    public void Longitude_normalisation_wraps_forward_rather_than_clamping(double input, double expected)
    {
        Assert.Equal(expected, KundaliEngine.Normalize(input), 9);
    }

    [Theory]
    [InlineData(BirthTimeMode.Approximate)]
    [InlineData(BirthTimeMode.Unknown)]
    public void A_non_exact_birth_time_suppresses_every_time_dependent_value_and_says_why(BirthTimeMode mode)
    {
        var engine = new KundaliEngine(new FixedEphemeris(120.0, 45.0), new FixedAyanamsa(0.0));

        var result = engine.Calculate(Request(mode));

        // Absence, not a midpoint guess. A wrong ascendant is worse than an absent one.
        Assert.Null(result.SiderealSunLongitude);
        Assert.Null(result.SiderealMoonLongitude);
        Assert.Null(result.SunSign);
        Assert.Null(result.MoonSign);

        Assert.All(result.Gaps, gap => Assert.Equal(
            KundaliCalculationReasons.RequiresExactBirthTime, gap.Reason));
        Assert.Equal(4, result.Gaps.Count);
    }

    [Fact]
    public void The_ascendant_is_withheld_as_unvalidated_not_as_a_missing_capability()
    {
        var engine = new KundaliEngine(new FixedEphemeris(120.0, 45.0), new FixedAyanamsa(0.0));

        var result = engine.Calculate(Request(BirthTimeMode.Known, new TimeOnly(6, 45)));

        var ascendant = Assert.Single(result.Gaps, g => g.Path == "Ascendant");
        // Distinct from requires-exact-birth-time: this is computable, just unproven. The distinction tells the next
        // person it is a validation task, not a computation one.
        Assert.Equal(KundaliCalculationReasons.UnvalidatedReference, ascendant.Reason);
    }

    [Fact]
    public void Conventions_are_reported_at_the_root_so_no_value_is_read_without_them()
    {
        var engine = new KundaliEngine(new FixedEphemeris(120.0, 45.0), new FixedAyanamsa(0.0));

        var result = engine.Calculate(Request(BirthTimeMode.Known, new TimeOnly(6, 45)));

        Assert.Equal("Sidereal", result.Conventions.Zodiac);
        Assert.Equal("Lahiri", result.Conventions.Ayanamsa);
        Assert.Equal("analytical", result.Conventions.Accuracy.EphemerisProvider);
        // The accuracy envelope is published rather than hidden, so a consumer knows what produced the number.
        Assert.Contains("arcsecond", result.Conventions.Accuracy.TropicalLongitudeToleranceArcseconds);
    }

    /// <summary>Returns fixed tropical longitudes, so the engine's own logic is tested without provider noise.</summary>
    private sealed class FixedEphemeris(double sun, double moon) : IEphemeris
    {
        public double GetSunLongitude(double julianDayUt) => sun;
        public double GetMoonLongitude(double julianDayUt) => moon;
        public EquatorialCoordinates GetSunEquatorial(double julianDayUt) => new(0, 0);
        public LunarEquatorialCoordinates GetMoonEquatorial(double julianDayUt) => new(0, 0, 0.9);
    }

    private sealed class FixedAyanamsa(double degrees) : IAyanamsa
    {
        public double GetDegrees(double julianDayUt) => degrees;
    }
}

