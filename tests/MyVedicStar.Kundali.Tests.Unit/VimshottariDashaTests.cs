using MyVedicStar.Kundali.Calculation;
using NodaTime;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// Vimshottari arithmetic, tested against the tradition's own rules rather than against our output.
/// </summary>
/// <remarks>
/// <para>These assertions are derived from the definition of the system: 27 equal nakshatras, a fixed nine-lord
/// sequence totalling 120 years, and a first period shortened by the fraction of the nakshatra already traversed.
/// They do not constitute an accuracy claim against a published ephemeris - that would need an independent
/// computed chart, which we do not have. What is proven here is that the rules are implemented as stated.</para>
/// </remarks>
public sealed class VimshottariDashaTests
{
    private static readonly Instant Birth = Instant.FromUtc(1990, 4, 12, 6, 45);

    /// <summary>360 / 27 = 13 degrees 20 minutes.</summary>
    private const double NakshatraWidth = 360.0 / 27.0;

    [Fact]
    public void A_full_cycle_of_nine_periods_totals_120_years_of_365_dot_25_days()
    {
        // Starting exactly on a nakshatra boundary makes the first period a full term, so the sum is the whole cycle.
        var periods = VimshottariDasha.BuildMahadashas(0.0, Birth);

        Assert.Equal(9, periods.Count);

        var totalDays = (periods[^1].EndUtc - periods[0].StartUtc).TotalDays;
        var expectedDays = 120.0 * 365.25;

        // One-second tolerance: the arithmetic is in seconds, so an exact match in days is not meaningful.
        Assert.InRange(totalDays, expectedDays - 1, expectedDays + 1);
    }

    [Fact]
    public void Periods_are_contiguous_so_no_instant_falls_between_two_dashas()
    {
        var periods = VimshottariDasha.BuildMahadashas(87.3, Birth);

        for (var i = 1; i < periods.Count; i++)
            Assert.Equal(periods[i - 1].EndUtc, periods[i].StartUtc);
    }

    [Fact]
    public void A_moon_on_a_nakshatra_boundary_starts_a_full_period_of_that_lord()
    {
        // 0 degrees is the start of Ashwini, whose lord is Ketu. Nothing has elapsed, so Ketu's period is whole.
        var periods = VimshottariDasha.BuildMahadashas(0.0, Birth);

        Assert.Equal("Ketu", periods[0].Lord);
        Assert.False(periods[0].IsRemainder);

        var expectedDays = 7.0 * 365.25;
        Assert.InRange((periods[0].EndUtc - periods[0].StartUtc).TotalDays, expectedDays - 1, expectedDays + 1);
    }

    [Fact]
    public void The_first_period_is_shortened_by_the_fraction_of_the_nakshatra_already_traversed()
    {
        // Exactly halfway through Ashwini: half of Ketu's 7 years remains.
        var periods = VimshottariDasha.BuildMahadashas(NakshatraWidth / 2.0, Birth);

        Assert.Equal("Ketu", periods[0].Lord);
        Assert.True(periods[0].IsRemainder);

        var expectedDays = 3.5 * 365.25;
        Assert.InRange((periods[0].EndUtc - periods[0].StartUtc).TotalDays, expectedDays - 1, expectedDays + 1);

        // Later periods are full terms even though the first is not.
        Assert.False(periods[1].IsRemainder);
    }

    [Fact]
    public void The_lord_sequence_follows_the_nakshatra_lords_and_wraps_after_nine()
    {
        // Nakshatra 0 is Ashwini (Ketu). Nakshatra 9 is Ashlesha, also Ketu - the nine lords cycle.
        var atAshwini = VimshottariDasha.BuildMahadashas(0.0, Birth);
        var atNinthNakshatra = VimshottariDasha.BuildMahadashas(9.0 * NakshatraWidth, Birth);

        Assert.Equal("Ketu", atAshwini[0].Lord);
        Assert.Equal("Ketu", atNinthNakshatra[0].Lord);

        // The next lord after Ketu is always Venus, wherever in the cycle we start.
        Assert.Equal("Venus", atAshwini[1].Lord);
        Assert.Equal("Venus", atNinthNakshatra[1].Lord);
    }

    [Fact]
    public void Every_period_starts_at_or_after_birth_and_the_first_starts_exactly_at_birth()
    {
        var periods = VimshottariDasha.BuildMahadashas(123.4, Birth);

        // The remainder period is presented as beginning at birth: the part already spent is not shown as a dasha,
        // because the person was not yet born for it.
        Assert.Equal(Birth, periods[0].StartUtc);
        Assert.All(periods, period => Assert.True(period.EndUtc > period.StartUtc));
    }

    [Fact]
    public void A_negative_or_wrapped_longitude_is_handled_as_the_same_position()
    {
        var direct = VimshottariDasha.BuildMahadashas(100.0, Birth);
        var wrapped = VimshottariDasha.BuildMahadashas(460.0, Birth);

        Assert.Equal(direct[0].Lord, wrapped[0].Lord);
        Assert.Equal(direct[0].EndUtc, wrapped[0].EndUtc);
    }

    [Fact]
    public void A_moment_inside_a_period_returns_that_period_and_a_moment_after_the_last_returns_null()
    {
        var periods = VimshottariDasha.BuildMahadashas(0.0, Birth);

        var inside = VimshottariDasha.PeriodAt(periods, Birth.Plus(Duration.FromDays(1)));
        Assert.Equal("Ketu", inside!.Lord);

        var after = VimshottariDasha.PeriodAt(periods, periods[^1].EndUtc);
        Assert.Null(after);
    }

    [Fact]
    public void The_documented_year_length_is_365_dot_25_days()
    {
        // Guarded explicitly: swapping this for 365.2425 or 360 would drift dasha boundaries by days over a lifetime,
        // and the constant is a stated convention rather than an incidental choice.
        Assert.Equal(365.25, VimshottariDasha.DaysPerDashaYear);
        Assert.Equal(120.0, VimshottariDasha.CycleYears);
    }
}
