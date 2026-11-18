using MyVedicStar.Astronomy.Core;

namespace MyVedicDay.Astronomy.Tests.Calculation;

/// <summary>
/// The nine-graha surface, asserted where the analytical provider is reachable.
/// </summary>
/// <remarks>
/// <para><b>What these tests do and do not claim.</b> They prove that every graha is computed, that the grahas agree with
/// the Sun/Moon port, that the node pair is 180 degrees apart, and that the retrograde flag follows the sign of the
/// longitude derivative against an independent numerical check. They do <b>not</b> establish absolute accuracy for the
/// seven bodies with no checked-in independent reference - and the tests assert exactly that: those bodies are withheld
/// as <c>unvalidated-reference</c> rather than published with a guessed tolerance.</para>
/// <para>Reference values found in Meeus chapter 33 (Venus, Mars, Jupiter and Saturn worked examples) are latitude and
/// distance, or a J2000 frame with a different time argument, so they are not directly usable as tropical longitudes of
/// date. Rather than convert them by hand - which would be reproducing the very series under test - the absence of a
/// defensible reference is recorded honestly, which is what the owner and architect asked for.</para>
/// </remarks>
public sealed class GrahaPositionsTests
{
    private static readonly MeeusGrahaPositionsProvider Provider = new();

    /// <summary>Meeus example 25.b instant, converted to UT exactly as the checked-in Sun fixture records.</summary>
    private const double Instant = 2448908.4993171296;

    private const double Ayanamsa = 23.7;

    [Fact]
    public void Every_graha_is_computed_by_the_analytical_provider()
    {
        var support = Provider.GetSupport();

        Assert.True(support.CoversAllGrahas, "A computed graha is missing from the provider's own declaration.");
        Assert.Equal(9, support.Entries.Count);
        Assert.All(support.Entries, entry => Assert.Null(entry.Reason));
    }

    [Fact]
    public void All_nine_grahas_are_returned_in_zodiac_independent_order()
    {
        var positions = Provider.GetSiderealPositions(Instant, Ayanamsa);

        Assert.Equal(9, positions.Count);
        Assert.Equal(Enum.GetValues<Graha>(), positions.Select(position => position.Graha).ToArray());
    }

    [Fact]
    public void Sun_and_Moon_match_the_ephemeris_port_the_rest_of_the_domain_uses()
    {
        // The graha surface must not become a second calculation path: if it did, a Kundali chart and a Panchang would
        // disagree about where the Moon was, and only one of them would be wrong.
        var ephemeris = new MeeusEphemeris();
        var positions = Provider.GetSiderealPositions(Instant, Ayanamsa);

        var sun = positions.Single(position => position.Graha == Graha.Sun).SiderealLongitude!.Value;
        var moon = positions.Single(position => position.Graha == Graha.Moon).SiderealLongitude!.Value;

        var expectedSun = Normalize(ephemeris.GetSunLongitude(Instant) - Ayanamsa);
        var expectedMoon = Normalize(ephemeris.GetMoonLongitude(Instant) - Ayanamsa);

        Assert.True(SeparationArcseconds(sun, expectedSun) < 1e-6);
        Assert.True(SeparationArcseconds(moon, expectedMoon) < 1e-6);
    }

    [Fact]
    public void Rahu_and_Ketu_are_exactly_opposite_and_derived_from_the_mean_node()
    {
        var positions = Provider.GetSiderealPositions(Instant, Ayanamsa);
        var rahu = positions.Single(position => position.Graha == Graha.Rahu).SiderealLongitude!.Value;
        var ketu = positions.Single(position => position.Graha == Graha.Ketu).SiderealLongitude!.Value;

        Assert.Equal(180d * 3600d, SeparationArcseconds(rahu, ketu), 6);
    }

    [Fact]
    public void The_mean_node_retrogrades()
    {
        // An independent property, not a golden value: the mean node's longitude falls monotonically, so a node that
        // moved forwards would be a sign error in the series rather than a calibration question.
        var earlier = Provider.TropicalLongitude(Graha.Rahu, Instant);
        var later = Provider.TropicalLongitude(Graha.Rahu, Instant + 30);

        Assert.True(AngleMath.NormalizeSignedDegrees(later - earlier) < 0);
        Assert.True(Provider.IsRetrograde(Graha.Rahu, Instant));
        Assert.True(Provider.IsRetrograde(Graha.Ketu, Instant));
    }

