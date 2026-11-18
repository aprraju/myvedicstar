using MyVedicStar.Kundali.Calculation;
using NodaTime;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// Yogini and Chara. Asserted against the systems' defining rules, not against our output, and with their limits
/// stated in the assertions themselves.
/// </summary>
public sealed class YoginiAndCharaDashaTests
{
    private static readonly Instant Birth = Instant.FromUtc(1990, 4, 12, 6, 45);
    private const double NakshatraWidth = 360.0 / 27.0;

    [Fact]
    public void A_full_yogini_cycle_totals_36_years_of_365_dot_25_days()
    {
        var periods = YoginiDasha.BuildPeriods(0.0, Birth);

        Assert.Equal(8, periods.Count);

        var totalDays = (periods[^1].EndUtc - periods[0].StartUtc).TotalDays;
        var expected = 36.0 * 365.25;
        Assert.InRange(totalDays, expected - 1, expected + 1);
    }

    [Fact]
    public void The_yogini_period_lengths_are_one_to_eight_years_and_sum_to_the_cycle()
    {
        // The defining property: eight periods of 1..8 years, which is what makes the total 36.
        var periods = YoginiDasha.BuildPeriods(0.0, Birth);

        var lengths = periods
            .Select(p => YoginiDasha.YearsOf(p.Name))
            .Order()
            .ToArray();

        Assert.Equal([1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0], lengths);
        Assert.Equal(36.0, lengths.Sum());
    }

    [Fact]
    public void A_moon_on_a_nakshatra_boundary_starts_a_whole_period_not_a_remainder()
    {
        var periods = YoginiDasha.BuildPeriods(0.0, Birth);

        Assert.False(periods[0].IsRemainder);

        var expectedDays = YoginiDasha.YearsOf(periods[0].Name) * 365.25;
        Assert.InRange((periods[0].EndUtc - periods[0].StartUtc).TotalDays, expectedDays - 1, expectedDays + 1);
    }

    [Fact]
    public void Halfway_through_a_nakshatra_leaves_half_of_the_first_period()
    {
        var periods = YoginiDasha.BuildPeriods(NakshatraWidth / 2.0, Birth);

        Assert.True(periods[0].IsRemainder);

        var expectedDays = YoginiDasha.YearsOf(periods[0].Name) * 365.25 / 2.0;
        Assert.InRange((periods[0].EndUtc - periods[0].StartUtc).TotalDays, expectedDays - 1, expectedDays + 1);
    }

    [Fact]
    public void Yogini_periods_are_contiguous_and_each_name_appears_exactly_once_per_cycle()
    {
        var periods = YoginiDasha.BuildPeriods(87.3, Birth);

        for (var i = 1; i < periods.Count; i++)
            Assert.Equal(periods[i - 1].EndUtc, periods[i].StartUtc);

        Assert.Equal(8, periods.Select(p => p.Name).Distinct().Count());
    }

    [Fact]
    public void The_yogini_and_vimshottari_cycles_share_a_year_length_but_not_a_period()
    {
        // 36 does not divide 120: three Yogini cycles cover 108 years, not 120, so the two systems drift apart by
        // 12 years per Vimshottari cycle. An earlier version of this test asserted 3 x 36 = 120 and was simply wrong.
        Assert.NotEqual(VimshottariDasha.CycleYears, YoginiDasha.CycleYears * 3.0);
        Assert.Equal(108.0, YoginiDasha.CycleYears * 3.0);

        // The year length IS shared, so the two timelines cannot disagree about how long a year is.
        Assert.Equal(VimshottariDasha.DaysPerDashaYear, YoginiDasha.DaysPerDashaYear);
    }

    [Fact]
    public void An_unknown_yogini_period_name_is_rejected_rather_than_treated_as_zero()
    {
        Assert.Throws<ArgumentException>(() => YoginiDasha.YearsOf("Rahu"));
    }

    [Fact]
    public void The_chara_sign_progression_advances_one_sign_at_a_time_and_covers_all_twelve()
    {
        var progression = CharaDasha.BuildSignProgression(0, Birth);

        Assert.Equal(12, progression.Count);
        Assert.Equal("Aries", progression[0].Sign);
        Assert.Equal("Pisces", progression[^1].Sign);
        Assert.Equal(12, progression.Select(p => p.Sign).Distinct().Count());
    }

    [Fact]
    public void The_chara_progression_wraps_from_the_starting_sign_rather_than_stopping()
    {
        var progression = CharaDasha.BuildSignProgression(9, Birth);

        // Capricorn onward: wraps through Pisces and back to Aries.
        Assert.Equal("Capricorn", progression[0].Sign);
        Assert.Equal("Pisces", progression[2].Sign);
        Assert.Equal("Aries", progression[3].Sign);
    }

    [Fact]
    public void Chara_periods_are_contiguous_and_do_not_overlap()
    {
        var progression = CharaDasha.BuildSignProgression(4, Birth);

        for (var i = 1; i < progression.Count; i++)
            Assert.Equal(progression[i - 1].EndUtc, progression[i].StartUtc);
    }

    [Fact]
    public void An_out_of_range_sign_index_or_non_positive_duration_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CharaDasha.BuildSignProgression(12, Birth));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharaDasha.BuildSignProgression(-1, Birth));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharaDasha.BuildSignProgression(0, Birth, 0));
    }
}
