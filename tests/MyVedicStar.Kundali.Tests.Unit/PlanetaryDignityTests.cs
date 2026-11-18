using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// Dignity, combustion and aspect rules, asserted against their classical definitions.
/// </summary>
/// <remarks>
/// Each assertion here is a property of the rule, not a copy of the implementation: exaltation is the stated sign,
/// debilitation is its opposite, Parashari aspects are whole-sign with the stated special cases. No ephemeris is
/// involved, which is exactly why these can be claimed while placements cannot.
/// </remarks>
public sealed class PlanetaryDignityTests
{
    /// <summary>Midpoint of a sign, so the assertion is about the sign and not a degree boundary.</summary>
    private static double Mid(int sign) => (sign * 30.0) + 15.0;

    [Theory]
    [InlineData("Sun", 0)]
    [InlineData("Moon", 1)]
    [InlineData("Mars", 9)]
    [InlineData("Mercury", 5)]
    [InlineData("Jupiter", 3)]
    [InlineData("Venus", 11)]
    [InlineData("Saturn", 6)]
    public void A_planet_in_its_exaltation_sign_is_exalted(string planet, int sign)
    {
        Assert.Equal("Exalted", PlanetaryDignity.Of(planet, Mid(sign)));
    }

    [Theory]
    [InlineData("Sun", 0)]
    [InlineData("Moon", 1)]
    [InlineData("Mars", 9)]
    [InlineData("Mercury", 5)]
    [InlineData("Jupiter", 3)]
    [InlineData("Venus", 11)]
    [InlineData("Saturn", 6)]
    public void Debilitation_is_the_sign_opposite_exaltation_and_never_a_separate_table(string planet, int exaltSign)
    {
        // Derived rather than listed: two tables could disagree, a derivation cannot.
        Assert.Equal("Debilitated", PlanetaryDignity.Of(planet, Mid((exaltSign + 6) % 12)));
    }

    [Fact]
    public void Exaltation_takes_precedence_over_own_sign()
    {
        // Saturn is exalted in Libra and owns Capricorn and Aquarius; Libra must not read as "Own".
        Assert.Equal("Exalted", PlanetaryDignity.Of("Saturn", Mid(6)));
        Assert.Equal("Own", PlanetaryDignity.Of("Saturn", Mid(9)));
    }

    [Fact]
    public void Moolatrikona_is_a_degree_span_within_a_sign_not_the_whole_sign()
    {
        // Sun is moolatrikona in Leo 0-20 and simply "Own" in Leo 20-30.
        Assert.Equal("Moolatrikona", PlanetaryDignity.Of("Sun", 4 * 30.0 + 10.0));
        Assert.Equal("Own", PlanetaryDignity.Of("Sun", 4 * 30.0 + 25.0));
    }

    [Fact]
    public void The_moolatrikona_upper_bound_is_exclusive_so_the_two_states_never_both_claim_a_degree()
    {
        // Exactly at the boundary belongs to the weaker state, not both.
        Assert.Equal("Own", PlanetaryDignity.Of("Sun", 4 * 30.0 + 20.0));
    }

    [Fact]
    public void Friendship_dignity_follows_the_sign_ruler_not_the_sign_itself()
    {
        // Jupiter is friendly with the Sun and Mars. Aries is ruled by Mars, so Jupiter in Aries is Friendly.
        Assert.Equal("Friendly", PlanetaryDignity.Of("Jupiter", Mid(0)));
        // Taurus is ruled by Venus, an enemy of Jupiter.
        Assert.Equal("Enemy", PlanetaryDignity.Of("Jupiter", Mid(1)));
    }

    [Fact]
    public void A_sign_whose_ruler_is_neither_friend_nor_enemy_yields_neutral()
    {
        // Jupiter in Cancer is exalted, so use a planet/sign pair with no other claim: Moon in Sagittarius, ruled by
        // Jupiter, which the Moon neither counts as friend nor enemy.
        Assert.Equal("Neutral", PlanetaryDignity.Of("Moon", Mid(8)));
    }

    [Fact]
    public void The_nodes_are_not_given_an_invented_own_sign()
    {
        // Rahu owns no sign classically. Its only strong claim is exaltation in Gemini; every other sign must fall
        // through to friendship or neutral, never to "Own", which would be an invented rule.
        Assert.Equal("Exalted", PlanetaryDignity.Of("Rahu", Mid(2)));

        for (var sign = 0; sign < 12; sign++)
        {
            if (sign == 2 || sign == 8) continue; // exaltation and its opposite
            Assert.NotEqual("Own", PlanetaryDignity.Of("Rahu", Mid(sign)));
            Assert.NotEqual("Moolatrikona", PlanetaryDignity.Of("Rahu", Mid(sign)));
        }
    }