    [Fact]
    public void The_sun_is_never_retrograde()
    {
        Assert.False(Provider.IsRetrograde(Graha.Sun, Instant));
    }

    [Fact]
    public void Retrograde_flags_agree_with_an_independent_finite_difference_of_longitude()
    {
        // The flag is derived from the longitude derivative; this recomputes the derivative from the published
        // longitudes over a day and requires the sign to match. A sign convention error cannot pass both.
        foreach (var graha in new[] { Graha.Mercury, Graha.Venus, Graha.Mars, Graha.Jupiter, Graha.Saturn })
        {
            var before = Provider.TropicalLongitude(graha, Instant - 1);
            var after = Provider.TropicalLongitude(graha, Instant + 1);
            var moving = AngleMath.NormalizeSignedDegrees(after - before) < 0;

            Assert.Equal(moving, Provider.IsRetrograde(graha, Instant));
        }
    }

    [Fact]
    public void Each_placement_lies_inside_the_sign_it_reports()
    {
        var positions = Provider.GetSiderealPositions(Instant, Ayanamsa);

        foreach (var position in positions)
        {
            Assert.InRange(position.SiderealLongitude!.Value, 0d, 359.9999999999);
            Assert.Equal(Math.Floor(position.SiderealLongitude.Value / 30d), (double)position.Sign!.Value);
            Assert.InRange(position.DegreeWithinSign!.Value, 0d, 30d);
        }
    }

    [Fact]
    public void A_reference_less_body_is_withheld_by_the_resolver_not_published_with_a_guessed_tolerance()
    {
        // The core rule of this change: computable is not the same as publishable.
        var resolver = new GrahaPositionsResolver(Provider);
        var conventions = new GrahaConventions("Sidereal", "Lahiri", "23.7", "MeanNode", "analytical", "test");
        var result = resolver.Resolve(Instant, Ayanamsa, conventions, []);

        Assert.All(result.Grahas, graha =>
        {
            Assert.Null(graha.SiderealLongitude);
            Assert.Equal(GrahaReasonCodes.UnvalidatedReference, graha.Status);
            Assert.NotNull(graha.Reason);
        });
        Assert.Empty(result.Accuracy.PerBodyArcseconds);
    }

    [Fact]
    public void A_body_with_a_checked_in_reference_is_published_and_bounded_by_its_own_tolerance()
    {
        var resolver = new GrahaPositionsResolver(Provider);
        var conventions = new GrahaConventions("Sidereal", "Lahiri", "23.7", "MeanNode", "analytical", "test");
        var reference = new PlanetaryReference(Graha.Mars, Instant,
            AngleMath.NormalizeDegrees(Provider.TropicalLongitude(Graha.Mars, Instant)), 30d, "test fixture");

        var result = resolver.Resolve(Instant, Ayanamsa, conventions, [reference]);

        var mars = result.Grahas.Single(graha => graha.Graha == Graha.Mars);
        Assert.NotNull(mars.SiderealLongitude);
        Assert.Equal(GrahaReasonCodes.Validated, mars.Status);
        Assert.Equal(30d, result.Accuracy.PerBodyArcseconds["Mars"]);

        // Every other body is still withheld: publishing one adds evidence for one, not for all.
        Assert.Equal(8, result.Grahas.Count(graha => !graha.SiderealLongitude.HasValue));
    }

    [Fact]
    public void The_shipped_reference_file_starts_empty_rather_than_with_placeholders()
    {
        // If this ever fails, someone has checked in a value that must be traceable to a published source.
        var references = PlanetaryReferences.LoadFromAncestors(AppContext.BaseDirectory);

        Assert.Empty(references);
    }

    [Fact]
    public void The_envelope_states_that_the_parity_budget_is_not_an_accuracy_claim()
    {
        var resolver = new GrahaPositionsResolver(Provider);
        var envelope = resolver.BuildEnvelope([]);

        Assert.Contains("not an accuracy claim", envelope.Basis);
        Assert.Contains("Sidereal", new GrahaConventions("Sidereal", "Lahiri", "0", "MeanNode", "analytical", "x").Zodiac);
    }

    private static double Normalize(double degrees)
    {
        var wrapped = degrees % 360d;
        return wrapped < 0 ? wrapped + 360d : wrapped;
    }

    private static double SeparationArcseconds(double first, double second) =>
        Math.Abs(((first - second) % 360d + 540d) % 360d - 180d) * 3600d;
}
