using MyVedicStar.Kundali.Calculation;
using NodaTime;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// Antara and pratyantar decomposition. Asserted against the proportional rule itself, not against our output.
/// </summary>
public sealed class VimshottariSubPeriodTests
{
    private static readonly Instant Start = Instant.FromUtc(2020, 1, 1, 0, 0);
    private static readonly Instant End = Instant.FromUtc(2040, 1, 1, 0, 0);

    [Fact]
    public void Sub_periods_are_contiguous_and_span_exactly_the_parent()
    {
        var subs = VimshottariSubPeriods.Build("Venus", Start, End);

        Assert.Equal(9, subs.Count);
        Assert.Equal(Start, subs[0].StartUtc);

        for (var i = 1; i < subs.Count; i++)
            Assert.Equal(subs[i - 1].EndUtc, subs[i].StartUtc);

        // The last sub-period ends at the parent's end, not near it: accumulated rounding must not leave a gap.
        Assert.Equal(End, subs[^1].EndUtc);
    }

    [Fact]
    public void The_sequence_starts_with_the_parent_lord()
    {
        // The antara of a Venus mahadasha begins with Venus, then follows the cycle order.
        var subs = VimshottariSubPeriods.Build("Venus", Start, End);

        Assert.Equal("Venus", subs[0].Lord);
        Assert.Equal("Sun", subs[1].Lord);
    }

    [Fact]
    public void Each_sub_period_is_proportional_to_its_lords_share_of_the_cycle()
    {
        var subs = VimshottariSubPeriods.Build("Venus", Start, End);
        var parentDays = (End - Start).TotalDays;

        // Venus is 20 of 120 years, so its antara is one sixth of the parent.
        var venusDays = (subs[0].EndUtc - subs[0].StartUtc).TotalDays;
        Assert.InRange(venusDays, (parentDays / 6.0) - 1, (parentDays / 6.0) + 1);

        // Ketu is 7 of 120.
        var ketu = subs.Single(s => s.Lord == "Ketu");
        var ketuDays = (ketu.EndUtc - ketu.StartUtc).TotalDays;
        Assert.InRange(ketuDays, (parentDays * 7.0 / 120.0) - 1, (parentDays * 7.0 / 120.0) + 1);
    }

    [Fact]
    public void An_empty_or_inverted_parent_yields_no_sub_periods_rather_than_a_negative_span()
    {
        Assert.Empty(VimshottariSubPeriods.Build("Venus", End, Start));
        Assert.Empty(VimshottariSubPeriods.Build("Venus", Start, Start));
    }

    [Fact]
    public void An_unknown_lord_is_rejected_rather_than_quietly_treated_as_zero()
    {
        Assert.Throws<ArgumentException>(() => VimshottariSubPeriods.Build("Pluto", Start, End));
    }

    [Fact]
    public void The_path_has_three_levels_and_each_nests_inside_its_parent()
    {
        var mahadashas = VimshottariDasha.BuildMahadashas(87.3, Instant.FromUtc(1990, 4, 12, 6, 45));
        var probe = mahadashas[1].StartUtc.Plus(Duration.FromDays(365));

        var path = VimshottariSubPeriods.PathAt(mahadashas, probe);

        Assert.Equal(3, path.Count);
        // Outermost first, and each level lies within the one before it.
        Assert.True(path[1].StartUtc >= path[0].StartUtc && path[1].EndUtc <= path[0].EndUtc);
        Assert.True(path[2].StartUtc >= path[1].StartUtc && path[2].EndUtc <= path[1].EndUtc);
        Assert.Equal(mahadashas[1].Lord, path[0].Lord);
    }

    [Fact]
    public void An_instant_outside_every_mahadasha_returns_no_path_rather_than_a_partial_one()
    {
        var mahadashas = VimshottariDasha.BuildMahadashas(87.3, Instant.FromUtc(1990, 4, 12, 6, 45));

        var path = VimshottariSubPeriods.PathAt(mahadashas, mahadashas[^1].EndUtc.Plus(Duration.FromDays(1)));

        // A pratyantar shown without its parents would misrepresent the chart, so nothing is returned.
        Assert.Empty(path);
    }
}