    [Fact]
    public void Every_planet_returns_a_dignity_from_the_published_vocabulary()
    {
        // A sweep over every sign and both halves: no input may produce an unlisted state.
        foreach (var planet in new[] { "Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn", "Rahu", "Ketu" })
        {
            for (var sign = 0; sign < 12; sign++)
            {
                foreach (var degree in new[] { 1.0, 15.0, 29.0 })
                {
                    var dignity = PlanetaryDignity.Of(planet, sign * 30.0 + degree);
                    Assert.Contains(dignity, PlanetaryDignity.Dignities);
                }
            }
        }
    }

    [Fact]
    public void The_sun_is_never_combust()
    {
        Assert.False(PlanetaryDignity.IsCombust("Sun", 0.0, 1.0, false));
    }

    [Fact]
    public void Combustion_uses_the_shortest_separation_across_the_zero_degree_boundary()
    {
        // 358 and 4 are 6 degrees apart, not 354. Using the raw difference would miss every conjunction at 0 Aries.
        Assert.True(PlanetaryDignity.IsCombust("Moon", 358.0, 4.0, false));
        Assert.Equal(6.0, PlanetaryDignity.AngularSeparation(358.0, 4.0), 6);
    }

    [Fact]
    public void Mercury_and_Venus_have_a_different_orb_retrograde_than_direct()
    {
        // The distinction is real, not cosmetic: a single orb would be wrong in one of the two states.
        Assert.True(PlanetaryDignity.CombustionOrb("Mercury", true) < PlanetaryDignity.CombustionOrb("Mercury", false));
        Assert.True(PlanetaryDignity.CombustionOrb("Venus", true) < PlanetaryDignity.CombustionOrb("Venus", false));
    }

    [Fact]
    public void The_nodes_are_never_combust()
    {
        Assert.Null(PlanetaryDignity.CombustionOrb("Rahu", false));
        Assert.False(PlanetaryDignity.IsCombust("Rahu", 100.0, 100.5, false));
    }

    [Fact]
    public void Every_planet_aspects_the_seventh_sign_from_itself()
    {
        foreach (var planet in new[] { "Sun", "Moon", "Mars", "Mercury", "Jupiter", "Venus", "Saturn" })
        {
            for (var from = 0; from < 12; from++)
                Assert.True(PlanetaryDignity.Aspects(planet, from, (from + 6) % 12));
        }
    }

    [Theory]
    [InlineData("Mars", 3)]
    [InlineData("Mars", 7)]
    [InlineData("Jupiter", 4)]
    [InlineData("Jupiter", 8)]
    [InlineData("Saturn", 2)]
    [InlineData("Saturn", 9)]
    public void The_special_whole_sign_aspects_are_present_for_their_planets_only(string planet, int offset)
    {
        Assert.True(PlanetaryDignity.Aspects(planet, 0, offset));

        // And a planet without that special aspect must not claim it.
        var other = planet switch { "Mars" => "Saturn", "Jupiter" => "Mars", _ => "Jupiter" };
        Assert.False(PlanetaryDignity.Aspects(other, 0, offset));
    }

    [Fact]
    public void A_planet_does_not_aspect_its_own_sign()
    {
        foreach (var planet in new[] { "Mars", "Jupiter", "Saturn" })
            Assert.False(PlanetaryDignity.Aspects(planet, 4, 4));
    }

    [Fact]
    public void Aspected_signs_agrees_with_the_single_aspect_predicate()
    {
        // Guards against the two entry points drifting: one is derived from the other, and this proves it.
        foreach (var planet in new[] { "Sun", "Mars", "Jupiter", "Saturn" })
        {
            for (var from = 0; from < 12; from++)
            {
                var listed = PlanetaryDignity.AspectedSigns(planet, from);
                for (var to = 0; to < 12; to++)
                    Assert.Equal(PlanetaryDignity.Aspects(planet, from, to), listed.Contains(to));
            }
        }
    }

    [Fact]
    public void An_out_of_range_sign_is_rejected_rather_than_wrapped_silently()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetaryDignity.Aspects("Mars", 12, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetaryDignity.Aspects("Mars", -1, 0));
    }
}
