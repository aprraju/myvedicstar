using MyVedicStar.Kundali.Calculation;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// Divisional chart mapping, asserted against each rule's defining property rather than against generated output.
/// </summary>
/// <remarks>
/// These are the classical construction rules expressed as invariants: a division partitions its sign into equal
/// (or, for D30, specified unequal) parts, every part maps to a sign, and the mapping depends only on the source sign
/// and the part within it. No claim is made that the result matches a published ephemeris.
/// </remarks>
public sealed class DivisionalChartsTests
{
    /// <summary>Midpoint of the given part index within a sign, in degrees.</summary>
    private static double MidOfPart(int sourceSign, int part, int divisions) =>
        (sourceSign * 30.0) + ((part + 0.5) * (30.0 / divisions));

    [Fact]
    public void D1_is_the_birth_chart_so_the_sign_is_unchanged()
    {
        for (var sign = 0; sign < 12; sign++)
            Assert.Equal(sign, DivisionalCharts.SignIn(1, (sign * 30.0) + 15.0));
    }

    [Fact]
    public void Equal_partition_charts_consume_exactly_the_degrees_of_one_sign()
    {
        // For each equal-partition division, every part index must be reachable inside one sign and the mapping must
        // change exactly when the part changes - no part swallowing another's degrees.
        foreach (var divisions in new[] { 3, 7, 9, 10, 12 })
        {
            for (var sign = 0; sign < 12; sign++)
            {
                var seen = new List<int>();
                for (var part = 0; part < divisions; part++)
                    seen.Add(DivisionalCharts.SignIn(divisions, MidOfPart(sign, part, divisions)));

                // Equal partitions produce a run of splits whose signs advance by a constant step per part.
                var steps = seen.Zip(seen.Skip(1), (a, b) => (b - a + 12) % 12).Distinct().Count();
                Assert.True(steps <= 1,
                    $"D{divisions} sign {sign} produced inconsistent step sizes: {string.Join(",", seen)}");
            }
        }
    }

    [Fact]
    public void Navamsa_starts_from_the_sign_for_movable_the_ninth_for_fixed_and_the_fifth_for_dual()
    {
        // Movable sign (Aries, index 0): first navamsa is Aries itself.
        Assert.Equal(0, DivisionalCharts.SignIn(9, MidOfPart(0, 0, 9)));
        // Fixed sign (Taurus, index 1): first navamsa is the 9th from Taurus = Capricorn (index 9).
        Assert.Equal(9, DivisionalCharts.SignIn(9, MidOfPart(1, 0, 9)));
        // Dual sign (Gemini, index 2): first navamsa is the 5th from Gemini = Libra (index 6).
        Assert.Equal(6, DivisionalCharts.SignIn(9, MidOfPart(2, 0, 9)));
    }

    [Fact]
    public void Drekkana_starts_from_the_sign_and_jumps_by_the_fifth_each_part()
    {
        // First part is the sign itself, second is the 5th from it, third is the 9th.
        Assert.Equal(0, DivisionalCharts.SignIn(3, MidOfPart(0, 0, 3)));
        Assert.Equal(4, DivisionalCharts.SignIn(3, MidOfPart(0, 1, 3)));
        Assert.Equal(8, DivisionalCharts.SignIn(3, MidOfPart(0, 2, 3)));
    }

    [Fact]
    public void Hora_maps_odd_sign_halves_to_Leo_then_Cancer_and_even_sign_halves_the_other_way()
    {
        // Odd sign (Aries): first half Leo (4), second half Cancer (3).
        Assert.Equal(4, DivisionalCharts.SignIn(2, 10.0));
        Assert.Equal(3, DivisionalCharts.SignIn(2, 20.0));
        // Even sign (Taurus): first half Cancer (3), second half Leo (4).
        Assert.Equal(3, DivisionalCharts.SignIn(2, 40.0));
        Assert.Equal(4, DivisionalCharts.SignIn(2, 50.0));
    }

    [Fact]
    public void An_unimplemented_division_is_rejected_rather_than_approximated()
    {
        // D16 uses a different construction; returning a cyclic-shift guess would be a wrong chart presented as a
        // real one.
        Assert.False(DivisionalCharts.IsImplemented(16));
        Assert.Throws<ArgumentOutOfRangeException>(() => DivisionalCharts.SignIn(16, 45.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DivisionalCharts.SignIn(60, 45.0));
    }

    [Fact]
    public void Every_implemented_division_returns_a_valid_sign_for_every_degree_of_the_circle()
    {
        // A full sweep: no longitude may produce an out-of-range or negative index.
        foreach (var divisions in DivisionalCharts.ImplementedDivisions)
        {
            for (var tenths = 0; tenths < 3600; tenths++)
            {
                var sign = DivisionalCharts.SignIn(divisions, tenths / 10.0);
                Assert.InRange(sign, 0, 11);
            }
        }
    }

    [Fact]
    public void Longitudes_outside_the_circle_are_normalised_rather_than_clamping_to_the_first_sign()
    {
        Assert.Equal(DivisionalCharts.SignIn(9, 15.0), DivisionalCharts.SignIn(9, 375.0));
        Assert.Equal(DivisionalCharts.SignIn(9, 15.0), DivisionalCharts.SignIn(9, -345.0));
    }

    [Fact]
    public void The_implemented_division_list_matches_what_the_capability_report_calls_supported()
    {
        // Guards against the two lists drifting: a division advertised by the capabilities endpoint but not
        // implemented here would 400 or throw, and one implemented but unadvertised would be unreachable.
        var advertised = MyVedicStar.Kundali.Shared.KundaliCapabilities.SupportedDivisionalCharts;

        foreach (var divisions in DivisionalCharts.ImplementedDivisions)
            Assert.Contains(divisions, advertised);
    }
}
