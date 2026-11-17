using MyVedicStar.Kundali.Shared;

namespace MyVedicStar.Kundali.Tests.Unit;

/// <summary>
/// ADR-0016 section 3 is a contract, not prose: these tests fail if an unsupported capability is silently dropped or
/// reclassified as supported. Removing an entry requires an ADR change, so the failure here is the intended guard.
/// </summary>
public sealed class KundaliCapabilityBoundaryTests
{
    [Fact]
    public void D60_is_never_a_supported_divisional()
    {
        Assert.Contains(60, KundaliCapabilities.ProvisionalDivisionalCharts);
        Assert.DoesNotContain(60, KundaliCapabilities.SupportedDivisionalCharts);
        Assert.False(KundaliCapabilities.IsSupportedDivisional(60));
        Assert.True(KundaliCapabilities.IsProvisionalDivisional(60));
    }

    [Fact]
    public void D9_is_the_smallest_definitively_supported_division()
    {
        Assert.Contains(9, KundaliCapabilities.SupportedDivisionalCharts);

        // A D-n cut is 30/n degrees, so a *higher* n is a narrower cut. D9 is the finest division that is still
        // supported definitively: every supported divisional must therefore have n <= 9.
        foreach (var divisions in KundaliCapabilities.SupportedDivisionalCharts)
            Assert.True(divisions <= 9,
                $"D{divisions} is finer than D9, so D9 is not the finest definitively supported division.");

        // D60 is finer still and must never be presented as a definitive fact.
        Assert.DoesNotContain(60, KundaliCapabilities.SupportedDivisionalCharts);
    }

    [Fact]
    public void Shadbala_reports_chesta_as_unsupported_and_never_as_supported()
    {
        Assert.Contains("Chesta", KundaliCapabilities.UnsupportedShadbalaComponents);
        Assert.DoesNotContain("Chesta", KundaliCapabilities.SupportedShadbalaComponents);

        // The other five classical balas stay supported, so Shadbala is partial rather than absent.
        Assert.Equal(5, KundaliCapabilities.SupportedShadbalaComponents.Count);
        Assert.Contains("Sthana", KundaliCapabilities.SupportedShadbalaComponents);
        Assert.Contains("Dig", KundaliCapabilities.SupportedShadbalaComponents);
        Assert.Contains("Kala", KundaliCapabilities.SupportedShadbalaComponents);
        Assert.Contains("Naisargika", KundaliCapabilities.SupportedShadbalaComponents);
        Assert.Contains("Drik", KundaliCapabilities.SupportedShadbalaComponents);
    }

    [Fact]
    public void Lunar_node_convention_is_mean_not_true()
    {
        Assert.Equal("MeanNode", KundaliCapabilities.LunarNodeConvention);
    }

    [Theory]
    [InlineData(1799, 12, 31)]
    [InlineData(2201, 1, 1)]
    [InlineData(1500, 6, 1)]
    public void Birth_dates_outside_the_supported_range_are_refused(int year, int month, int day)
    {
        Assert.False(KundaliCapabilities.IsSupportedBirthDate(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(1800, 1, 1)]
    [InlineData(2000, 6, 15)]
    [InlineData(2200, 12, 31)]
    public void Birth_dates_inside_the_supported_range_are_accepted(int year, int month, int day)
    {
        Assert.True(KundaliCapabilities.IsSupportedBirthDate(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Published_report_names_the_unsupported_capabilities_so_nothing_degrades_silently()
    {
        var report = KundaliCapabilities.Report();

        Assert.Contains(60, report.ProvisionalDivisionalCharts);
        Assert.Contains("Chesta", report.UnsupportedShadbalaComponents);
        Assert.Equal("MeanNode", report.LunarNodeConvention);

        // At least one human-readable entry must name each mandated item: D60, Chesta, and pada boundaries.
        Assert.Contains(report.UnsupportedCapabilities, c => c.Contains("D60", StringComparison.Ordinal));
        Assert.Contains(report.UnsupportedCapabilities, c => c.Contains("Chesta", StringComparison.Ordinal));
        Assert.Contains(report.UnsupportedCapabilities, c => c.Contains("pada", StringComparison.OrdinalIgnoreCase));
    }
}
